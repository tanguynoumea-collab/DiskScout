using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// Phase 10 acceptance gate. Validates the AppData orphan-detection pipeline
/// (10-04) against the user's 365-item ProgramData audit corpus from a
/// production HP corporate machine (NinjaRMM + Zscaler + Matrix42 + Empirum +
/// Bitdefender + Splashtop + Autodesk + Office 365 + Teams + Claude + Python).
///
/// Two hard invariants:
///   (1) >= 95 % concordance (within 1 RiskLevel band) between engine and human.
///   (2) ZERO Critique-marked items proposed for Supprimer or CorbeilleOk.
///
/// Marked Trait("Category","Acceptance") so it can be excluded for fast cycles.
/// </summary>
public class CorpusAcceptanceTests
{
    private const string FixtureResourceName = "DiskScout.Tests.Fixtures.programdata_corpus_365.json";
    private const double ConcordanceTarget = 0.95;
    private const int MinCorpusItems = 365;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();

    /// <summary>
    /// Corpus item — schema mirrors tests/DiskScout.Tests/Fixtures/programdata_corpus_365.json.
    /// </summary>
    private sealed record CorpusItem(
        int Index,
        string Path,
        long SizeBytes,
        DateTime LastWriteUtc,
        string Verdict,             // "Aucun" | "Faible" | "Moyen" | "Eleve" | "Critique"
        string RecommendedAction,   // "Supprimer" | "CorbeilleOk" | "VerifierAvant" | "NePasToucher" | "Garder"
        string Reason);

    // ---------------------------------------------------------------------
    // Fakes for the acceptance test — one frozen MachineSnapshot reflecting
    // the user's corporate machine, alongside the real PathRuleEngine /
    // ParentContextAnalyzer / matchers / scorer / classifier.
    // ---------------------------------------------------------------------

    private sealed class FakeSnapshotProvider : IMachineSnapshotProvider
    {
        private readonly MachineSnapshot _snapshot;
        public FakeSnapshotProvider(MachineSnapshot snapshot) { _snapshot = snapshot; }
        public Task<MachineSnapshot> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);
        public void Invalidate() { /* no-op */ }
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    public async Task RemanentDetector_Should_Match_Manual_Audit_With_95_Percent_Concordance()
    {
        // ---- Step 1: load the corpus from embedded resource ----
        var corpus = LoadCorpus();
        corpus.Should().HaveCountGreaterThanOrEqualTo(MinCorpusItems,
            $"corpus fixture must ship at least {MinCorpusItems} items (the audit baseline).");

        // ---- Step 2: build the pipeline using REAL implementations ----
        var pipeline = await BuildRealPipelineAsync();

        // ---- Step 3: run every corpus item through the pipeline ----
        int agreed = 0;
        int criticalMisclassified = 0;
        int hardBlacklistAgreement = 0;
        int hardBlacklistDisagreement = 0;
        var disagreements = new List<string>();
        var criticalIssues = new List<string>();

        // Use a fixed, representative installed-programs list for the corporate
        // machine (does not need to be exhaustive — the matchers + path rules
        // do most of the work).
        var programs = BuildCorporatePrograms();

        foreach (var item in corpus)
        {
            var humanRisk = ParseRisk(item.Verdict);
            var node = SynthesizeNode(item);

            var result = await pipeline.EvaluateAsync(node, programs, CancellationToken.None);

            if (result is null)
            {
                // HardBlacklist suppression. The pipeline's strongest signal:
                // these are paths the user must NEVER see. They map cleanly to
                // human verdict=Critique (action=NePasToucher).
                if (humanRisk == RiskLevel.Critique)
                {
                    agreed++;
                    hardBlacklistAgreement++;
                }
                else
                {
                    // Engine over-suppressed (humans graded Eleve/Moyen/etc.).
                    // Still safe: HardBlacklist erring on the side of caution
                    // is acceptable per CONTEXT.md. Counts as 1-band agreement
                    // when human verdict is Eleve (Critique-1 = Eleve).
                    if (humanRisk == RiskLevel.Eleve)
                    {
                        agreed++;
                        hardBlacklistAgreement++;
                    }
                    else
                    {
                        hardBlacklistDisagreement++;
                        disagreements.Add(
                            $"[{item.Index}] {item.Path}: pipeline=HardBlacklist(Critique) human={humanRisk}");
                    }
                }
                continue;
            }

            // Within-1-band concordance.
            if (RisksAreWithinOneBand(result.Risk, humanRisk))
            {
                agreed++;
            }
            else
            {
                disagreements.Add(
                    $"[{item.Index}] {item.Path}: pipeline={result.Risk}/{result.Action} (score={result.ConfidenceScore}) human={humanRisk}/{item.RecommendedAction}");
            }

            // Hard invariant: NO Critique-marked items should be proposed for
            // Supprimer or CorbeilleOk by the engine.
            if (humanRisk == RiskLevel.Critique &&
                (result.Action == RecommendedAction.Supprimer ||
                 result.Action == RecommendedAction.CorbeilleOk))
            {
                criticalMisclassified++;
                criticalIssues.Add(
                    $"[{item.Index}] {item.Path}: pipeline action={result.Action} on human=Critique");
            }
        }

        double concordance = (double)agreed / corpus.Count;

        // Surface details on failure.
        var diagSummary = $"Corpus={corpus.Count}, Agreed={agreed}, Concordance={concordance:P1}, " +
                         $"HardBlacklistOK={hardBlacklistAgreement}, HardBlacklistKO={hardBlacklistDisagreement}, " +
                         $"CriticalMisclass={criticalMisclassified}";

        // CRITIQUE invariant — must be 0.
        criticalMisclassified.Should().Be(0,
            "the engine must NEVER propose Supprimer or CorbeilleOk on a Critique-graded path. " +
            $"{diagSummary}. First issues:\n  " +
            string.Join("\n  ", criticalIssues.Take(10)));

        // Concordance threshold.
        concordance.Should().BeGreaterThanOrEqualTo(ConcordanceTarget,
            $"engine must reach {ConcordanceTarget:P0} concordance with the manual audit. " +
            $"{diagSummary}. First disagreements:\n  " +
            string.Join("\n  ", disagreements.Take(10)));
    }

