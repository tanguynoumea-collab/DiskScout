using System.IO;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Microsoft.Win32;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// Synthetic-fixture tests for <see cref="ResidueScanner"/>.
/// Filesystem and shortcut tests use a per-test temp directory injected via the internal
/// constructor; registry tests write transient subkeys under HKCU\Software\DiskScoutTest_{Guid}
/// and clean them up in <see cref="Dispose"/>.
/// </summary>
[Trait("Category", "Integration")]
public class ResidueScannerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ILogger _logger;
    private readonly List<string> _hkcuKeysToCleanup = new();

    public ResidueScannerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DiskScoutResidueTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
    }

    public void Dispose()
    {
        foreach (var subKey in _hkcuKeysToCleanup)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false); }
            catch { /* best-effort */ }
        }
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    private string CreateDir(params string[] parts)
    {
        var path = Path.Combine(new[] { _tempRoot }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private string CreateFile(string path, byte[]? content = null)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, content ?? new byte[1024]);
        return path;
    }

    private ResidueScanner BuildFsOnlyScanner(string[] fsRoots, string[]? shortcutRoots = null)
        => new ResidueScanner(
            _logger,
            defaultFsRoots: fsRoots,
            defaultShortcutRoots: shortcutRoots ?? Array.Empty<string>(),
            includeRegistry: false,
            includeServices: false,
            includeScheduledTasks: false,
            includeShellExtensions: false,
            includeMsiPatches: false);

    [Fact]
    public async Task Filesystem_FuzzyMatchesAdobeFolders_AndIgnoresControlFolder()
    {
        // Synthetic fake AppData hierarchy.
        var fakeLocal = CreateDir("AppData", "Local");
        var fakeRoaming = CreateDir("AppData", "Roaming");
        var adobeLocal = CreateDir("AppData", "Local", "Adobe");
        CreateFile(Path.Combine(adobeLocal, "settings.dat"), new byte[2048]);
        var adobeRoaming = CreateDir("AppData", "Roaming", "Adobe");
        CreateFile(Path.Combine(adobeRoaming, "user.json"), new byte[512]);
        var documents = CreateDir("Documents");
        CreateFile(Path.Combine(documents, "letter.doc"));

        var scanner = BuildFsOnlyScanner(new[] { fakeLocal, fakeRoaming, documents });

        var target = new ResidueScanTarget(
            DisplayName: "Acrobat",
            Publisher: "Adobe",
            InstallLocation: null,
            RegistryKeyName: null);

        var findings = await scanner.ScanAsync(target, installTrace: null, progress: null, default);

        findings.Should().Contain(f => f.Category == ResidueCategory.Filesystem &&
                                       f.Path.Equals(adobeLocal, StringComparison.OrdinalIgnoreCase));
        findings.Should().Contain(f => f.Category == ResidueCategory.Filesystem &&
                                       f.Path.Equals(adobeRoaming, StringComparison.OrdinalIgnoreCase));
        findings.Should().NotContain(f => f.Path.Contains("Documents", StringComparison.OrdinalIgnoreCase));
        findings.Where(f => f.Category == ResidueCategory.Filesystem)
                .Should().OnlyContain(f => f.Source == ResidueSource.NameHeuristic);
        findings.First(f => f.Category == ResidueCategory.Filesystem).Reason
                .Should().Contain("fuzzy match", "the reason must be a grep-stable heuristic explanation")
                .And.Contain("Adobe");
    }

    [Fact]
    public async Task Filesystem_RejectsPathsCoveredByResiduePathSafetyWhitelist()
    {
        // Create a fake "System32" subfolder with a publisher-matching subdirectory.
        // ResiduePathSafety should reject everything under System32 even if fuzzy matches.
        // We mimic the path by creating a folder whose absolute path contains "\Windows\System32".
        var fakeSystemRoot = CreateDir("Windows", "System32");
        CreateDir("Windows", "System32", "Microsoft");
        CreateFile(Path.Combine(fakeSystemRoot, "Microsoft", "stub.dll"), new byte[1024]);

        var scanner = BuildFsOnlyScanner(new[] { fakeSystemRoot });

        var target = new ResidueScanTarget(
            DisplayName: "Microsoft",
            Publisher: "Microsoft",
            InstallLocation: null,
            RegistryKeyName: null);

        var findings = await scanner.ScanAsync(target, installTrace: null, progress: null, default);

        findings.Should().NotContain(f => f.Path.Contains(@"\Windows\System32",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TraceMatch_ReportsOnlyExistingPaths_WithHighConfidence()
    {
        var stillThereDir = CreateDir("synthetic", "StillThere");
        var stillThere = CreateFile(Path.Combine(stillThereDir, "file.dll"), new byte[4096]);
        var removed = Path.Combine(_tempRoot, "synthetic", "Removed", "gone.dll");
        // Note: we deliberately do NOT create 'removed'.

        var trace = new InstallTrace(
            Header: new InstallTraceHeader(
                TraceId: "test-trace",
                TrackerVersion: "1.0",
                StartedUtc: DateTime.UtcNow.AddMinutes(-10),
                StoppedUtc: DateTime.UtcNow.AddMinutes(-5),
                InstallerCommandLine: "fake-installer.exe",
                InstallerProductHint: "FakeProduct"),
            Events: new[]
            {
                new InstallTraceEvent(InstallTraceEventKind.FileCreated, stillThere, DateTime.UtcNow.AddMinutes(-9)),
                new InstallTraceEvent(InstallTraceEventKind.FileCreated, removed,    DateTime.UtcNow.AddMinutes(-8)),
            });

        // Filesystem-only scanner; we won't use FS heuristic match (target.Publisher won't match).
        var scanner = BuildFsOnlyScanner(new[] { _tempRoot });
        var target = new ResidueScanTarget(
            DisplayName: "ZZZUnmatchableProduct",
            Publisher: "ZZZUnmatchablePublisher",
            InstallLocation: null,
            RegistryKeyName: null);

        var findings = await scanner.ScanAsync(target, installTrace: trace, progress: null, default);

        findings.Should().Contain(f =>
            f.Source == ResidueSource.TraceMatch &&
            f.Trust == ResidueTrustLevel.HighConfidence &&
            string.Equals(f.Path, stillThere, StringComparison.OrdinalIgnoreCase));
        findings.Should().NotContain(f =>
            string.Equals(f.Path, removed, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Registry_FindsTargetSubkey_AndEmitsMediumConfidenceNameHeuristic()
    {
        var testRootName = "DiskScoutTest_" + Guid.NewGuid().ToString("N");
        _hkcuKeysToCleanup.Add("Software\\" + testRootName);

        // Write a synthetic SOFTWARE\<Publisher>\<DisplayName> sub-tree.
        using (var pubKey = Registry.CurrentUser.CreateSubKey(
            $"Software\\{testRootName}\\JetBrains\\Rider 2024.3", writable: true))
        {
            pubKey!.SetValue("InstallPath", @"C:\Test\JetBrains\Rider");
        }

        // Build a scanner that only searches our test prefix (no FS, no shortcuts).
        var scanner = new ResidueScanner(
            _logger,
            defaultFsRoots: Array.Empty<string>(),
            defaultShortcutRoots: Array.Empty<string>(),
            includeRegistry: true,
            includeServices: false,
            includeScheduledTasks: false,
            includeShellExtensions: false,
            includeMsiPatches: false,
            registryTestPrefix: $"Software\\{testRootName}");

        var target = new ResidueScanTarget(
            DisplayName: "Rider 2024.3",
            Publisher: "JetBrains",
            InstallLocation: null,
            RegistryKeyName: "Rider 2024.3");

        var findings = await scanner.ScanAsync(target, installTrace: null, progress: null, default);

        findings.Should().Contain(f =>
            f.Category == ResidueCategory.Registry &&
            f.Trust == ResidueTrustLevel.MediumConfidence &&
            f.Source == ResidueSource.NameHeuristic &&
            f.Path.Contains("JetBrains", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Shortcuts_MatchByDisplayNameSubstring_CaseInsensitive()
    {
        var startMenu = CreateDir("StartMenu");
        var lnk1 = Path.Combine(startMenu, "Adobe Acrobat.lnk");
        var lnk2 = Path.Combine(startMenu, "Word 2024.lnk");
        File.WriteAllBytes(lnk1, new byte[256]);
        File.WriteAllBytes(lnk2, new byte[256]);

        var scanner = new ResidueScanner(
            _logger,
            defaultFsRoots: Array.Empty<string>(),
            defaultShortcutRoots: new[] { startMenu },
            includeRegistry: false,
            includeServices: false,
            includeScheduledTasks: false,
            includeShellExtensions: false,
            includeMsiPatches: false);

        var target = new ResidueScanTarget(
            DisplayName: "Acrobat",
            Publisher: "Adobe",
            InstallLocation: null,
            RegistryKeyName: null);

        var findings = await scanner.ScanAsync(target, installTrace: null, progress: null, default);

        findings.Should().Contain(f =>
            f.Category == ResidueCategory.Shortcut &&
            string.Equals(f.Path, lnk1, StringComparison.OrdinalIgnoreCase));
        findings.Should().NotContain(f => string.Equals(f.Path, lnk2, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancellation_PropagatesWithinTwoSeconds()
    {
        // Build a deep folder fixture so the scan does meaningful work.
        for (int i = 0; i < 50; i++)
        {
            var sub = CreateDir($"deep-{i}", "Adobe");
            for (int j = 0; j < 10; j++) CreateFile(Path.Combine(sub, $"f{j}.bin"), new byte[1024]);
        }

        var scanner = BuildFsOnlyScanner(new[] { _tempRoot });
        var target = new ResidueScanTarget("Acrobat", "Adobe", null, null);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel so any cancellation check fires immediately

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Func<Task> act = () => scanner.ScanAsync(target, null, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}
