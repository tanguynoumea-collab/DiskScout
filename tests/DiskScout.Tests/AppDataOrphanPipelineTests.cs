using System.IO;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// AppDataOrphanPipeline integration coverage. Uses hand-written fakes
/// throughout (project policy precedent from Plan 09-05 / 10-02 — no Moq for
/// new code). Exercises:
///   - HardBlacklist suppression returns null
///   - MinRiskFloor enforcement (PackageCache -> Eleve)
///   - 7-step happy path with real residue (Aucun, Supprimer)
///   - Registry matcher hit subtracts -50
///   - Combined Registry + Service hits clamp at 0 (Critique)
///   - ParentContextAnalyzer routing (Logs subdir -> uses parent vendor)
///   - PublisherAlias bridging (RVT 2025 -> Revit canonical)
///   - ResiduePathSafety defense-in-depth boundary (Common Files -> Critique)
/// </summary>
public class AppDataOrphanPipelineTests
{
    private readonly ILogger _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();

    // ---------------- Hand-written fakes ----------------

    private sealed class FakePathRuleEngine : IPathRuleEngine
    {
        private readonly List<PathRule> _rules = new();
        public IReadOnlyList<PathRule> AllRules => _rules;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void AddRule(PathRule rule) => _rules.Add(rule);

        public IReadOnlyList<RuleHit> Match(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return Array.Empty<RuleHit>();
            // Simple prefix match (no env var expansion for tests).
            var hits = new List<(int Length, RuleHit Hit)>();
            foreach (var rule in _rules)
            {
                if (fullPath.StartsWith(rule.PathPattern, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add((rule.PathPattern.Length, new RuleHit(rule.Id, rule.Category, rule.Reason ?? rule.Id)));
                }
            }
            hits.Sort((a, b) => b.Length.CompareTo(a.Length));
            return hits.Select(h => h.Hit).ToList();
        }
    }

    private sealed class IdentityParentContextAnalyzer : IParentContextAnalyzer
    {
        public string GetSignificantParent(string fullPath) => fullPath ?? string.Empty;
    }

    private sealed class FakeMachineSnapshotProvider : IMachineSnapshotProvider
    {
        private readonly MachineSnapshot _snapshot;
        public FakeMachineSnapshotProvider(MachineSnapshot snapshot) { _snapshot = snapshot; }
        public Task<MachineSnapshot> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);
        public void Invalidate() { /* no-op */ }
    }

    private sealed class FakeAliasResolver : IPublisherAliasResolver
    {
        public Func<string, string?, string?, (double Score, string? MatchedCanonical)?>? Behavior { get; set; }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<(double Score, string? MatchedCanonical)?> ResolveAsync(
            string folderName, string? publisher, string? displayName, CancellationToken cancellationToken = default)
        {
            var b = Behavior;
            return Task.FromResult(b?.Invoke(folderName, publisher, displayName));
        }
    }

    /// <summary>Empty snapshot: no services / drivers / appx / scheduled tasks.</summary>
    private static MachineSnapshot EmptySnapshot() =>
        new(DateTime.UtcNow,
            Services: Array.Empty<ServiceEntry>(),
            Drivers: Array.Empty<DriverEntry>(),
            AppxPackages: Array.Empty<AppxEntry>(),
            ScheduledTasks: Array.Empty<ScheduledTaskEntry>());

    private static FileSystemNode Node(string path, long size = 0, DateTime? lastWriteUtc = null) =>
        new(
            Id: 1,
            ParentId: null,
            Name: Path.GetFileName(path),
            FullPath: path,
            Kind: FileSystemNodeKind.Directory,
            SizeBytes: size,
            FileCount: 0,
            DirectoryCount: 0,
            LastModifiedUtc: lastWriteUtc ?? DateTime.UtcNow.AddDays(-400),
            IsReparsePoint: false,
            Depth: 3);

