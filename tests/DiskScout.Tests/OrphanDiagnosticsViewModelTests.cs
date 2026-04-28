using System.Globalization;
using System.Windows.Media;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.ViewModels;
using FluentAssertions;

namespace DiskScout.Tests;

/// <summary>
/// Coverage for the Plan 10-06 UI VM (OrphanDiagnosticsViewModel) and
/// the RiskLevelToBrushConverter that paints the Score badge.
/// </summary>
public class OrphanDiagnosticsViewModelTests
{
    private static AppDataOrphanCandidate BuildCandidate(
        int score = 75,
        RiskLevel risk = RiskLevel.Faible,
        RecommendedAction action = RecommendedAction.CorbeilleOk,
        long sizeBytes = 4096,
        PathCategory category = PathCategory.Generic,
        IReadOnlyList<RuleHit>? rules = null,
        IReadOnlyList<MatcherHit>? matches = null,
        string fullPath = @"C:\ProgramData\Vendor\App",
        string? significant = null,
        DateTime? lastWriteUtc = null,
        string reason = "Score 75 / 100")
        => new(
            NodeId: 42,
            FullPath: fullPath,
            SizeBytes: sizeBytes,
            LastWriteUtc: lastWriteUtc ?? new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            ParentSignificantPath: significant ?? fullPath,
            Category: category,
            MatchedSources: matches ?? Array.Empty<MatcherHit>(),
            TriggeredRules: rules ?? Array.Empty<RuleHit>(),
            ConfidenceScore: score,
            Risk: risk,
            Action: action,
            Reason: reason);

    [Fact]
    public void Ctor_Populates_Path_AndScore_AndRisk_AndAction()
    {
        var diag = BuildCandidate(
            score: 82,
            risk: RiskLevel.Aucun,
            action: RecommendedAction.Supprimer,
            fullPath: @"C:\ProgramData\OldVendor\Cache",
            significant: @"C:\ProgramData\OldVendor",
            category: PathCategory.Generic);

        var vm = new OrphanDiagnosticsViewModel(diag);

        vm.FullPath.Should().Be(@"C:\ProgramData\OldVendor\Cache");
        vm.ParentSignificantPath.Should().Be(@"C:\ProgramData\OldVendor");
        vm.ConfidenceScore.Should().Be(82);
        vm.Risk.Should().Be(RiskLevel.Aucun);
        vm.RiskLabel.Should().Be("Aucun");
        vm.Action.Should().Be(RecommendedAction.Supprimer);
        vm.ActionLabel.Should().Be("Supprimer");
        vm.CategoryLabel.Should().Be("Generic");
    }

    [Fact]
    public void TriggeredRulesLines_FormattedAs_RuleId_And_Reason()
    {
        var diag = BuildCandidate(rules: new[]
        {
            new RuleHit("os-critical-system32", PathCategory.OsCriticalDoNotPropose, "OS component"),
            new RuleHit("pkg-cache-windows-installer", PathCategory.PackageCache, "MSI shared cache"),
        });

        var vm = new OrphanDiagnosticsViewModel(diag);

        vm.TriggeredRulesLines.Should().HaveCount(2);
        vm.TriggeredRulesLines[0].Should().Be("os-critical-system32 — OS component");
        vm.TriggeredRulesLines[1].Should().Be("pkg-cache-windows-installer — MSI shared cache");
    }

    [Fact]
    public void MatchedSourcesLines_FormattedWithSignedDelta()
    {
        var diag = BuildCandidate(matches: new[]
        {
            new MatcherHit("Registry", "HKLM\\SOFTWARE\\Vendor", -50),
            new MatcherHit("Service", "VendorSvc", -45),
            new MatcherHit("Bonus", "EmptyFolder", +20),
        });

        var vm = new OrphanDiagnosticsViewModel(diag);

        vm.MatchedSourcesLines.Should().HaveCount(3);
        vm.MatchedSourcesLines[0].Should().Be("Registry: HKLM\\SOFTWARE\\Vendor (-50 pts)");
        vm.MatchedSourcesLines[1].Should().Be("Service: VendorSvc (-45 pts)");
        vm.MatchedSourcesLines[2].Should().Be("Bonus: EmptyFolder (+20 pts)");
    }

    [Fact]
    public void SizeDisplay_ZeroBytes_Returns_0o()
    {
        var diag = BuildCandidate(sizeBytes: 0);
        var vm = new OrphanDiagnosticsViewModel(diag);
        vm.SizeDisplay.Should().Be("0 o");
    }

