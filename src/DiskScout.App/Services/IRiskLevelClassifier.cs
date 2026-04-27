using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Stage-[7] of the AppData orphan-detection pipeline: maps the integer
/// confidence score (and optional path-rule context) to a
/// (<see cref="RiskLevel"/>, <see cref="RecommendedAction"/>) tuple per
/// CONTEXT.md banding.
/// </summary>
public interface IRiskLevelClassifier
{
    /// <summary>
    /// Classify the score into a risk band:
    /// <list type="bullet">
    ///   <item>>= 80 -> (Aucun, Supprimer)</item>
    ///   <item>60..79 -> (Faible, CorbeilleOk)</item>
    ///   <item>40..59 -> (Moyen, VerifierAvant)</item>
    ///   <item>20..39 -> (Eleve, NePasToucher)</item>
    ///   <item>&lt; 20 -> (Critique, NePasToucher)</item>
    /// </list>
    /// If <paramref name="pathRuleCategory"/> is
    /// <see cref="PathCategory.OsCriticalDoNotPropose"/> the result is forced
    /// to (Critique, NePasToucher) regardless of score.
    ///
    /// If <paramref name="minRiskFloor"/> is supplied (e.g. PackageCache or
    /// CorporateAgent), the result is clamped UP to that floor: a score of 80
    /// with floor=Eleve returns (Eleve, NePasToucher).
    /// </summary>
    (RiskLevel Risk, RecommendedAction Action) Classify(
        int confidenceScore,
        PathCategory? pathRuleCategory,
        RiskLevel? minRiskFloor);
}