    private AppDataOrphanPipeline BuildPipeline(
        IPathRuleEngine engine,
        IParentContextAnalyzer parent,
        IMachineSnapshotProvider snapshotProvider,
        IPublisherAliasResolver aliasResolver)
    {
        return new AppDataOrphanPipeline(
            _logger,
            engine,
            parent,
            snapshotProvider,
            aliasResolver,
            new ConfidenceScorer(),
            new RiskLevelClassifier(),
            new ServiceMatcher(_logger),
            new DriverMatcher(_logger),
            new AppxMatcher(_logger),
            new RegistryMatcher(_logger));
    }

    // -------------------------------------------------------------------------
    // Test 1: HardBlacklist returns null.
    //   PathRule with category OsCriticalDoNotPropose matches the candidate ->
    //   pipeline returns null (user must never see OS-critical paths).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_HardBlacklist_ReturnsNull()
    {
        var engine = new FakePathRuleEngine();
        engine.AddRule(new PathRule(
            Id: "os-windows-system32",
            PathPattern: @"C:\Windows\System32",
            Category: PathCategory.OsCriticalDoNotPropose,
            MinRiskFloor: null,
            Reason: "OS-critical"));

        var pipeline = BuildPipeline(
            engine,
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            new FakeAliasResolver());

        var node = Node(@"C:\Windows\System32\drivers\etc");

        var result = await pipeline.EvaluateAsync(node, Array.Empty<InstalledProgram>());

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Test 2: MinRiskFloor=Eleve enforced.
    //   PackageCache PathRule with MinRiskFloor=Eleve. Score is high (would be
    //   Aucun) but the floor clamps risk UP to Eleve, action -> NePasToucher.
    //   Note: with PackageCache adjustment (-90), score is naturally already
    //   low. We pick a path that ONLY hits the rule (no matchers) so the score
    //   reflects only the residue bonuses minus the category cost.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_PackageCacheWithFloor_RiskClampedToEleve()
    {
        var engine = new FakePathRuleEngine();
        engine.AddRule(new PathRule(
            Id: "package-cache-test",
            PathPattern: @"C:\ProgramData\PackageCache",
            Category: PathCategory.PackageCache,
            MinRiskFloor: RiskLevel.Eleve,
            Reason: "MSI Package Cache"));

        var pipeline = BuildPipeline(
            engine,
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            new FakeAliasResolver());

        var node = Node(@"C:\ProgramData\PackageCache\ABC123",
            size: 0,
            lastWriteUtc: DateTime.UtcNow.AddDays(-400));

        var result = await pipeline.EvaluateAsync(node, Array.Empty<InstalledProgram>());

        result.Should().NotBeNull();
        // Score: 100 - 90 (PackageCache) + 20 (size=0) + 15 (>365d) + 10 (no exe — folder doesn't exist on disk) = 55
        // Without floor that maps to Moyen, but floor=Eleve clamps UP.
        result!.Risk.Should().Be(RiskLevel.Eleve);
        result.Action.Should().Be(RecommendedAction.NePasToucher);
        result.Category.Should().Be(PathCategory.PackageCache);
    }

    // -------------------------------------------------------------------------
    // Test 3: real residue happy path. No PathRule, no matcher hits, size=0,
    //   age > 365d, no exe -> score = 100 + bonuses (clamped 100) -> Aucun.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_RealResidue_AucunSupprimer()
    {
        var pipeline = BuildPipeline(
            new FakePathRuleEngine(),
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            new FakeAliasResolver());

        var node = Node(@"C:\ProgramData\OldVendor\Leftover",
            size: 0,
            lastWriteUtc: DateTime.UtcNow.AddDays(-500));

        var result = await pipeline.EvaluateAsync(node, Array.Empty<InstalledProgram>());

        result.Should().NotBeNull();
        result!.ConfidenceScore.Should().Be(100);
        result.Risk.Should().Be(RiskLevel.Aucun);
        result.Action.Should().Be(RecommendedAction.Supprimer);
        result.MatchedSources.Should().BeEmpty();
        result.TriggeredRules.Should().BeEmpty();
        result.Category.Should().Be(PathCategory.Generic);
    }

    // -------------------------------------------------------------------------
    // Test 4: Registry matcher fires. An installed program whose Publisher
    //   fuzzy-matches the folder name -> -50 -> 100 - 50 + bonuses (capped) = 50.
    //   With size > 0, recent, with exe-assumed-true (folder doesn't exist -> false +10),
    //   really: 100 -50 +10 = 60 -> Faible (CorbeilleOk).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_RegistryMatcherHit_ReducesScore()
    {
        var pipeline = BuildPipeline(
            new FakePathRuleEngine(),
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            new FakeAliasResolver());

        var program = new InstalledProgram(
            RegistryKey: @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AcmeApp",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: "Acme App",
            Publisher: "Acme Corporation",
            Version: "1.0",
            InstallDate: null,
            InstallLocation: null,
            UninstallString: null,
            RegistryEstimatedSizeBytes: 0,
            ComputedSizeBytes: 0);

        var node = Node(@"C:\ProgramData\Acme Corporation",
            size: 1024 * 1024,
            lastWriteUtc: DateTime.UtcNow.AddDays(-7));

        var result = await pipeline.EvaluateAsync(node, new[] { program });

        result.Should().NotBeNull();
        result!.MatchedSources.Should().NotBeEmpty();
        result.MatchedSources.Should().Contain(h => h.Source == "Registry");
        // 100 - 50 + 10 (no exe — folder doesn't exist) = 60 -> Faible.
        result.ConfidenceScore.Should().Be(60);
        result.Risk.Should().Be(RiskLevel.Faible);
    }

    // -------------------------------------------------------------------------
    // Test 5: Registry + Service matchers both fire on same Acme folder.
    //   Phase-10-05: matcher penalty capped at -50 aggregate.
    //   Raw: -50 (Registry) - 45 (Service) = -95, capped at -50.
    //   Score: 100 - 50 + 10 (no exe) = 60 -> Faible (Score 60-79 -> Faible).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_RegistryPlusServiceMatcher_Faible()
    {
        var snapshot = new MachineSnapshot(
            CapturedUtc: DateTime.UtcNow,
            Services: new[] { new ServiceEntry("AcmeService", "Acme Background Service", null) },
            Drivers: Array.Empty<DriverEntry>(),
            AppxPackages: Array.Empty<AppxEntry>(),
            ScheduledTasks: Array.Empty<ScheduledTaskEntry>());

        var pipeline = BuildPipeline(
            new FakePathRuleEngine(),
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(snapshot),
            new FakeAliasResolver());

        var program = new InstalledProgram(
            RegistryKey: @"HKLM\...\AcmeApp",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: "Acme",
            Publisher: "Acme Corporation",
            Version: "1.0",
            InstallDate: null,
            InstallLocation: null,
            UninstallString: null,
            RegistryEstimatedSizeBytes: 0,
            ComputedSizeBytes: 0);

        var node = Node(@"C:\ProgramData\Acme",
            size: 1024 * 1024,
            lastWriteUtc: DateTime.UtcNow.AddDays(-7));

        var result = await pipeline.EvaluateAsync(node, new[] { program });

        result.Should().NotBeNull();
        result!.MatchedSources.Should().Contain(h => h.Source == "Registry");
        result.MatchedSources.Should().Contain(h => h.Source == "Service");
        result.Risk.Should().Be(RiskLevel.Faible);
        result.Action.Should().Be(RecommendedAction.CorbeilleOk);
    }

    // -------------------------------------------------------------------------
    // Test 6: ParentContextAnalyzer routing.
    //   We use a custom analyzer that walks "Logs" up to its parent "Vendor".
    //   The folder name passed to the matchers is "Vendor", not "Logs".
    //   We confirm the candidate emitted carries the significant parent path.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_GenericLeaf_UsesParentVendorFolder()
    {
        var realParentAnalyzer = new ParentContextAnalyzer();

        var pipeline = BuildPipeline(
            new FakePathRuleEngine(),
            realParentAnalyzer,
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            new FakeAliasResolver());

        var node = Node(@"C:\ProgramData\Vendor\Logs",
            size: 0,
            lastWriteUtc: DateTime.UtcNow.AddDays(-500));

        var result = await pipeline.EvaluateAsync(node, Array.Empty<InstalledProgram>());

        result.Should().NotBeNull();
        // The significant parent is "...\Vendor" because Logs is in the
        // generic-leaves set.
        result!.ParentSignificantPath.Should().EndWith(@"\Vendor");
        result.FullPath.Should().Be(@"C:\ProgramData\Vendor\Logs");
    }

    // -------------------------------------------------------------------------
    // Test 7: PublisherAlias bridging.
    //   Folder is "RVT 2025"; alias resolver maps it to canonical "Revit".
    //   The Registry matcher then matches "Revit" against an installed program
    //   whose Publisher is "Autodesk Revit" via the canonical-publisher branch.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_PublisherAlias_BridgesFolderToRegistry()
    {
        var resolver = new FakeAliasResolver
        {
            Behavior = (folder, _, _) =>
            {
                if (string.Equals(folder, "RVT 2025", StringComparison.OrdinalIgnoreCase))
                    return (0.9, "Revit");
                return null;
            },
        };

        var pipeline = BuildPipeline(
            new FakePathRuleEngine(),
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            resolver);

        var program = new InstalledProgram(
            RegistryKey: @"HKLM\...\Autodesk Revit 2025",
            Hive: RegistryHive.LocalMachine64,
            DisplayName: "Autodesk Revit 2025",
            Publisher: "Autodesk Revit",
            Version: "2025",
            InstallDate: null,
            InstallLocation: null,
            UninstallString: null,
            RegistryEstimatedSizeBytes: 0,
            ComputedSizeBytes: 0);

        var node = Node(@"C:\ProgramData\RVT 2025",
            size: 1024 * 1024,
            lastWriteUtc: DateTime.UtcNow.AddDays(-7));

        var result = await pipeline.EvaluateAsync(node, new[] { program });

        result.Should().NotBeNull();
        result!.MatchedSources.Should().Contain(h => h.Source == "Registry"
            && h.Evidence.Contains("Autodesk Revit 2025"));
    }

    // -------------------------------------------------------------------------
    // Test 8: ResiduePathSafety override forces Critique even without rules.
    //   Path under \Program Files\Common Files trips the Phase-9 safety net.
    //   No PathRule, no matchers — score would be 100 / Aucun, but the
    //   defense-in-depth boundary clamps to Critique / NePasToucher.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_ResiduePathSafetyOverride_ForcesCritique()
    {
        var pipeline = BuildPipeline(
            new FakePathRuleEngine(),
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            new FakeAliasResolver());

        var node = Node(@"C:\Program Files\Common Files\foo",
            size: 0,
            lastWriteUtc: DateTime.UtcNow.AddDays(-500));

        var result = await pipeline.EvaluateAsync(node, Array.Empty<InstalledProgram>());

        result.Should().NotBeNull();
        result!.Risk.Should().Be(RiskLevel.Critique);
        result.Action.Should().Be(RecommendedAction.NePasToucher);
    }

    // -------------------------------------------------------------------------
    // Test 9: Reason string contains rule + matcher counts and final score.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_ReasonStringFormatted()
    {
        var pipeline = BuildPipeline(
            new FakePathRuleEngine(),
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            new FakeAliasResolver());

        var node = Node(@"C:\ProgramData\Foo",
            size: 0,
            lastWriteUtc: DateTime.UtcNow.AddDays(-500));

        var result = await pipeline.EvaluateAsync(node, Array.Empty<InstalledProgram>());

        result.Should().NotBeNull();
        result!.Reason.Should().Contain("règle");
        result.Reason.Should().Contain("signal");
        result.Reason.Should().Contain("/100");
    }

    // -------------------------------------------------------------------------
    // Test 10: Cancellation: a cancelled token is honored.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task EvaluateAsync_Cancelled_Throws()
    {
        var pipeline = BuildPipeline(
            new FakePathRuleEngine(),
            new IdentityParentContextAnalyzer(),
            new FakeMachineSnapshotProvider(EmptySnapshot()),
            new FakeAliasResolver());

        var node = Node(@"C:\ProgramData\Foo");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await pipeline.EvaluateAsync(node, Array.Empty<InstalledProgram>(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