    [Fact]
    public void SizeDisplay_NonZero_FormattedHumanReadable()
    {
        var diag = BuildCandidate(sizeBytes: 5L * 1024 * 1024); // 5 MB
        var vm = new OrphanDiagnosticsViewModel(diag);
        vm.SizeDisplay.Should().Contain("Mo");
    }

    [Fact]
    public void LastWriteDisplay_FormattedYYYYMMDD_HHmm()
    {
        var lwUtc = new DateTime(2026, 3, 15, 14, 30, 0, DateTimeKind.Utc);
        var diag = BuildCandidate(lastWriteUtc: lwUtc);
        var vm = new OrphanDiagnosticsViewModel(diag);

        // We can't assert the exact local-time hour (timezone-dependent), but format must match.
        vm.LastWriteDisplay.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$");
        vm.LastWriteDisplay.Should().StartWith("2026-03-15");
    }

    [Fact]
    public void EmptyRulesAndMatchers_YieldEmptyLines()
    {
        var diag = BuildCandidate();
        var vm = new OrphanDiagnosticsViewModel(diag);
        vm.TriggeredRulesLines.Should().BeEmpty();
        vm.MatchedSourcesLines.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // RiskLevelToBrushConverter
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(RiskLevel.Aucun, "#FF27AE60")]
    [InlineData(RiskLevel.Faible, "#FF2ECC71")]
    [InlineData(RiskLevel.Moyen, "#FFF39C12")]
    [InlineData(RiskLevel.Eleve, "#FFE67E22")]
    [InlineData(RiskLevel.Critique, "#FFE74C3C")]
    public void RiskLevelToBrush_ReturnsExpectedColor(RiskLevel risk, string expectedArgb)
    {
        var conv = new RiskLevelToBrushConverter();
        var result = conv.Convert(risk, typeof(Brush), null!, CultureInfo.InvariantCulture);

        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.ToString().Should().BeEquivalentTo(expectedArgb);
    }

    [Fact]
    public void RiskLevelToBrush_NullValue_ReturnsTransparent()
    {
        var conv = new RiskLevelToBrushConverter();
        var result = conv.Convert(null!, typeof(Brush), null!, CultureInfo.InvariantCulture);
        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result!;
        brush.Color.A.Should().Be(0);
    }

    [Fact]
    public void RiskLevelToBrush_ConvertBack_Throws()
    {
        var conv = new RiskLevelToBrushConverter();
        var act = () => conv.ConvertBack(Brushes.Red, typeof(RiskLevel), null!, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>();
    }

    // -------------------------------------------------------------------------
    // OrphanRow extension: Diagnostics flow-through
    // -------------------------------------------------------------------------

    [Fact]
    public void OrphanRow_ForwardsDiagnostics_FromOrphanCandidate()
    {
        var diag = BuildCandidate(score: 50, risk: RiskLevel.Moyen);
        var candidate = new OrphanCandidate(
            NodeId: 7,
            FullPath: @"C:\ProgramData\Vendor\App",
            SizeBytes: 1024,
            Category: OrphanCategory.AppDataOrphan,
            Reason: "appdata orphan",
            MatchScore: null) { Diagnostics = diag };

        var row = new OrphanRow(candidate);

        row.HasDiagnostics.Should().BeTrue();
        row.Diagnostics.Should().BeSameAs(diag);
        row.Score.Should().Be(50);
        row.Risk.Should().Be(RiskLevel.Moyen);
        row.ScoreBadgeText.Should().Be("50");
    }

    [Fact]
    public void OrphanRow_NullDiagnostics_StaleTemp_GetsSynthesizedAucunDefaults()
    {
        // Phase 10 (post-launch tweak): non-AppData rows get a category-default
        // (Score, Risk, Action) so the badge is consistent across every tab.
        // StaleTemp = Aucun (very high confidence — these are old temp files).
        var candidate = new OrphanCandidate(
            NodeId: 7,
            FullPath: @"C:\Temp\foo",
            SizeBytes: 1024,
            Category: OrphanCategory.StaleTemp,
            Reason: "old temp",
            MatchScore: null);

        var row = new OrphanRow(candidate);

        row.HasDiagnostics.Should().BeFalse(); // gate for "Pourquoi ?" button
        row.Diagnostics.Should().BeNull();
        row.Score.Should().Be(90);             // synthesized default for StaleTemp
        row.Risk.Should().Be(RiskLevel.Aucun);
        row.RecommendedAction.Should().Be(RecommendedAction.Supprimer);
        row.ScoreBand.Should().Be(RiskLevel.Aucun);
        row.HasFloorApplied.Should().BeFalse(); // no floor for non-AppData
        row.ScoreBadgeText.Should().Be("90");   // no 🛡 prefix
    }
}
