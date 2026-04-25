using System.IO;
using DiskScout.Helpers;
using DiskScout.Models;
using Microsoft.Win32;
using Serilog;

namespace DiskScout.Services;

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
        string? registryTestPrefix = null)
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
    }

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

    // ---------- Task 3 stubs (populated in next commit) ----------

    private void ScanMsiPatches(ResidueScanTarget target, List<ResidueFinding> findings,
        HashSet<string> seen, CancellationToken ct)
    {
        // Populated by Task 3 (MSI patch heuristic against C:\Windows\Installer\*.msp).
    }

    private void ScanServices(ResidueScanTarget target, List<ResidueFinding> findings,
        HashSet<string> seen, CancellationToken ct)
    {
        // Populated by Task 3 (System.ServiceProcess.ServiceController + IsSafeServiceName).
    }

    private void ScanScheduledTasks(ResidueScanTarget target, List<ResidueFinding> findings,
        HashSet<string> seen, CancellationToken ct)
    {
        // Populated by Task 3 (schtasks.exe /query /fo CSV /v parsing).
    }

    private void ScanShellExtensions(ResidueScanTarget target, List<ResidueFinding> findings,
        HashSet<string> seen, CancellationToken ct)
    {
        // Populated by Task 3 (HKLM\SOFTWARE\Classes\CLSID\*\InprocServer32 walk).
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
