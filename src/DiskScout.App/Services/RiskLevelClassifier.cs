using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Pure-function <see cref="IRiskLevelClassifier"/>. Stateless,
/// thread-safe, no dependencies. CONTEXT.md `<specifics>` banding +
/// MinRiskFloor clamp + OsCriticalDoNotPropose hard override.
/// </summary>
public sealed class RiskLevelClassifier : IRiskLevelClassifier
{
    public (RiskLevel Risk, RecommendedAction Action) Classify(
        int confidenceScore,
        PathCategory? pathRuleCategory,
        RiskLevel? minRiskFloor)
    {
        // OsCriticalDoNotPropose hard override: defense-in-depth.
        // The pipeline's HardBlacklist gate normally returns null before reaching
        // the classifier, but we honor the contract here as a final guard.
        if (pathRuleCategory == PathCategory.OsCriticalDoNotPropose)
            return (RiskLevel.Critique, RecommendedAction.NePasToucher);

        // Score-band classification per CONTEXT.md.
        RiskLevel risk;
        if (confidenceScore >= 80) risk = RiskLevel.Aucun;
        else if (confidenceScore >= 60) risk = RiskLevel.Faible;
        else if (confidenceScore >= 40) risk = RiskLevel.Moyen;
        else if (confidenceScore >= 20) risk = RiskLevel.Eleve;
        else risk = RiskLevel.Critique;

        // MinRiskFloor clamp UP. Higher RiskLevel int = more dangerous, so
        // pick the max(risk, floor).
        if (minRiskFloor.HasValue && (int)minRiskFloor.Value > (int)risk)
        {
            risk = minRiskFloor.Value;
        }

        return (risk, MapAction(risk));
    }

    private static RecommendedAction MapAction(RiskLevel risk) => risk switch
    {
        RiskLevel.Aucun => RecommendedAction.Supprimer,
        RiskLevel.Faible => RecommendedAction.CorbeilleOk,
        RiskLevel.Moyen => RecommendedAction.VerifierAvant,
        RiskLevel.Eleve => RecommendedAction.NePasToucher,
        RiskLevel.Critique => RecommendedAction.NePasToucher,
        _ => RecommendedAction.NePasToucher,
    };
}