    [Fact(Skip = "Diagnostic only — un-skip locally to introspect the loaded path-rule set.")]
    public async Task Diagnostic_DumpRules()
    {
        var logger = new LoggerConfiguration().MinimumLevel.Debug().CreateLogger();
        var engine = new PathRuleEngine(logger);
        await engine.LoadAsync();
        var report = new System.Text.StringBuilder();
        report.AppendLine($"Total rules: {engine.AllRules.Count}");
        foreach (var r in engine.AllRules.Where(r => r.Id.Contains("empirum", StringComparison.OrdinalIgnoreCase)))
            report.AppendLine($"  Rule: {r.Id} pattern={r.PathPattern} cat={r.Category}");

        var hits = engine.Match(@"C:\ProgramData\_Empirum");
        report.AppendLine($"Match _Empirum: {hits.Count} hits");
        foreach (var h in hits) report.AppendLine($"  {h.RuleId} cat={h.Category}");

        var hits2 = engine.Match(@"C:\ProgramData\_Empirum\Adobe Systems");
        report.AppendLine($"Match _Empirum\\Adobe Systems: {hits2.Count} hits");
        foreach (var h in hits2) report.AppendLine($"  {h.RuleId} cat={h.Category}");

        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diskscout_engine_debug.txt"), report.ToString());
        true.Should().BeTrue();
    }

