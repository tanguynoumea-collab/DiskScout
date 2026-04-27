using System.Diagnostics;
using System.IO;
using DiskScout.Helpers;
using DiskScout.Models;
using Microsoft.Win32;
using Serilog;

namespace DiskScout.Services;

// NOTE (Plan 10-02): the public interfaces IServiceEnumerator and IScheduledTaskEnumerator
// were promoted from internal nested types here to dedicated files
// (Services/IServiceEnumerator.cs, Services/IScheduledTaskEnumerator.cs) so the new
// Phase-10 MachineSnapshotProvider + matchers can consume them as first-class injectable
// seams. The concrete production implementations (WmiServiceEnumerator,
// SchTasksEnumerator) remain package-private inside this file and are exposed via the
// static factory methods CreateDefaultServiceEnumerator() / CreateDefaultScheduledTaskEnumerator()
// for App.xaml.cs DI wiring (Plan 10-04).

/// <summary>
/// Post-uninstall residue scanner — scans seven surfaces for artifacts left behind by a uninstalled
/// program. See <see cref="IResidueScanner"/> for the contract and CONTEXT.md D-02 for the design
/// intent ("Revo Pro level": driver + deep scan + publisher rules).
/// </summary>
/// <remarks>
/// Every emitted <see cref="ResidueFinding"/> passes through
/// <see cref="ResiduePathSafety.IsSafeToPropose"/> (filesystem/registry) or
/// <see cref="ResiduePathSafety.IsSafeServiceName"/> (services) before being added to the result.
/// Per-category exceptions are caught and logged at Warning level — one branch's failure must
/// never abort the whole scan (see CONTEXT.md "degrades gracefully on permission errors").
/// </remarks>
public sealed class ResidueScanner : IResidueScanner
{
    private const double FuzzyThreshold = 0.7;

    /// <summary>
    /// HKCU/HKLM subkeys to probe. Exposed as a constant so tests and reviewers can grep
    /// the exact registry surface area covered by this scanner.
    /// </summary>
    private static readonly string[] PublisherRegistryBaseKeys =
    {
        @"SOFTWARE\{Publisher}",
        @"SOFTWARE\{Publisher}\{DisplayName}",
        @"SOFTWARE\Wow6432Node\{Publisher}",
        @"SOFTWARE\Wow6432Node\{Publisher}\{DisplayName}",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{RegistryKeyName}",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{DisplayName}.exe",
    };

