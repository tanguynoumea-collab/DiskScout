using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;

namespace DiskScout.Tests;

/// <summary>
/// ConfidenceScorer coverage — verifies the exact score deltas from
/// CONTEXT.md `<specifics>` (Plan 10): registry/service/driver/appx
/// matcher penalties, PathCategory adjustments, residue bonuses, clamp
/// to [0, 100], and the OsCriticalDoNotPropose hard-zero override.
/// </summary>
public class ConfidenceScorerTests
{
    private readonly ConfidenceScorer _scorer = new();

    private static AppDataOrphanInput Build(
        long size = 1024 * 1024,
        DateTime? lastWriteUtc = null,
        bool hasExeOrDll = true,
        PathCategory? pathRuleCategory = null,
        params MatcherHit[] hits)
        => new(
            FullPath: @"C:\ProgramData\Vendor",
            SizeBytes: size,
            LastWriteUtc: lastWriteUtc ?? DateTime.UtcNow.AddDays(-7),
            HasExeOrDll: hasExeOrDll,
            PathRuleCategory: pathRuleCategory,
            MatcherHits: hits);

    // -------------------------------------------------------------------------
    // Test 1: empty input (no matchers, no rules, recent, has binaries) -> 100.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_EmptyInput_ReturnsInitial100()
    {
        var input = Build();
        _scorer.Compute(input).Should().Be(100);
    }