    [Fact(Skip = "Diagnostic only — un-skip locally to dump per-item disagreements when tuning the engine. Always passes; writes report to %TEMP%\\diskscout_corpus_diagnostic.txt.")]
    [Trait("Category", "Acceptance")]
    public async Task Diagnostic_Print_All_Disagreements()
    {
        var corpus = LoadCorpus();
        var pipeline = await BuildRealPipelineAsync();
        var programs = BuildCorporatePrograms();

        var report = new System.Text.StringBuilder();
        var critIssues = new System.Text.StringBuilder();
        int agreed = 0;
        int critMisclass = 0;
        foreach (var item in corpus)
        {
            var node = SynthesizeNode(item);
            var humanRisk = ParseRisk(item.Verdict);
            var result = await pipeline.EvaluateAsync(node, programs, CancellationToken.None);
            var engineRisk = result?.Risk ?? RiskLevel.Critique;
            var engineAction = result?.Action ?? RecommendedAction.NePasToucher;
            var engineScore = result?.ConfidenceScore ?? 0;
            var match = RisksAreWithinOneBand(engineRisk, humanRisk);
            if (match) agreed++;
            else
            {
                report.AppendLine($"[{item.Index,3}] {item.Path,-90} HUM={humanRisk,-9} ENG={engineRisk,-9}/{engineAction,-15} score={engineScore} ruleHits={(result?.TriggeredRules.Count ?? 0)} matchHits={(result?.MatchedSources.Count ?? 0)}");
            }
            if (humanRisk == RiskLevel.Critique && (engineAction == RecommendedAction.Supprimer || engineAction == RecommendedAction.CorbeilleOk))
            {
                critMisclass++;
                critIssues.AppendLine($"[{item.Index,3}] {item.Path,-90} CRIT-MISCLASS: ENG={engineRisk}/{engineAction} score={engineScore}");
            }
        }
        var concordance = (double)agreed / corpus.Count;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diskscout_corpus_diagnostic.txt");
        System.IO.File.WriteAllText(path, $"Concordance: {concordance:P1} ({agreed}/{corpus.Count}) CritMisclass: {critMisclass}\n\nCRITICAL ISSUES:\n{critIssues}\n\nDISAGREEMENTS:\n{report}");
        true.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    public void Corpus_Fixture_Has_Expected_Count_And_Distribution()
    {
        // Cheaper sanity test that doesn't run the pipeline. Confirms the
        // fixture itself is well-formed before the heavyweight test runs.
        var corpus = LoadCorpus();

        corpus.Should().HaveCountGreaterThanOrEqualTo(MinCorpusItems);

        var indices = corpus.Select(c => c.Index).ToHashSet();
        indices.Should().HaveCount(corpus.Count, "every index must be unique");

        // Verdicts must be one of the 5 valid strings.
        var validVerdicts = new HashSet<string> { "Aucun", "Faible", "Moyen", "Eleve", "Critique" };
        foreach (var item in corpus)
        {
            validVerdicts.Should().Contain(item.Verdict, $"item [{item.Index}] verdict invalid");
        }

        // Critique items dominate (>= 30 %) — the user's machine is full of
        // Windows OS components. Cross-check against the documented distribution.
        var critiqueShare = (double)corpus.Count(c => c.Verdict == "Critique") / corpus.Count;
        critiqueShare.Should().BeGreaterThan(0.30,
            "Critique items must be at least 30 % of the corpus per the user audit");
    }

    [Fact]
    [Trait("Category", "Acceptance")]
    public void Corpus_Fixture_All_Critique_Items_Map_To_Safe_Actions()
    {
        // Defensive: validates the fixture itself (not the engine). Every
        // Critique-graded item must have RecommendedAction in {NePasToucher, Garder}.
        var corpus = LoadCorpus();

        var safeCriticalActions = new HashSet<string> { "NePasToucher", "Garder" };
        var bad = corpus
            .Where(c => c.Verdict == "Critique" && !safeCriticalActions.Contains(c.RecommendedAction))
            .ToList();

        bad.Should().BeEmpty(
            "Critique items must always recommend NePasToucher or Garder. " +
            "Bad items: " + string.Join(", ", bad.Take(5).Select(b => $"[{b.Index}] {b.Path} -> {b.RecommendedAction}")));
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static List<CorpusItem> LoadCorpus()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(FixtureResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource not found: {FixtureResourceName}. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");

        var items = JsonSerializer.Deserialize<CorpusItem[]>(stream, JsonOptions);
        if (items is null || items.Length == 0)
            throw new InvalidOperationException("Corpus fixture deserialized to null/empty.");

        return items.ToList();
    }

    private async Task<AppDataOrphanPipeline> BuildRealPipelineAsync()
    {
        // Real PathRuleEngine — loads the 5 embedded JSONs from the App assembly.
        // We construct it via the production constructor; it's read-only post-load
        // so it's safe to share across test cases (xUnit instantiates the class
        // per test by default).
        var pathEngine = new PathRuleEngine(_logger);
        await pathEngine.LoadAsync();

        // Real ParentContextAnalyzer (no I/O, no async).
        var parentAnalyzer = new ParentContextAnalyzer();

        // Real PublisherAliasResolver (loads aliases.json from the App assembly).
        var aliasResolver = new PublisherAliasResolver(_logger);

        // Frozen MachineSnapshot reflecting the user's corporate machine.
        var snapshot = BuildCorporateSnapshot();
        var snapshotProvider = new FakeSnapshotProvider(snapshot);

        var scorer = new ConfidenceScorer();
        var classifier = new RiskLevelClassifier();
        var serviceMatcher = new ServiceMatcher(_logger);
        var driverMatcher = new DriverMatcher(_logger);
        var appxMatcher = new AppxMatcher(_logger);
        var registryMatcher = new RegistryMatcher(_logger);

        return new AppDataOrphanPipeline(
            _logger,
            pathEngine,
            parentAnalyzer,
            snapshotProvider,
            aliasResolver,
            scorer,
            classifier,
            serviceMatcher,
            driverMatcher,
            appxMatcher,
            registryMatcher);
    }

    private static FileSystemNode SynthesizeNode(CorpusItem item)
    {
        // Use the corpus's lastWriteUtc as-is (already > 365 days in the
        // fixture) so the residue bonuses fire on truly-stale items. The
        // pipeline's ProbeForBinaries check returns false for non-existent
        // folders → +10 bonus naturally applied.
        return new FileSystemNode(
            Id: item.Index,
            ParentId: null,
            Name: System.IO.Path.GetFileName(item.Path),
            FullPath: item.Path,
            Kind: FileSystemNodeKind.Directory,
            SizeBytes: item.SizeBytes,
            FileCount: 0,
            DirectoryCount: 0,
            LastModifiedUtc: item.LastWriteUtc,
            IsReparsePoint: false,
            Depth: 3);
    }

    /// <summary>
    /// Hand-curated installed-programs list reflecting the user's corporate
    /// machine. Not exhaustive — only programs whose Publisher / DisplayName /
    /// InstallLocation might be referenced by the corpus paths.
    /// </summary>
    private static IReadOnlyList<InstalledProgram> BuildCorporatePrograms()
    {
        return new List<InstalledProgram>
        {
            // Autodesk suite
            P("Autodesk Revit 2025", "Autodesk", "2025", null),
            P("Autodesk Revit Steel Connections 2025", "Autodesk", "2025", null),
            P("Autodesk Revit Interoperability 2025", "Autodesk", "2025", null),
            P("Autodesk Geospatial Coordinate Systems 15.01", "Autodesk", "15.01", null),
            P("Autodesk Identity Manager", "Autodesk", "6.17", null),
            P("Autodesk DWG TrueView 2022 - English", "Autodesk", "2022", null),
            P("Autodesk Inventor", "Autodesk", "2024", null),
            P("Autodesk Navisworks Manage 2024", "Autodesk", "2024", null),
            P("Autodesk Application Plugins", "Autodesk", "1.0", null),

            // BIM plugins
            P("BCF Managers 6.5 - Revit 2021 - 2024", "KUBUS", "6.5.5", null),
            P("DiRoots.One", "DiRoots", "2.0", null),
            P("DiRoots.ProSheets", "DiRoots", "2.0", null),
            P("PlaceMaker for Revit", "PlaceMaker", "1.0", null),
            P("Ideate Software", "Ideate", "1.0", null),
            P("Dynamo Core", "Dynamo", "2.16", null),
            P("BEXEL Manager", "BEXEL", "5.0", null),
            P("BIM Vision", "Datacomp", "2.30", null),

            // Corporate agents
            P("NinjaOne Agent", "NinjaOne", "5.7", null),
            P("Bitdefender Endpoint Security Tools", "Bitdefender", "7.9", null),
            P("Zscaler Client Connector", "Zscaler", "4.2", null),
            P("Matrix42 Empirum Client", "Matrix42", "23.1", null),
            P("Splashtop Streamer", "Splashtop", "3.5", null),
            P("Cisco AnyConnect Secure Mobility Client", "Cisco", "4.10", null),
            P("HP TechPulse SmartHealth", "HP", "1.0", null),
            P("Printix Client", "Printix", "2.0", null),

            // Microsoft ecosystem
            P("Microsoft Visual Studio Build Tools 2022", "Microsoft Corporation", "17.8", null),
            P("Microsoft 365 Apps for Enterprise", "Microsoft Corporation", "16.0", null),
            P("Microsoft Edge", "Microsoft Corporation", "121.0", null),
            P("Microsoft OneDrive", "Microsoft Corporation", "24.0", null),
            P("Microsoft Teams", "Microsoft Corporation", "1.7", null),
            P("Windows SDK", "Microsoft Corporation", "10.1.26100.7705", null),
            P("Microsoft .NET Runtime 8", "Microsoft Corporation", "8.0.21", null),
            P("Microsoft .NET Runtime 6", "Microsoft Corporation", "6.0.36", null),
            P("Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Corporation", "14.50", null),
            P("Microsoft Visual C++ 2013 Redistributable (x64)", "Microsoft Corporation", "12.0", null),
            P("Microsoft Visual C++ 2012 Redistributable (x64)", "Microsoft Corporation", "11.0", null),

            // OEM / drivers
            P("HP Display Center", "HP Inc.", "1.0", null),
            P("HP Touchpoint Analytics Client", "HP Inc.", "4.2", null),
            P("Intel Graphics Command Center", "Intel Corporation", "1.100", null),
            P("Intel Extreme Tuning Utility", "Intel Corporation", "7.13", null),
            P("Intel Management Engine Components", "Intel Corporation", "2335", null),
            P("NVIDIA Graphics Driver", "NVIDIA Corporation", "551.86", null),
            P("NVIDIA Control Panel", "NVIDIA Corporation", "8.1", null),

            // Productivity
            P("Claude Desktop", "Anthropic", "0.7", null),
            P("Python Manager", "Python Software Foundation", "3.12", null),
            P("WhatsApp Desktop", "WhatsApp", "2.24", null),
            P("Apple iTunes", "Apple Inc.", "12.13", null),
            P("Adobe Creative Cloud", "Adobe Inc.", "5.10", null),
            P("ABBYY FineReader", "ABBYY", "15.0", null),
            P("FileOpen Plug-In for PDF", "FileOpen Systems", "1.0", null),
            P("Caphyon Advanced Installer", "Caphyon", "21.0", null),
            P("TechSmith Snagit", "TechSmith", "2024", null),
            P("Tracker Software PDF-XChange", "Tracker Software", "10.0", null),
            P("KeenTools", "KeenTools", "2024", null),
            P("Owl Labs Meeting Owl", "Owl Labs", "1.0", null),
        };
    }

    private static InstalledProgram P(string displayName, string publisher, string version, string? installLocation)
    {
        return new InstalledProgram(
            RegistryKey: $"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{displayName}",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: displayName,
            Publisher: publisher,
            Version: version,
            InstallDate: null,
            InstallLocation: installLocation,
            UninstallString: null,
            RegistryEstimatedSizeBytes: 0,
            ComputedSizeBytes: 0);
    }

    /// <summary>
    /// Frozen MachineSnapshot reflecting the user's corporate machine. The
    /// matchers consult this for Service / Driver / Appx evidence on a per-folder
    /// basis. Includes the dominant corporate/security agents and OEM drivers.
    /// </summary>
    private static MachineSnapshot BuildCorporateSnapshot()
    {
        var services = new List<ServiceEntry>
        {
            new("NinjaRMMAgent", "NinjaOne RMM Agent", @"C:\Program Files (x86)\NinjaRMMAgent\ninjarmm-cli.exe"),
            new("Bitdefender", "Bitdefender Endpoint Security", @"C:\Program Files\Bitdefender\Endpoint Security\EPSecurityService.exe"),
            new("Zscaler", "Zscaler Client Connector Service", @"C:\Program Files\Zscaler\ZSAService.exe"),
            new("Matrix42Agent", "Matrix42 Empirum Agent", @"C:\Program Files\Matrix42\Empirum\Agent.exe"),
            new("Splashtop Remote Service", "Splashtop Remote Service", @"C:\Program Files (x86)\Splashtop\Splashtop Remote\Server\SRService.exe"),
            new("EdgeUpdate", "Microsoft Edge Update Service", @"C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe"),
            new("ClickToRunSvc", "Microsoft Office Click-to-Run Service", @"C:\Program Files\Common Files\Microsoft Shared\ClickToRun\OfficeClickToRun.exe"),
            new("WinDefend", "Microsoft Defender Antivirus Service", @"C:\ProgramData\Microsoft\Windows Defender\Platform\MsMpEng.exe"),
            new("AdskLicensingService", "Autodesk Desktop Licensing Service", @"C:\Program Files (x86)\Common Files\Autodesk Shared\AdskLicensing\Current\AdskLicensingService.exe"),
            new("printixservice", "Printix Service", @"C:\Program Files\printix.net\Printix Client\PrintixService.exe"),
            new("vpnagent", "Cisco AnyConnect Secure Mobility Agent", @"C:\Program Files (x86)\Cisco\Cisco AnyConnect Secure Mobility Client\vpnagent.exe"),
            new("SoftwareDistribution", "Windows Update", @"C:\Windows\System32\svchost.exe"),
            new("USOSvc", "Update Orchestrator Service", @"C:\Windows\System32\svchost.exe"),
            new("FlexNet Licensing Service 64", "FlexNet Licensing Service 64", @"C:\Program Files (x86)\Common Files\Macrovision Shared\FlexNet Publisher\FNPLicensingService64.exe"),
            new("RevitInterProcess", "Revit Inter-Process Communication", @"C:\Program Files\Autodesk\Revit 2025\RevitInterProcess.exe"),
            new("HPTouchpointAnalyticsService", "HP Touchpoint Analytics Service", @"C:\Program Files (x86)\HP\HP Touchpoint Analytics Client\Touchpoint.Analytics.Client.Service.exe"),
            new("CADManagerCtrlUtilityHelper", "Autodesk CAD Manager Helper", @"C:\Program Files (x86)\Autodesk\CADManagerCtrlUtility\Helper.exe"),
            new("NVDisplay.ContainerLocalSystem", "NVIDIA Display Container LS", @"C:\Program Files\NVIDIA Corporation\Display.NvContainer\NVDisplay.Container.exe"),
            new("Apple Mobile Device Service", "Apple Mobile Device Service", @"C:\Program Files\Common Files\Apple\Mobile Device Support\AppleMobileDeviceService.exe"),
        };

        var drivers = new List<DriverEntry>
        {
            new("oem1.inf", "iigd_dch.inf",  "Intel Corporation", "Display"),
            new("oem2.inf", "nv_disp.inf",   "NVIDIA Corporation", "Display"),
            new("oem3.inf", "rt640x64.inf",  "Realtek", "Net"),
            new("oem4.inf", "ibtusb.inf",    "Intel Corporation", "Bluetooth"),
            new("oem5.inf", "iaStorAC.inf",  "Intel Corporation", "SCSIAdapter"),
            new("oem6.inf", "xmm7560.inf",   "Intel Corporation", "Modem"),
            new("oem7.inf", "hpqkbfiltr.inf","HP Inc.", "Keyboard"),
            new("oem8.inf", "epsec.sys.inf", "Bitdefender", "System"),
            new("oem9.inf", "zsalwf.inf",    "Zscaler Inc.", "NetService"),
            new("oem10.inf","NinjaWPM.sys.inf","NinjaOne", "System"),
            new("oem11.inf","wdcsam64.inf",  "Microsoft", "System"),
        };

        var appx = new List<AppxEntry>
        {
            new("AD2F1837.myHP_10.10.0.0_x64__v10z8vjag6ke6", "AD2F1837.myHP_v10z8vjag6ke6", "HP Inc.", @"C:\Program Files\WindowsApps\AD2F1837.myHP_10.10.0.0_x64__v10z8vjag6ke6"),
            new("AD2F1837.HPDisplayCenter_2.0.0.0_x64__v10z8vjag6ke6", "AD2F1837.HPDisplayCenter_v10z8vjag6ke6", "HP Inc.", @"C:\Program Files\WindowsApps\AD2F1837.HPDisplayCenter_2.0.0.0_x64__v10z8vjag6ke6"),
            new("MSTeams_24062.0.0.0_x64__8wekyb3d8bbwe", "MSTeams_8wekyb3d8bbwe", "Microsoft Corporation", @"C:\Program Files\WindowsApps\MSTeams_24062.0.0.0_x64__8wekyb3d8bbwe"),
            new("5319275A.WhatsAppDesktop_2.24.5.0_x64__cv1g1gvanyjgm", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "WhatsApp LLC", @"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.24.5.0_x64__cv1g1gvanyjgm"),
            new("Claude_0.7.0.0_x64__pzs8sxrjxfjjc", "Claude_pzs8sxrjxfjjc", "Anthropic", @"C:\Program Files\WindowsApps\Claude_0.7.0.0_x64__pzs8sxrjxfjjc"),
            new("PythonSoftwareFoundation.PythonManager_3.12.0.0_x64__qbz5n2kfra8p0", "PythonSoftwareFoundation.PythonManager_qbz5n2kfra8p0", "Python Software Foundation", @"C:\Program Files\WindowsApps\PythonSoftwareFoundation.PythonManager_3.12.0.0_x64__qbz5n2kfra8p0"),
            new("AppleInc.iCloud_14.0.0.0_x64__nzyj5cx40ttqa", "AppleInc.iCloud_nzyj5cx40ttqa", "Apple Inc.", @"C:\Program Files\WindowsApps\AppleInc.iCloud_14.0.0.0_x64__nzyj5cx40ttqa"),
            new("AppUp.IntelGraphicsExperience_1.100.0.0_x64__8j3eq9eme6ctt", "AppUp.IntelGraphicsExperience_8j3eq9eme6ctt", "Intel Corporation", @"C:\Program Files\WindowsApps\AppUp.IntelGraphicsExperience_1.100.0.0_x64__8j3eq9eme6ctt"),
            new("AppUp.IntelManagementandSecurityStatus_2335.0.0.0_x64__8j3eq9eme6ctt", "AppUp.IntelManagementandSecurityStatus_8j3eq9eme6ctt", "Intel Corporation", @"C:\Program Files\WindowsApps\AppUp.IntelManagementandSecurityStatus_2335.0.0.0_x64__8j3eq9eme6ctt"),
            new("NVIDIACorp.NVIDIAControlPanel_8.1.0.0_x64__56jybvy8sckqj", "NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj", "NVIDIA Corporation", @"C:\Program Files\WindowsApps\NVIDIACorp.NVIDIAControlPanel_8.1.0.0_x64__56jybvy8sckqj"),
            new("com.owllabs.meetingowl.windows_1.0.0.0_x64__cb17j8khw3v66", "com.owllabs.meetingowl.windows_cb17j8khw3v66", "Owl Labs", @"C:\Program Files\WindowsApps\com.owllabs.meetingowl.windows_1.0.0.0_x64__cb17j8khw3v66"),
            new("OwlLabs.MeetingOwl_1.0.0.0_x64__pkza4zvy68k6r", "OwlLabs.MeetingOwl_pkza4zvy68k6r", "Owl Labs", @"C:\Program Files\WindowsApps\OwlLabs.MeetingOwl_1.0.0.0_x64__pkza4zvy68k6r"),
        };

        var tasks = new List<ScheduledTaskEntry>
        {
            new(@"\Microsoft\Office\Office Automatic Updates 2.0", "Microsoft Corporation", @"C:\Program Files\Common Files\Microsoft Shared\ClickToRun\OfficeC2RClient.exe"),
            new(@"\Microsoft\Edge\MicrosoftEdgeUpdateTaskMachineCore", "Microsoft Corporation", @"C:\Program Files (x86)\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe"),
            new(@"\Bitdefender Agent\BDInfoBackup", "Bitdefender", @"C:\Program Files\Bitdefender\Endpoint Security\BDInfoBackup.exe"),
            new(@"\NinjaOne\NinjaUpdater", "NinjaOne", @"C:\Program Files (x86)\NinjaRMMAgent\NinjaUpdater.exe"),
            new(@"\Autodesk\Genuine Service\AdskGenuineService", "Autodesk", @"C:\Program Files (x86)\Common Files\Autodesk Shared\Genuine Service\AdskGenuineService.exe"),
        };

        return new MachineSnapshot(DateTime.UtcNow, services, drivers, appx, tasks);
    }

    private static RiskLevel ParseRisk(string verdict) => verdict switch
    {
        "Aucun" => RiskLevel.Aucun,
        "Faible" => RiskLevel.Faible,
        "Moyen" => RiskLevel.Moyen,
        "Eleve" => RiskLevel.Eleve,
        "Critique" => RiskLevel.Critique,
        _ => throw new ArgumentException($"Unknown verdict: {verdict}"),
    };

    private static bool RisksAreWithinOneBand(RiskLevel a, RiskLevel b)
    {
        return Math.Abs((int)a - (int)b) <= 1;
    }
}