    /// <summary>Replicates the four-view enumeration pattern from RegistryInstalledProgramsScanner.</summary>
    private static readonly (Microsoft.Win32.RegistryHive Hive, RegistryView View, string Prefix)[] HiveViews =
    {
        (Microsoft.Win32.RegistryHive.LocalMachine, RegistryView.Registry64, @"HKLM"),
        (Microsoft.Win32.RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM"),
        (Microsoft.Win32.RegistryHive.CurrentUser,  RegistryView.Registry64, @"HKCU"),
        (Microsoft.Win32.RegistryHive.CurrentUser,  RegistryView.Registry32, @"HKCU"),
    };

    private readonly ILogger _logger;
    private readonly string[] _filesystemRoots;
    private readonly string[] _shortcutRoots;
    private readonly bool _includeRegistry;
    private readonly bool _includeServices;
    private readonly bool _includeScheduledTasks;
    private readonly bool _includeShellExtensions;
    private readonly bool _includeMsiPatches;

    /// <summary>
    /// When non-null, the registry scan walks ONLY this single HKCU prefix (test mode) instead of
    /// the four real hive views. Lets tests assert behaviour without polluting the user's machine.
    /// </summary>
    private readonly string? _registryTestPrefix;

    /// <summary>Override for the Windows\Installer scan root (defaults to %WINDIR%\Installer).</summary>
    private readonly string _windowsInstallerPath;

    /// <summary>Service enumerator (defaults to ServiceController.GetServices()).</summary>
    private readonly IServiceEnumerator _serviceEnumerator;

    /// <summary>Scheduled-task enumerator (defaults to schtasks /query /fo CSV /v parser).</summary>
    private readonly IScheduledTaskEnumerator _scheduledTaskEnumerator;

    /// <summary>
    /// When non-null, the shell-extension scan walks this single HKCU prefix (under which the
    /// test creates synthetic CLSID\{guid}\InprocServer32 keys). When null, the real
    /// HKLM\SOFTWARE\Classes\CLSID hive is enumerated. Test-only knob.
    /// </summary>
    private readonly string? _shellExtensionTestPrefix;

    /// <summary>Production constructor: uses real environment paths and full registry enumeration.</summary>
    public ResidueScanner(ILogger logger)
        : this(logger, defaultFsRoots: null, defaultShortcutRoots: null) { }

    /// <summary>
    /// Test-only / advanced constructor. Internal so tests (via [InternalsVisibleTo]) can inject
    /// synthetic roots and category flags. Defaults to real environment paths when arguments are null.
    /// </summary>
    internal ResidueScanner(
        ILogger logger,
        string[]? defaultFsRoots,
        string[]? defaultShortcutRoots,
        bool includeRegistry = true,
        bool includeServices = true,
        bool includeScheduledTasks = true,
        bool includeShellExtensions = true,
        bool includeMsiPatches = true,
        string? registryTestPrefix = null,
        string? windowsInstallerPath = null,
        IServiceEnumerator? serviceEnumerator = null,
        IScheduledTaskEnumerator? scheduledTaskEnumerator = null,
        string? shellExtensionTestPrefix = null)
    {
        _logger = logger;
        _filesystemRoots = defaultFsRoots ?? DefaultFilesystemRoots();
        _shortcutRoots = defaultShortcutRoots ?? DefaultShortcutRoots();
        _includeRegistry = includeRegistry;
        _includeServices = includeServices;
        _includeScheduledTasks = includeScheduledTasks;
        _includeShellExtensions = includeShellExtensions;
        _includeMsiPatches = includeMsiPatches;
        _registryTestPrefix = registryTestPrefix;
        _windowsInstallerPath = windowsInstallerPath ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer");
        _serviceEnumerator = serviceEnumerator ?? new WmiServiceEnumerator(logger);
        _scheduledTaskEnumerator = scheduledTaskEnumerator ?? new SchTasksEnumerator(logger);
        _shellExtensionTestPrefix = shellExtensionTestPrefix;
    }

    /// <summary>
    /// Returns the default production <see cref="IServiceEnumerator"/> (registry-backed
    /// enumeration of <c>HKLM\SYSTEM\CurrentControlSet\Services</c>). Wraps the
    /// package-private <c>WmiServiceEnumerator</c> nested type so App.xaml.cs (Plan
    /// 10-04) can inject the same default the scanner uses without creating a
    /// duplicate concrete class.
    /// </summary>
    public static IServiceEnumerator CreateDefaultServiceEnumerator(ILogger logger)
        => new WmiServiceEnumerator(logger);

    /// <summary>
    /// Returns the default production <see cref="IScheduledTaskEnumerator"/> (shells
    /// out to <c>schtasks.exe /query /fo CSV /v</c> with a 10s timeout). Wraps the
    /// package-private <c>SchTasksEnumerator</c> nested type so App.xaml.cs (Plan
    /// 10-04) can inject the same default the scanner uses without creating a
    /// duplicate concrete class.
    /// </summary>
    public static IScheduledTaskEnumerator CreateDefaultScheduledTaskEnumerator(ILogger logger)
        => new SchTasksEnumerator(logger);

    public Task<IReadOnlyList<ResidueFinding>> ScanAsync(
        ResidueScanTarget target,
        InstallTrace? installTrace,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<ResidueFinding>>(() =>
        {
            var findings = new List<ResidueFinding>(64);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Cross-referencing install trace...");
            SafeRun(() => ScanFromTrace(target, installTrace, findings, seen, cancellationToken),
                    ResidueCategory.Filesystem, "Trace");
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report("Scanning filesystem residue...");
            SafeRun(() => ScanFilesystem(target, findings, seen, cancellationToken),
                    ResidueCategory.Filesystem, "Filesystem");
            cancellationToken.ThrowIfCancellationRequested();

            if (_includeRegistry)
            {
                progress?.Report("Scanning registry residue...");
                SafeRun(() => ScanRegistry(target, findings, seen, cancellationToken),
                        ResidueCategory.Registry, "Registry");
                cancellationToken.ThrowIfCancellationRequested();
            }

            progress?.Report("Scanning Start menu / desktop shortcuts...");
            SafeRun(() => ScanShortcuts(target, findings, seen, cancellationToken),
                    ResidueCategory.Shortcut, "Shortcuts");
            cancellationToken.ThrowIfCancellationRequested();

            // Task 3 categories — left as no-ops in this commit; populated by Task 3 implementation.
            if (_includeMsiPatches)
            {
                progress?.Report("Scanning MSI patches...");
                SafeRun(() => ScanMsiPatches(target, findings, seen, cancellationToken),
                        ResidueCategory.MsiPatch, "MsiPatches");
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (_includeServices)
            {
                progress?.Report("Scanning Windows services...");
                SafeRun(() => ScanServices(target, findings, seen, cancellationToken),
                        ResidueCategory.Service, "Services");
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (_includeScheduledTasks)
            {
                progress?.Report("Scanning scheduled tasks...");
                SafeRun(() => ScanScheduledTasks(target, findings, seen, cancellationToken),
                        ResidueCategory.ScheduledTask, "ScheduledTasks");
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (_includeShellExtensions)
            {
                progress?.Report("Scanning shell extensions...");
                SafeRun(() => ScanShellExtensions(target, findings, seen, cancellationToken),
                        ResidueCategory.ShellExtension, "ShellExtensions");
                cancellationToken.ThrowIfCancellationRequested();
            }

            _logger.Information("ResidueScanner: {Count} findings for {Target}",
                findings.Count, target.DisplayName);
            return (IReadOnlyList<ResidueFinding>)findings.AsReadOnly();
        }, cancellationToken);
    }

    /// <summary>
    /// Wraps a per-category scan in a try/catch — any unhandled exception logs a warning and
    /// allows remaining categories to continue. Exception: <see cref="OperationCanceledException"/>
    /// is rethrown so cancellation propagates immediately.
    /// </summary>
    private void SafeRun(Action scan, ResidueCategory category, string label)
    {
        try
        {
            scan();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Residue scan failed for category {Category} ({Label})", category, label);
        }
    }

    /// <summary>
    /// Adds a finding only if it passes the whitelist and has not been emitted already
    /// for the same Category+Path pair.
    /// </summary>
    private bool TryAdd(List<ResidueFinding> findings, HashSet<string> seen, ResidueFinding finding)
    {
        if (!ResiduePathSafety.IsSafeToPropose(finding.Path))
        {
            _logger.Debug("Suppressed by ResiduePathSafety whitelist: {Path}", finding.Path);
            return false;
        }
        var dedupKey = $"{finding.Category}|{finding.Path}";
        if (!seen.Add(dedupKey)) return false;
        findings.Add(finding);
        return true;
    }

    // ---------- Trace scan ----------

    private void ScanFromTrace(
        ResidueScanTarget target,
        InstallTrace? trace,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        if (trace is null) return;

        foreach (var ev in trace.Events)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(ev.Path)) continue;

            switch (ev.Kind)
            {
                case InstallTraceEventKind.FileCreated:
                case InstallTraceEventKind.FileModified:
                {
                    if (!File.Exists(ev.Path)) continue;
                    long size = TryGetFileSize(ev.Path);
                    TryAdd(findings, seen, new ResidueFinding(
                        Category: ResidueCategory.Filesystem,
                        Path: ev.Path,
                        SizeBytes: size,
                        Reason: $"InstallTrace recorded this file during install of '{target.DisplayName}', still present after uninstall.",
                        Trust: ResidueTrustLevel.HighConfidence,
                        Source: ResidueSource.TraceMatch));
                    break;
                }

                case InstallTraceEventKind.DirectoryCreated:
                {
                    if (!Directory.Exists(ev.Path)) continue;
                    long size = MeasureFolderBytes(ev.Path);
                    TryAdd(findings, seen, new ResidueFinding(
                        Category: ResidueCategory.Filesystem,
                        Path: ev.Path,
                        SizeBytes: size,
                        Reason: $"InstallTrace recorded this directory during install of '{target.DisplayName}', still present after uninstall.",
                        Trust: ResidueTrustLevel.HighConfidence,
                        Source: ResidueSource.TraceMatch));
                    break;
                }

                case InstallTraceEventKind.RegistryKeyCreated:
                case InstallTraceEventKind.RegistryValueWritten:
                {
                    // Registry events are best-effort: the trace records the parent hive only
                    // (RegNotifyChangeKeyValue limitation, see Plan 09-01 SUMMARY). We surface
                    // the event so the registry walk below has supplementary context, but we
                    // don't synthesize a finding without a precise subkey path.
                    break;
                }
            }
        }
    }

    // ---------- Filesystem scan ----------

    private void ScanFilesystem(
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        foreach (var root in _filesystemRoots)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warning(ex, "Filesystem scan unauthorized at {Root}", root);
                continue;
            }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException ex) { _logger.Warning(ex, "Filesystem scan IO error at {Root}", root); continue; }

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(child);
                if (string.IsNullOrEmpty(name)) continue;

                if (!FuzzyMatcher.IsMatch(name, target.Publisher, target.DisplayName, FuzzyThreshold))
                    continue;

                long size = MeasureFolderBytes(child);
                TryAdd(findings, seen, new ResidueFinding(
                    Category: ResidueCategory.Filesystem,
                    Path: child,
                    SizeBytes: size,
                    Reason: $"Folder name fuzzy match Publisher='{target.Publisher}' DisplayName='{target.DisplayName}'.",
                    Trust: ResidueTrustLevel.MediumConfidence,
                    Source: ResidueSource.NameHeuristic));
            }
        }
    }

    // ---------- Registry scan ----------

    private void ScanRegistry(
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        // Test-mode shortcut: walk a single HKCU prefix directly.
        if (_registryTestPrefix is not null)
        {
            ScanRegistryTestPrefix(target, findings, seen, ct);
            return;
        }

        foreach (var (hive, view, hivePrefix) in HiveViews)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                foreach (var template in PublisherRegistryBaseKeys)
                {
                    ct.ThrowIfCancellationRequested();
                    var subPath = ExpandTemplate(template, target);
                    if (subPath is null) continue;

                    using var key = root.OpenSubKey(subPath, writable: false);
                    if (key is null) continue;

                    var fullPath = $"{hivePrefix}\\{subPath}";
                    TryAdd(findings, seen, new ResidueFinding(
                        Category: ResidueCategory.Registry,
                        Path: fullPath,
                        SizeBytes: 0,
                        Reason: $"Registry key '{subPath}' still present in {hivePrefix} ({view}).",
                        Trust: ResidueTrustLevel.MediumConfidence,
                        Source: ResidueSource.NameHeuristic));
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Registry residue scan failed for {Hive}/{View}", hive, view);
            }
        }
    }

    private void ScanRegistryTestPrefix(
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        try
        {
            using var prefixKey = Registry.CurrentUser.OpenSubKey(_registryTestPrefix!, writable: false);
            if (prefixKey is null) return;

            // For test mode: walk every immediate subkey and recursively report any that
            // fuzzy-match the publisher / display name.
            WalkRegistry(prefixKey, $"HKCU\\{_registryTestPrefix}", target, findings, seen, ct, depth: 0);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Registry test-prefix scan failed for {Prefix}", _registryTestPrefix);
        }
    }

    private void WalkRegistry(
        RegistryKey key,
        string fullPath,
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct,
        int depth)
    {
        if (depth > 4) return;
        ct.ThrowIfCancellationRequested();

        var name = fullPath.Substring(fullPath.LastIndexOf('\\') + 1);
        bool fuzzyHit = FuzzyMatcher.IsMatch(name, target.Publisher, target.DisplayName, FuzzyThreshold) ||
                        (target.Publisher is not null && name.Contains(target.Publisher, StringComparison.OrdinalIgnoreCase)) ||
                        (target.DisplayName is not null && name.Contains(target.DisplayName, StringComparison.OrdinalIgnoreCase));

        if (fuzzyHit && depth > 0)
        {
            TryAdd(findings, seen, new ResidueFinding(
                Category: ResidueCategory.Registry,
                Path: fullPath,
                SizeBytes: 0,
                Reason: $"Registry subkey '{name}' fuzzy-matches Publisher='{target.Publisher}' DisplayName='{target.DisplayName}'.",
                Trust: ResidueTrustLevel.MediumConfidence,
                Source: ResidueSource.NameHeuristic));
        }

        foreach (var subName in key.GetSubKeyNames())
        {
            ct.ThrowIfCancellationRequested();
            using var sub = key.OpenSubKey(subName, writable: false);
            if (sub is null) continue;
            WalkRegistry(sub, $"{fullPath}\\{subName}", target, findings, seen, ct, depth + 1);
        }
    }

    // ---------- Shortcut scan ----------

    private void ScanShortcuts(
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        foreach (var root in _shortcutRoots)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            IEnumerable<string> lnks;
            try
            {
                lnks = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warning(ex, "Shortcut scan unauthorized at {Root}", root);
                continue;
            }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException ex) { _logger.Warning(ex, "Shortcut scan IO error at {Root}", root); continue; }

            foreach (var lnk in lnks)
            {
                ct.ThrowIfCancellationRequested();
                var fileNameNoExt = Path.GetFileNameWithoutExtension(lnk);
                if (string.IsNullOrEmpty(fileNameNoExt)) continue;

                bool match = (!string.IsNullOrEmpty(target.DisplayName) &&
                              fileNameNoExt.Contains(target.DisplayName, StringComparison.OrdinalIgnoreCase)) ||
                             FuzzyMatcher.IsMatch(fileNameNoExt, target.Publisher, target.DisplayName, FuzzyThreshold);

                if (!match) continue;

                long size = TryGetFileSize(lnk);
                TryAdd(findings, seen, new ResidueFinding(
                    Category: ResidueCategory.Shortcut,
                    Path: lnk,
                    SizeBytes: size,
                    Reason: $"Shortcut name '{fileNameNoExt}' matches DisplayName='{target.DisplayName}' or fuzzy publisher.",
                    Trust: ResidueTrustLevel.MediumConfidence,
                    Source: ResidueSource.NameHeuristic));
            }
        }
    }

    // ---------- MSI patch scan ----------

    /// <summary>
    /// Walks <c>%WINDIR%\Installer\*.msp</c> for MSI patches whose filename's first 8 hex
    /// characters match the target's RegistryKeyName GUID prefix (heuristic — full PatchInfo
    /// metadata extraction is intentionally NOT performed to avoid linking against MsiOpenDatabase).
    /// </summary>
    private void ScanMsiPatches(
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_windowsInstallerPath) || !Directory.Exists(_windowsInstallerPath))
            return;

        // Strip leading '{' and any non-hex chars from RegistryKeyName to get a comparable prefix.
        var keyName = target.RegistryKeyName ?? string.Empty;
        var guidPrefix = ExtractGuidPrefix(keyName);
        if (string.IsNullOrEmpty(guidPrefix)) return;

        IEnumerable<string> patches;
        try
        {
            patches = Directory.EnumerateFiles(_windowsInstallerPath, "*.msp", SearchOption.TopDirectoryOnly);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warning(ex, "MSI patch scan unauthorized at {Path}", _windowsInstallerPath);
            return;
        }
        catch (IOException ex) { _logger.Warning(ex, "MSI patch scan IO error at {Path}", _windowsInstallerPath); return; }

        foreach (var patch in patches)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(patch);
            if (string.IsNullOrEmpty(fileName)) continue;

            if (!fileName.StartsWith(guidPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            long size = TryGetFileSize(patch);
            TryAdd(findings, seen, new ResidueFinding(
                Category: ResidueCategory.MsiPatch,
                Path: patch,
                SizeBytes: size,
                Reason: $"MSI patch '{fileName}.msp' shares GUID prefix '{guidPrefix}' with target's RegistryKeyName.",
                Trust: ResidueTrustLevel.MediumConfidence,
                Source: ResidueSource.NameHeuristic));
        }
    }

    private static string ExtractGuidPrefix(string keyName)
    {
        // Accept GUIDs with or without enclosing braces. Take the first 8 hex chars after any '{'.
        var trimmed = keyName.TrimStart('{');
        var prefix = new char[8];
        int filled = 0;
        foreach (var c in trimmed)
        {
            if (filled == 8) break;
            if (Uri.IsHexDigit(c)) prefix[filled++] = c;
            else if (c != '-') break;
        }
        return filled == 8 ? new string(prefix) : string.Empty;
    }

    // ---------- Service scan ----------

    /// <summary>
    /// Iterates every Windows service from <see cref="IServiceEnumerator"/>. A service is a
    /// candidate if its Name contains the publisher (or its DisplayName contains the program
    /// DisplayName, or its ImagePath starts with InstallLocation) AND
    /// <see cref="ResiduePathSafety.IsSafeServiceName"/> returns true.
    /// </summary>
    private void ScanServices(
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        foreach (var svc in _serviceEnumerator.EnumerateServices())
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(svc.Name)) continue;
            if (!ResiduePathSafety.IsSafeServiceName(svc.Name)) continue;

            bool nameMatch = !string.IsNullOrWhiteSpace(target.Publisher) &&
                             svc.Name.Contains(target.Publisher!, OIC);
            bool displayMatch = !string.IsNullOrWhiteSpace(target.DisplayName) &&
                                !string.IsNullOrWhiteSpace(svc.DisplayName) &&
                                svc.DisplayName.Contains(target.DisplayName, OIC);
            bool binaryMatch = !string.IsNullOrWhiteSpace(target.InstallLocation) &&
                               !string.IsNullOrWhiteSpace(svc.BinaryPath) &&
                               svc.BinaryPath!.StartsWith(target.InstallLocation!, OIC);

            if (!(nameMatch || displayMatch || binaryMatch)) continue;

            // ResiduePathSafety also checks the BinaryPath — if the binary lives in a critical
            // directory, refuse to propose deletion of the service.
            if (!string.IsNullOrWhiteSpace(svc.BinaryPath) && !ResiduePathSafety.IsSafeToPropose(svc.BinaryPath!))
            {
                _logger.Debug("Service {Name} skipped: binary {Path} hits whitelist", svc.Name, svc.BinaryPath);
                continue;
            }

            string reason = nameMatch
                ? $"Service name '{svc.Name}' contains publisher '{target.Publisher}'."
                : displayMatch
                    ? $"Service display name '{svc.DisplayName}' contains '{target.DisplayName}'."
                    : $"Service binary path lives under InstallLocation '{target.InstallLocation}'.";

            TryAdd(findings, seen, new ResidueFinding(
                Category: ResidueCategory.Service,
                Path: $"Service:{svc.Name}",
                SizeBytes: 0,
                Reason: reason,
                Trust: ResidueTrustLevel.MediumConfidence,
                Source: ResidueSource.NameHeuristic));
        }
    }

    // ---------- Scheduled task scan ----------

    /// <summary>
    /// Iterates every scheduled task from <see cref="IScheduledTaskEnumerator"/>. A task is a
    /// candidate if its Author contains the publisher, its TaskPath contains the publisher, OR
    /// its ActionPath starts with InstallLocation. Whitelist-filtered before emit.
    /// </summary>
    private void ScanScheduledTasks(
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        foreach (var task in _scheduledTaskEnumerator.EnumerateTasks())
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(task.TaskPath)) continue;

            bool authorMatch = !string.IsNullOrWhiteSpace(target.Publisher) &&
                               !string.IsNullOrWhiteSpace(task.Author) &&
                               task.Author!.Contains(target.Publisher!, OIC);
            bool taskPathMatch = !string.IsNullOrWhiteSpace(target.Publisher) &&
                                 task.TaskPath.Contains(target.Publisher!, OIC);
            bool actionPathMatch = !string.IsNullOrWhiteSpace(target.InstallLocation) &&
                                   !string.IsNullOrWhiteSpace(task.ActionPath) &&
                                   task.ActionPath!.StartsWith(target.InstallLocation!, OIC);

            if (!(authorMatch || taskPathMatch || actionPathMatch)) continue;

            // Reject if the action path is on the critical-system whitelist.
            if (!string.IsNullOrWhiteSpace(task.ActionPath) && !ResiduePathSafety.IsSafeToPropose(task.ActionPath!))
            {
                _logger.Debug("Scheduled task {TaskPath} skipped: action {Action} hits whitelist",
                    task.TaskPath, task.ActionPath);
                continue;
            }

            string reason = authorMatch
                ? $"Task author '{task.Author}' contains publisher '{target.Publisher}'."
                : taskPathMatch
                    ? $"Task path '{task.TaskPath}' contains publisher '{target.Publisher}'."
                    : $"Task action path lives under InstallLocation '{target.InstallLocation}'.";

            TryAdd(findings, seen, new ResidueFinding(
                Category: ResidueCategory.ScheduledTask,
                Path: $"ScheduledTask:{task.TaskPath}",
                SizeBytes: 0,
                Reason: reason,
                Trust: ResidueTrustLevel.MediumConfidence,
                Source: ResidueSource.NameHeuristic));
        }
    }

    // ---------- Shell extension scan ----------

    /// <summary>
    /// Walks every CLSID under HKLM\SOFTWARE\Classes\CLSID and inspects its InprocServer32
    /// default value. A finding is emitted if the InprocServer32 DLL path lives under the
    /// target's InstallLocation. In test mode, walks the injected HKCU prefix instead.
    /// </summary>
    private void ScanShellExtensions(
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target.InstallLocation)) return;
        if (!ResiduePathSafety.IsSafeToPropose(target.InstallLocation!))
        {
            // If the install location itself is on the critical whitelist, refuse to scan
            // shell extensions for it (no third-party shell ext lives at the OS layer).
            return;
        }

        if (_shellExtensionTestPrefix is not null)
        {
            ScanShellExtensionsAtPrefix(
                Registry.CurrentUser,
                _shellExtensionTestPrefix,
                hivePrefixForReporting: $"HKCU\\{_shellExtensionTestPrefix}",
                target,
                findings,
                seen,
                ct);
            return;
        }

        try
        {
            using var root = RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, RegistryView.Registry64);
            ScanShellExtensionsAtPrefix(
                root,
                @"SOFTWARE\Classes\CLSID",
                hivePrefixForReporting: @"HKLM\SOFTWARE\Classes\CLSID",
                target,
                findings,
                seen,
                ct);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Shell extension scan failed at HKLM\\SOFTWARE\\Classes\\CLSID");
        }
    }

    private void ScanShellExtensionsAtPrefix(
        RegistryKey baseKey,
        string subPath,
        string hivePrefixForReporting,
        ResidueScanTarget target,
        List<ResidueFinding> findings,
        HashSet<string> seen,
        CancellationToken ct)
    {
        using var clsidRoot = baseKey.OpenSubKey(subPath, writable: false);
        if (clsidRoot is null) return;

        foreach (var clsidName in clsidRoot.GetSubKeyNames())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var clsidKey = clsidRoot.OpenSubKey(clsidName, writable: false);
                if (clsidKey is null) continue;
                using var inproc = clsidKey.OpenSubKey("InprocServer32", writable: false);
                if (inproc is null) continue;

                var dllPath = inproc.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(dllPath)) continue;

                // Strip surrounding quotes that some installers add.
                dllPath = dllPath.Trim().Trim('"');
                if (string.IsNullOrEmpty(dllPath)) continue;

                if (!dllPath.StartsWith(target.InstallLocation!, StringComparison.OrdinalIgnoreCase)) continue;

                var fullClsidPath = $"{hivePrefixForReporting}\\{clsidName}";
                TryAdd(findings, seen, new ResidueFinding(
                    Category: ResidueCategory.ShellExtension,
                    Path: fullClsidPath,
                    SizeBytes: 0,
                    Reason: $"Shell extension CLSID points InprocServer32 at '{dllPath}' under InstallLocation '{target.InstallLocation}'.",
                    Trust: ResidueTrustLevel.MediumConfidence,
                    Source: ResidueSource.NameHeuristic));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to inspect CLSID subkey {Clsid}", clsidName);
            }
        }
    }

    // ---------- Default enumerator implementations ----------

    /// <summary>
    /// Default implementation of <see cref="IServiceEnumerator"/> backed by direct enumeration
    /// of <c>HKLM\SYSTEM\CurrentControlSet\Services</c>. Each subkey is a service; DisplayName
    /// and ImagePath are values on that subkey.
    /// </summary>
    /// <remarks>
    /// We enumerate the registry rather than calling <c>ServiceController.GetServices()</c> to
    /// avoid taking a dependency on the <c>System.ServiceProcess.ServiceController</c> NuGet
    /// package (project policy: no new packages — see Plan 09-01 SUMMARY).
    /// </remarks>
    private sealed class WmiServiceEnumerator : IServiceEnumerator
    {
        private readonly ILogger _logger;
        public WmiServiceEnumerator(ILogger logger) => _logger = logger;

        public IEnumerable<(string Name, string DisplayName, string? BinaryPath)> EnumerateServices()
        {
            RegistryKey? root = null;
            RegistryKey? servicesKey = null;
            string[] subKeyNames;
            try
            {
                root = RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine, RegistryView.Registry64);
                servicesKey = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", writable: false);
                if (servicesKey is null) yield break;
                subKeyNames = servicesKey.GetSubKeyNames();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to open HKLM\\SYSTEM\\CurrentControlSet\\Services");
                root?.Dispose();
                servicesKey?.Dispose();
                yield break;
            }

            try
            {
                foreach (var serviceName in subKeyNames)
                {
                    string? displayName = null;
                    string? binaryPath = null;
                    try
                    {
                        using var key = servicesKey.OpenSubKey(serviceName, writable: false);
                        if (key is null) continue;
                        displayName = key.GetValue("DisplayName") as string;
                        binaryPath = key.GetValue("ImagePath") as string;
                        if (!string.IsNullOrWhiteSpace(binaryPath))
                        {
                            binaryPath = binaryPath.Trim();
                            // ImagePath often starts with '\??\' or is quoted; strip prefix + quotes.
                            if (binaryPath.StartsWith(@"\??\", StringComparison.Ordinal)) binaryPath = binaryPath[4..];
                            binaryPath = binaryPath.Trim('"');
                            var spaceIdx = binaryPath.IndexOf(' ');
                            if (spaceIdx > 0 && binaryPath[0] != '"') binaryPath = binaryPath[..spaceIdx];
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to read service {Name}", serviceName);
                        continue;
                    }
                    yield return (serviceName, displayName ?? serviceName, binaryPath);
                }
            }
            finally
            {
                servicesKey.Dispose();
                root.Dispose();
            }
        }
    }

    /// <summary>
    /// Default implementation of <see cref="IScheduledTaskEnumerator"/> that shells out to
    /// <c>schtasks.exe /query /fo CSV /v</c> and parses the verbose CSV output.
    /// Times out after 10 seconds.
    /// </summary>
    private sealed class SchTasksEnumerator : IScheduledTaskEnumerator
    {
        private readonly ILogger _logger;
        public SchTasksEnumerator(ILogger logger) => _logger = logger;

        public IEnumerable<(string TaskPath, string? Author, string? ActionPath)> EnumerateTasks()
        {
            string output;
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", "/query /fo CSV /v")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) yield break;
                output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(10_000))
                {
                    try { p.Kill(); } catch { /* best-effort */ }
                    _logger.Warning("schtasks.exe timed out after 10s; results may be incomplete");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "schtasks.exe failed to launch");
                yield break;
            }

            // Parse CSV: first line = headers, subsequent lines = tasks.
            // Columns of interest: "TaskName", "Author", "Task To Run".
            using var reader = new StringReader(output);
            string? headerLine = reader.ReadLine();
            if (string.IsNullOrEmpty(headerLine)) yield break;

            var headers = ParseCsvLine(headerLine);
            int idxTaskName = headers.FindIndex(h => string.Equals(h, "TaskName", StringComparison.OrdinalIgnoreCase));
            int idxAuthor   = headers.FindIndex(h => string.Equals(h, "Author",   StringComparison.OrdinalIgnoreCase));
            int idxAction   = headers.FindIndex(h =>
                string.Equals(h, "Task To Run", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(h, "Action",      StringComparison.OrdinalIgnoreCase));

            if (idxTaskName < 0) yield break;

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("\"TaskName\"", StringComparison.OrdinalIgnoreCase)) continue; // repeat headers per folder

                var fields = ParseCsvLine(line);
                if (fields.Count <= idxTaskName) continue;
                var taskPath = fields[idxTaskName];
                if (string.IsNullOrWhiteSpace(taskPath)) continue;

                string? author = idxAuthor >= 0 && idxAuthor < fields.Count ? fields[idxAuthor] : null;
                string? action = idxAction >= 0 && idxAction < fields.Count ? fields[idxAction] : null;

                yield return (taskPath, author, action);
            }
        }

        /// <summary>Minimal RFC-4180 CSV line parser for schtasks output (no embedded newlines expected).</summary>
        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>(16);
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else current.Append(c);
                }
                else
                {
                    if (c == ',') { result.Add(current.ToString()); current.Clear(); }
                    else if (c == '"') inQuotes = true;
                    else current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }
    }

    // ---------- Helpers ----------

    private static string? ExpandTemplate(string template, ResidueScanTarget target)
    {
        // Template uses {Publisher} / {DisplayName} / {RegistryKeyName} placeholders.
        // If a required placeholder is missing/empty for this target, return null (skip).
        var result = template;

        if (template.Contains("{Publisher}"))
        {
            if (string.IsNullOrWhiteSpace(target.Publisher)) return null;
            result = result.Replace("{Publisher}", SanitizeRegistrySegment(target.Publisher!));
        }
        if (template.Contains("{DisplayName}"))
        {
            if (string.IsNullOrWhiteSpace(target.DisplayName)) return null;
            result = result.Replace("{DisplayName}", SanitizeRegistrySegment(target.DisplayName));
        }
        if (template.Contains("{RegistryKeyName}"))
        {
            if (string.IsNullOrWhiteSpace(target.RegistryKeyName)) return null;
            result = result.Replace("{RegistryKeyName}", SanitizeRegistrySegment(target.RegistryKeyName!));
        }
        return result;
    }

    private static string SanitizeRegistrySegment(string segment)
    {
        // Strip backslashes that would break the subkey path; trim whitespace.
        return segment.Replace('\\', '_').Replace('/', '_').Trim();
    }

    private static string[] DefaultFilesystemRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var publicProfile = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";
        var localTemp = Path.Combine(
            Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? local,
            "Temp");
        return new[] { local, roaming, common, pf, pf86, publicProfile, localTemp }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] DefaultShortcutRoots()
    {
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        }
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    }

    private static long MeasureFolderBytes(string path)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { /* per-file best-effort */ }
            }
        }
        catch { /* per-folder best-effort */ }
        return total;
    }

    private static long TryGetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