    // -------------------------------------------------------------------------
    // Test 2: each individual matcher delta.
    //   Registry hit -> 100 - 50 = 50
    //   Service hit  -> 100 - 45 = 55  (avg until State exposed)
    //   Driver hit   -> 100 - 45 = 55
    //   Appx hit     -> 100 - 50 = 50
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("Registry", -50, 50)]
    [InlineData("Service", -45, 55)]
    [InlineData("Driver", -45, 55)]
    [InlineData("Appx", -50, 50)]
    public void Compute_SingleMatcherHit_AppliesDelta(string source, int delta, int expectedScore)
    {
        var input = Build(hits: new MatcherHit(source, $"{source}:fake", delta));
        _scorer.Compute(input).Should().Be(expectedScore);
    }

    // -------------------------------------------------------------------------
    // Test 3: multiple matchers stack additively.
    //   Registry (-50) + Service (-45) = -95 -> 100 - 95 = 5.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_MultipleMatcherHits_StackAdditively()
    {
        var input = Build(
            hits: new[]
            {
                new MatcherHit("Registry", "Registry:Foo", -50),
                new MatcherHit("Service", "Service:Bar", -45),
            });
        _scorer.Compute(input).Should().Be(5);
    }

    // -------------------------------------------------------------------------
    // Test 4: PathCategory adjustments (no matchers, no bonuses).
    //   PackageCache    -> 100 - 90 = 10
    //   DriverData      -> 100 - 70 = 30
    //   CorporateAgent  -> 100 - 80 = 20
    //   VendorShared    -> 100 - 50 = 50
    //   Generic         -> 100  (no adjustment)
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(PathCategory.PackageCache, 10)]
    [InlineData(PathCategory.DriverData, 30)]
    [InlineData(PathCategory.CorporateAgent, 20)]
    [InlineData(PathCategory.VendorShared, 50)]
    [InlineData(PathCategory.Generic, 100)]
    public void Compute_PathCategoryAdjustment(PathCategory category, int expected)
    {
        var input = Build(pathRuleCategory: category);
        _scorer.Compute(input).Should().Be(expected);
    }

    // -------------------------------------------------------------------------
    // Test 5: OsCriticalDoNotPropose forces score=0 regardless of bonuses.
    //   Defense-in-depth — pipeline's HardBlacklist gate normally suppresses
    //   these before they reach the scorer.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_OsCritical_Returns0RegardlessOfBonuses()
    {
        var input = Build(
            size: 0,                                // +20
            lastWriteUtc: DateTime.UtcNow.AddDays(-400), // +15
            hasExeOrDll: false,                     // +10
            pathRuleCategory: PathCategory.OsCriticalDoNotPropose);
        _scorer.Compute(input).Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Test 6: residue bonuses individually.
    //   Size==0   -> 100 + 20 = clamped to 100
    //   >365d age -> 100 + 15 = clamped to 100
    //   >180d age (and not >365d) -> 100 + 10 = clamped to 100
    //   no exe/dll -> 100 + 10 = clamped to 100
    // Use a -50 baseline (one Registry hit) to actually surface the bonus value.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_SizeZeroBonus()
    {
        var input = Build(
            size: 0,
            hits: new MatcherHit("Registry", "x", -50));
        // 100 - 50 + 20 = 70.
        _scorer.Compute(input).Should().Be(70);
    }

    [Fact]
    public void Compute_OldFolderBonus_Over365Days()
    {
        var input = Build(
            lastWriteUtc: DateTime.UtcNow.AddDays(-400),
            hits: new MatcherHit("Registry", "x", -50));
        // 100 - 50 + 15 = 65.
        _scorer.Compute(input).Should().Be(65);
    }

    [Fact]
    public void Compute_OldFolderBonus_Over180Days_Below365Days()
    {
        var input = Build(
            lastWriteUtc: DateTime.UtcNow.AddDays(-200),
            hits: new MatcherHit("Registry", "x", -50));
        // 100 - 50 + 10 = 60. The +15 (>365d) branch is mutually exclusive.
        _scorer.Compute(input).Should().Be(60);
    }

    [Fact]
    public void Compute_NoBinariesBonus()
    {
        var input = Build(
            hasExeOrDll: false,
            hits: new MatcherHit("Registry", "x", -50));
        // 100 - 50 + 10 = 60.
        _scorer.Compute(input).Should().Be(60);
    }

    // -------------------------------------------------------------------------
    // Test 7: clamp at 0 — overshoot due to many matchers can't go negative.
    //   3 Registry hits (3 * -50 = -150) -> 100 - 150 = -50 -> clamped to 0.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_ManyMatchers_ClampedAt0()
    {
        var input = Build(
            hits: new[]
            {
                new MatcherHit("Registry", "a", -50),
                new MatcherHit("Registry", "b", -50),
                new MatcherHit("Service", "c", -45),
            });
        _scorer.Compute(input).Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Test 8: clamp at 100 — bonuses can't push past 100.
    //   No hits + size=0 + >365d + no exe -> 100 + 20 + 15 + 10 = 145 -> 100.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_AllBonuses_ClampedAt100()
    {
        var input = Build(
            size: 0,
            lastWriteUtc: DateTime.UtcNow.AddDays(-400),
            hasExeOrDll: false);
        _scorer.Compute(input).Should().Be(100);
    }

    // -------------------------------------------------------------------------
    // Test 9: PackageCache combined with size=0 bonus.
    //   100 - 90 + 20 = 30. Verifies category and bonus interact correctly
    //   (no clamp here, no matchers). MinRiskFloor=Eleve enforced separately
    //   in the classifier, but the score itself is still 30.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_PackageCacheWithSizeZeroBonus()
    {
        var input = Build(
            size: 0,
            pathRuleCategory: PathCategory.PackageCache);
        _scorer.Compute(input).Should().Be(30);
    }

    // -------------------------------------------------------------------------
    // Test 10: Registry + PackageCache stack to clamp at 0.
    //   100 - 50 (registry) - 90 (PackageCache) = -40 -> clamped at 0.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_RegistryPlusPackageCache_ClampedAt0()
    {
        var input = Build(
            pathRuleCategory: PathCategory.PackageCache,
            hits: new MatcherHit("Registry", "x", -50));
        _scorer.Compute(input).Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Test 11: ArgumentNullException if input is null (defensive contract).
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_NullInput_Throws()
    {
        var act = () => _scorer.Compute(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------
    // Test 12: positive-delta bonus expressed via MatcherHit (defensive — the
    //   pipeline applies bonuses as separate fields, but the scorer subtracts
    //   |ScoreDelta| so a positive ScoreDelta still SUBTRACTS its absolute value
    //   per the contract in IConfidenceScorer XML doc. Verifies the contract.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_PositiveScoreDeltaInHit_StillSubtractsAbsoluteValue()
    {
        // Defensive: scorer treats hits as monotonically negative-pressure.
        var input = Build(hits: new MatcherHit("Test", "weird", 30));
        // 100 - |30| = 70.
        _scorer.Compute(input).Should().Be(70);
    }
}
