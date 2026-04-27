using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;

namespace DiskScout.Tests;

/// <summary>
/// RiskLevelClassifier coverage — score banding per CONTEXT.md, the
/// OsCriticalDoNotPropose hard override, and the MinRiskFloor clamp-up
/// behavior (PackageCache + CorporateAgent floors).
/// </summary>
public class RiskLevelClassifierTests
{
    private readonly RiskLevelClassifier _classifier = new();

    // -------------------------------------------------------------------------
    // Test 1: each band boundary value (no rule, no floor).
    //   100, 80          -> Aucun, Supprimer
    //   79, 60           -> Faible, CorbeilleOk
    //   59, 40           -> Moyen, VerifierAvant
    //   39, 20           -> Eleve, NePasToucher
    //   19, 0            -> Critique, NePasToucher
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(100, RiskLevel.Aucun, RecommendedAction.Supprimer)]
    [InlineData(80, RiskLevel.Aucun, RecommendedAction.Supprimer)]
    [InlineData(79, RiskLevel.Faible, RecommendedAction.CorbeilleOk)]
    [InlineData(70, RiskLevel.Faible, RecommendedAction.CorbeilleOk)]
    [InlineData(60, RiskLevel.Faible, RecommendedAction.CorbeilleOk)]
    [InlineData(59, RiskLevel.Moyen, RecommendedAction.VerifierAvant)]
    [InlineData(50, RiskLevel.Moyen, RecommendedAction.VerifierAvant)]
    [InlineData(40, RiskLevel.Moyen, RecommendedAction.VerifierAvant)]
    [InlineData(39, RiskLevel.Eleve, RecommendedAction.NePasToucher)]
    [InlineData(30, RiskLevel.Eleve, RecommendedAction.NePasToucher)]
    [InlineData(20, RiskLevel.Eleve, RecommendedAction.NePasToucher)]
    [InlineData(19, RiskLevel.Critique, RecommendedAction.NePasToucher)]
    [InlineData(10, RiskLevel.Critique, RecommendedAction.NePasToucher)]
    [InlineData(0, RiskLevel.Critique, RecommendedAction.NePasToucher)]
    public void Classify_ScoreBandsWithoutFloor(int score, RiskLevel expectedRisk, RecommendedAction expectedAction)
    {
        var (risk, action) = _classifier.Classify(score, pathRuleCategory: null, minRiskFloor: null);
        risk.Should().Be(expectedRisk);
        action.Should().Be(expectedAction);
    }

    // -------------------------------------------------------------------------
    // Test 2: OsCriticalDoNotPropose hard override - any score forces Critique.
    //   Defense-in-depth in case the pipeline's HardBlacklist gate slips.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(100)]
    [InlineData(80)]
    [InlineData(50)]
    [InlineData(0)]
    public void Classify_OsCriticalCategory_ForcesCritiqueRegardlessOfScore(int score)
    {
        var (risk, action) = _classifier.Classify(
            score, PathCategory.OsCriticalDoNotPropose, minRiskFloor: null);
        risk.Should().Be(RiskLevel.Critique);
        action.Should().Be(RecommendedAction.NePasToucher);
    }

    // -------------------------------------------------------------------------
    // Test 3: MinRiskFloor=Eleve clamps a high score UP to Eleve.
    //   Score 100 (Aucun normally) + floor=Eleve -> Eleve, NePasToucher.
    //   Models the PackageCache + CorporateAgent safety floor from CONTEXT.md.
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_MinRiskFloorEleve_ClampsHighScoreUpToEleve()
    {
        var (risk, action) = _classifier.Classify(
            confidenceScore: 100,
            pathRuleCategory: PathCategory.PackageCache,
            minRiskFloor: RiskLevel.Eleve);
        risk.Should().Be(RiskLevel.Eleve);
        action.Should().Be(RecommendedAction.NePasToucher);
    }

    // -------------------------------------------------------------------------
    // Test 4: MinRiskFloor=Eleve on a Faible score (70) -> Eleve.
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_MinRiskFloorEleve_ClampsFaibleUp()
    {
        var (risk, action) = _classifier.Classify(70, PathCategory.CorporateAgent, RiskLevel.Eleve);
        risk.Should().Be(RiskLevel.Eleve);
        action.Should().Be(RecommendedAction.NePasToucher);
    }

    // -------------------------------------------------------------------------
    // Test 5: MinRiskFloor below the natural risk -> no clamp (we only clamp UP).
    //   Score 0 (Critique) + floor=Eleve -> stays Critique, not downgraded.
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_MinRiskFloorBelowNatural_DoesNotDowngrade()
    {
        var (risk, action) = _classifier.Classify(0, PathCategory.PackageCache, RiskLevel.Eleve);
        risk.Should().Be(RiskLevel.Critique);
        action.Should().Be(RecommendedAction.NePasToucher);
    }

    // -------------------------------------------------------------------------
    // Test 6: MinRiskFloor=Moyen on Aucun -> Moyen, VerifierAvant.
    //   The action follows from the clamped risk (not NePasToucher because
    //   Moyen's natural action is VerifierAvant).
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_MinRiskFloorMoyen_ClampsAucunToMoyen_VerifierAvantAction()
    {
        var (risk, action) = _classifier.Classify(95, PathCategory.VendorShared, RiskLevel.Moyen);
        risk.Should().Be(RiskLevel.Moyen);
        action.Should().Be(RecommendedAction.VerifierAvant);
    }

    // -------------------------------------------------------------------------
    // Test 7: edge case — exact 80 should map to Aucun (NOT Faible).
    //   Common off-by-one trap; pin in test.
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_ExactBoundary80_IsAucun()
    {
        _classifier.Classify(80, null, null).Risk.Should().Be(RiskLevel.Aucun);
        _classifier.Classify(79, null, null).Risk.Should().Be(RiskLevel.Faible);
    }

    // -------------------------------------------------------------------------
    // Test 8: edge case — exact 60 should map to Faible (NOT Moyen).
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_ExactBoundary60_IsFaible()
    {
        _classifier.Classify(60, null, null).Risk.Should().Be(RiskLevel.Faible);
        _classifier.Classify(59, null, null).Risk.Should().Be(RiskLevel.Moyen);
    }

    // -------------------------------------------------------------------------
    // Test 9: edge case — exact 20 should map to Eleve (NOT Critique).
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_ExactBoundary20_IsEleve()
    {
        _classifier.Classify(20, null, null).Risk.Should().Be(RiskLevel.Eleve);
        _classifier.Classify(19, null, null).Risk.Should().Be(RiskLevel.Critique);
    }

    // -------------------------------------------------------------------------
    // Test 10: OsCritical + MinRiskFloor=Eleve still results in Critique
    //   (OsCritical takes precedence over the floor).
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_OsCriticalTakesPrecedenceOverFloor()
    {
        var (risk, action) = _classifier.Classify(
            100, PathCategory.OsCriticalDoNotPropose, RiskLevel.Eleve);
        risk.Should().Be(RiskLevel.Critique);
        action.Should().Be(RecommendedAction.NePasToucher);
    }

    // -------------------------------------------------------------------------
    // Test 11: scores well above 100 (defensive, in case caller doesn't clamp)
    //   still map to Aucun.
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_ScoreAbove100_StillAucun()
    {
        _classifier.Classify(150, null, null).Risk.Should().Be(RiskLevel.Aucun);
    }

    // -------------------------------------------------------------------------
    // Test 12: negative scores (defensive) still map to Critique.
    // -------------------------------------------------------------------------
    [Fact]
    public void Classify_NegativeScore_Critique()
    {
        _classifier.Classify(-1, null, null).Risk.Should().Be(RiskLevel.Critique);
    }
}
