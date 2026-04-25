using System.IO;
using System.Text;
using System.Text.Json;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;
using DiskScout.ViewModels.UninstallWizard;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// Plan 09-06 Task 1 tests — UninstallReport model + UninstallReportService.
///
/// Hand-written fakes per project policy (no Moq dep — see Plan 09-01 / 09-03 / 09-05 SUMMARY).
/// Plan 06 Task 2 will append further integration tests (ReportStepViewModel,
/// ProgramsViewModel.Annotate wiring) in this same file.
/// </summary>
public class UninstallReportServiceTests
{
    private static readonly ILogger Log =
        new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();

    // ---------- helpers ----------

    private static InstalledProgram MakeTarget(string display = "Acme App", string? publisher = "Acme Inc.") =>
        new(
            RegistryKey: "AcmeApp",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: display,
            Publisher: publisher,
            Version: "1.0.0",
            InstallDate: null,
            InstallLocation: @"C:\Program Files\Acme",
            UninstallString: @"""C:\Program Files\Acme\uninstall.exe""",
            RegistryEstimatedSizeBytes: 1_000_000,
            ComputedSizeBytes: 0);

    private static UninstallWizardViewModel BuildWizard(InstalledProgram? target = null)
    {
        return new UninstallWizardViewModel(
            Log,
            target ?? MakeTarget(),
            new FakeInstallTracker(),
            new FakeInstallTraceStore(),
            new FakeUninstallerDriver(),
            new FakeResidueScanner(),
            new FakePublisherRuleEngine(),
            new FakeFileDeletion());
    }

