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
    // Test 3: multiple matchers stack additively up to the per-aggregate cap.
    //   Registry (-50) + Service (-45) = -95 raw, but the Phase-10-05 cap
    //   limits aggregate matcher penalty to 50 -> 100 - 50 = 50.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_MultipleMatcherHits_StackAdditively_CappedAt50()
    {
        var input = Build(
            hits: new[]
            {
                new MatcherHit("Registry", "Registry:Foo", -50),
                new MatcherHit("Service", "Service:Bar", -45),
            });
        _scorer.Compute(input).Should().Be(50);
    }

    // -------------------------------------------------------------------------
    // Test 4: PathCategory adjustments (no matchers, no bonuses).
    //   Phase-10-05 corpus tuning softened these deltas to avoid forcing
    //   Critique on items the human audit graded Moyen/Eleve.
    //   PackageCache    -> 100 - 60 = 40
    //   DriverData      -> 100 - 50 = 50
    //   CorporateAgent  -> 100 - 60 = 40
    //   VendorShared    -> 100 - 40 = 60
    //   Generic         -> 100  (no adjustment)
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(PathCategory.PackageCache, 40)]
    [InlineData(PathCategory.DriverData, 50)]
    [InlineData(PathCategory.CorporateAgent, 40)]
    [InlineData(PathCategory.VendorShared, 60)]
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
    // Test 7: matcher cap — many hits saturate at -50 aggregate, then optional
    //   PathCategory penalty + bonuses can drive the score lower / clamp to 0.
    //   3 hits (3 * -50 = -150) raw, capped at -50 -> 100 - 50 = 50.
    //   Plus PackageCache (-60) -> 50 - 60 = -10 -> clamped to 0.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_ManyMatchers_CappedAt50_ThenCategory_ClampsAt0()
    {
        var input = Build(
            pathRuleCategory: PathCategory.PackageCache,
            hits: new[]
            {
                new MatcherHit("Registry", "a", -50),
                new MatcherHit("Registry", "b", -50),
                new MatcherHit("Service", "c", -45),
            });
        _scorer.Compute(input).Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Test 7-bis: matcher cap alone (no category) — many hits saturate at -50.
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_ManyMatchers_CapsAt50_NoCategory()
    {
        var input = Build(
            hits: new[]
            {
                new MatcherHit("Registry", "a", -50),
                new MatcherHit("Registry", "b", -50),
                new MatcherHit("Service", "c", -45),
            });
        // 3 hits sum to -145 raw, capped at -50 -> 100 - 50 = 50.
        _scorer.Compute(input).Should().Be(50);
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
    //   100 - 60 + 20 = 60. Verifies category and bonus interact correctly
    //   (no clamp here, no matchers). MinRiskFloor=Eleve enforced separately
    //   in the classifier, but the score itself is still 60.
    //   (Phase-10-05 tuning: PackageCache delta softened from -90 to -60.)
    // -------------------------------------------------------------------------
    [Fact]
    public void Compute_PackageCacheWithSizeZeroBonus()
    {
        var input = Build(
            size: 0,
            pathRuleCategory: PathCategory.PackageCache);
        _scorer.Compute(input).Should().Be(60);
    }

    // -------------------------------------------------------------------------
    // Test 10: Registry + PackageCache combined.
    //   100 - 50 (registry) - 60 (PackageCache, softened) = -10 -> clamped at 0.
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