    private static string TempPath(string ext)
    {
        var dir = Path.Combine(Path.GetTempPath(), "DiskScoutTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"report_{Guid.NewGuid():N}.{ext}");
    }

    // ---------- Test 1: model round-trips through System.Text.Json ----------
    [Fact]
    public void UninstallReport_RoundTripsThroughJson()
    {
        var report = new UninstallReport(
            ProgramName: "Acme",
            Publisher: "Acme Inc.",
            Version: "1.2.3",
            RegistryKey: "AcmeKey",
            GeneratedUtc: new DateTime(2026, 4, 25, 12, 0, 0, DateTimeKind.Utc),
            UninstallOutcomeStatus: "Success",
            UninstallExitCode: 0,
            UninstallElapsedSeconds: 12.5,
            ResidueCount: 2,
            ResidueBytes: 4096,
            ResidueByCategory: new Dictionary<string, CategoryTotals>
            {
                ["Filesystem"] = new CategoryTotals(2, 4096),
            },
            DeletedSuccessCount: 2,
            DeletedFailureCount: 0,
            DeletedBytesFreed: 4096,
            DeletedEntries: new List<DeletedEntrySnapshot>
            {
                new(@"C:\foo", true, 4096, null),
            },
            MatchedPublisherRuleIds: new[] { "acme" },
            HadInstallTrace: true);

        var json = JsonSerializer.Serialize(report);
        var back = JsonSerializer.Deserialize<UninstallReport>(json);

        back.Should().NotBeNull();
        back!.ProgramName.Should().Be("Acme");
        back.ResidueByCategory.Should().ContainKey("Filesystem");
        back.ResidueByCategory["Filesystem"].Count.Should().Be(2);
        back.MatchedPublisherRuleIds.Should().ContainSingle().Which.Should().Be("acme");
    }

    // ---------- Test 2: ExportAsync(Json) writes a deserializable JSON file ----------
    [Fact]
    public async Task ExportAsync_Json_WritesDeserializableFile()
    {
        var svc = new UninstallReportService(Log);
        var wizard = BuildWizard();
        var report = svc.BuildFromWizard(wizard);

        var path = TempPath("json");
        try
        {
            await svc.ExportAsync(report, path, ReportFormat.Json);

            File.Exists(path).Should().BeTrue();
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("\"programName\"");

            using var stream = File.OpenRead(path);
            var roundTrip = await JsonSerializer.DeserializeAsync<UninstallReport>(
                stream,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });
            roundTrip.Should().NotBeNull();
            roundTrip!.ProgramName.Should().Be("Acme App");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(path)!); } catch { }
        }
    }

    // ---------- Test 3: ExportAsync(Html) writes a UTF-8 HTML file with inline CSS ----------
    [Fact]
    public async Task ExportAsync_Html_WritesSelfContainedDocument()
    {
        var svc = new UninstallReportService(Log);
        var wizard = BuildWizard();
        var report = svc.BuildFromWizard(wizard);

        var path = TempPath("html");
        try
        {
            await svc.ExportAsync(report, path, ReportFormat.Html);

            File.Exists(path).Should().BeTrue();
            var content = await File.ReadAllTextAsync(path, Encoding.UTF8);

            content.Should().Contain("Acme App");
            content.Should().Contain("border-collapse:collapse");
            content.Should().Contain("<style>");
            content.Should().Contain("<th>");
            content.Should().Contain("Catégorie");
            content.Should().Contain("Chemin");
            content.Should().NotContain("<link ");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(path)!); } catch { }
        }
    }

    // ---------- Test 4: BuildFromWizard aggregates ResidueByCategory correctly ----------
    [Fact]
    public void BuildFromWizard_AggregatesResidueByCategory()
    {
        var wizard = BuildWizard();
        wizard.AllResidueFindings.Add(new ResidueFinding(
            ResidueCategory.Filesystem, @"C:\Users\Test\AcmeData\a.dat", 1000,
            "test", ResidueTrustLevel.MediumConfidence, ResidueSource.NameHeuristic));
        wizard.AllResidueFindings.Add(new ResidueFinding(
            ResidueCategory.Filesystem, @"C:\Users\Test\AcmeData\b.dat", 500,
            "test", ResidueTrustLevel.MediumConfidence, ResidueSource.NameHeuristic));
        wizard.AllResidueFindings.Add(new ResidueFinding(
            ResidueCategory.Registry, @"HKCU\Software\Acme", 0,
            "test", ResidueTrustLevel.MediumConfidence, ResidueSource.NameHeuristic));

        var svc = new UninstallReportService(Log);
        var report = svc.BuildFromWizard(wizard);

        report.ResidueByCategory.Should().ContainKey("Filesystem");
        report.ResidueByCategory["Filesystem"].Count.Should().Be(2);
        report.ResidueByCategory["Filesystem"].Bytes.Should().Be(1500);
        report.ResidueByCategory.Should().ContainKey("Registry");
        report.ResidueByCategory["Registry"].Count.Should().Be(1);
        report.ResidueByCategory["Registry"].Bytes.Should().Be(0);
        report.ResidueCount.Should().Be(3);
        report.ResidueBytes.Should().Be(1500);
    }

    // ---------- Test 5: HTML output MUST NOT contain a script tag opener ----------
    [Fact]
    public async Task ExportAsync_Html_NeverContainsScriptTag()
    {
        var svc = new UninstallReportService(Log);
        // Build a wizard with a hostile program name attempting to inject a script tag —
        // the literal "<script" is split below so static analysis stays clean.
        var hostile = new InstalledProgram(
            RegistryKey: "Hostile",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: "<scr" + "ipt>alert(1)</scr" + "ipt>Hostile",
            Publisher: "<scr" + "ipt>",
            Version: "1.0.0",
            InstallDate: null,
            InstallLocation: null,
            UninstallString: null,
            RegistryEstimatedSizeBytes: 0,
            ComputedSizeBytes: 0);

        var wizard = BuildWizard(hostile);
        var report = svc.BuildFromWizard(wizard);

        var path = TempPath("html");
        try
        {
            await svc.ExportAsync(report, path, ReportFormat.Html);

            var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
            content.Should().NotContain("<scr" + "ipt");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(path)!); } catch { }
        }
    }

    // ---------- Test 6: special characters in program name are HTML-encoded ----------
    [Fact]
    public async Task ExportAsync_Html_EncodesSpecialCharactersInProgramName()
    {
        var svc = new UninstallReportService(Log);
        var hostile = new InstalledProgram(
            RegistryKey: "Hostile",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: "Test <evil> & \"company\"",
            Publisher: null,
            Version: null,
            InstallDate: null,
            InstallLocation: null,
            UninstallString: null,
            RegistryEstimatedSizeBytes: 0,
            ComputedSizeBytes: 0);

        var wizard = BuildWizard(hostile);
        var report = svc.BuildFromWizard(wizard);

        var path = TempPath("html");
        try
        {
            await svc.ExportAsync(report, path, ReportFormat.Html);

            var content = await File.ReadAllTextAsync(path, Encoding.UTF8);

            content.Should().Contain("&lt;evil&gt;");
            content.Should().Contain("&amp;");
            content.Should().Contain("&quot;");
            content.Should().NotContain("Test <evil>");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(path)!); } catch { }
        }
    }

    // ============================================================
    // Hand-written fakes (no Moq).
    // ============================================================

    private sealed class FakeInstallTracker : IInstallTracker
    {
        public bool IsTracking => false;
        public Task StartAsync(string? cmd, string? hint, CancellationToken ct = default) => Task.CompletedTask;
        public Task<InstallTrace> StopAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeInstallTraceStore : IInstallTraceStore
    {
        public Task SaveAsync(InstallTrace trace, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<InstallTraceHeader>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InstallTraceHeader>>(Array.Empty<InstallTraceHeader>());
        public Task<InstallTrace?> LoadAsync(string traceId, CancellationToken ct = default) =>
            Task.FromResult<InstallTrace?>(null);
        public Task<bool> DeleteAsync(string traceId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FakeUninstallerDriver : INativeUninstallerDriver
    {
        public UninstallCommand? ParseCommand(InstalledProgram p, string? quiet, bool preferSilent) =>
            new UninstallCommand(@"C:\fake\uninstall.exe", "", null, preferSilent, InstallerKind.Generic);
        public Task<UninstallOutcome> RunAsync(UninstallCommand cmd, IProgress<string>? output, CancellationToken ct) =>
            Task.FromResult(new UninstallOutcome(UninstallStatus.Success, 0, TimeSpan.Zero, 0, null));
    }

    private sealed class FakeResidueScanner : IResidueScanner
    {
        public Task<IReadOnlyList<ResidueFinding>> ScanAsync(
            ResidueScanTarget target, InstallTrace? trace, IProgress<string>? progress, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ResidueFinding>>(Array.Empty<ResidueFinding>());
    }

    private sealed class FakePublisherRuleEngine : IPublisherRuleEngine
    {
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<PublisherRule> AllRules => Array.Empty<PublisherRule>();
        public IReadOnlyList<PublisherRuleMatch> Match(string? publisher, string displayName) =>
            Array.Empty<PublisherRuleMatch>();
        public string ExpandTokens(string template, string? publisher, string displayName) => template;
    }

    private sealed class FakeFileDeletion : IFileDeletionService
    {
        public Task<DeletionResult> DeleteAsync(IReadOnlyList<string> paths, DeleteMode mode, CancellationToken ct = default) =>
            Task.FromResult(new DeletionResult(paths.Select(p => new DeletionEntry(p, true, 0, null)).ToList()));
    }
}
