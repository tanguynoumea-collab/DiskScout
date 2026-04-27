using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Pure-function <see cref="IConfidenceScorer"/> implementation following
/// the scoring table in CONTEXT.md `<specifics>` exactly. Stateless;
/// thread-safe; no logger needed.
/// </summary>
public sealed class ConfidenceScorer : IConfidenceScorer
{
    private const int InitialScore = 100;

    public int Compute(AppDataOrphanInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        // OsCriticalDoNotPropose forces score to 0 (forced Critique).
        // The HardBlacklist gate in AppDataOrphanPipeline normally prevents
        // these from reaching the scorer at all (returns null instead), but
        // the scorer enforces the contract defensively.
        if (input.PathRuleCategory == PathCategory.OsCriticalDoNotPropose)
            return 0;

        int score = InitialScore;

        // Step 1: subtract matcher hits (ScoreDelta is negative; subtract its absolute value).
        // Phase-10-05 corpus tuning: cap aggregate matcher subtraction at MaxMatcherPenalty
        // so multiple matchers (4 sources × 3 hits = 12 hits worst case → -540) don't
        // collapse the score to 0/Critique on items the human audit graded Moyen/Faible.
        // The cap reflects that "vendor is installed" is a single signal regardless of how
        // many places it shows up; the PathRule category delta + MinRiskFloor handle the
        // up-clamp side of the safety story.
        const int MaxMatcherPenalty = 50;
        int matcherPenalty = 0;
        if (input.MatcherHits is not null)
        {
            foreach (var hit in input.MatcherHits)
            {
                matcherPenalty += Math.Abs(hit.ScoreDelta);
            }
        }
        if (matcherPenalty > MaxMatcherPenalty) matcherPenalty = MaxMatcherPenalty;
        score -= matcherPenalty;

        // Step 2: PathCategory adjustment.
        // Original CONTEXT.md `<specifics>` had: PackageCache=-90, DriverData=-70,
        // CorporateAgent=-80, VendorShared=-50. Phase-10-05 corpus tuning:
        // softened to -60/-50/-60/-40 because the original deltas combined with
        // matcher hits (-50 each, capped at 3 hits/source × 4 sources = -600 worst case)
        // collapse the score to 0 → Critique in too many cases the human audit
        // marked as Moyen / Eleve. The MinRiskFloor on each category compensates
        // by enforcing an UP-clamp; the goal here is to avoid forcing CRITIQUE
        // (the floors already prevent Aucun/Supprimer).
        switch (input.PathRuleCategory)
        {
            case PathCategory.PackageCache:
                score -= 60;
                break;
            case PathCategory.DriverData:
                score -= 50;
                break;
            case PathCategory.CorporateAgent:
                score -= 60;
                break;
            case PathCategory.VendorShared:
                score -= 40;
                break;
            case PathCategory.Generic:
            case null:
                // No adjustment.
                break;
            // OsCriticalDoNotPropose handled at top.
        }

        // Step 3: residue bonuses (additive).
        if (input.SizeBytes == 0)
            score += 20;

        var ageDays = (DateTime.UtcNow - input.LastWriteUtc).TotalDays;
        if (ageDays > 365)
            score += 15;
        else if (ageDays > 180)
            score += 10;

        if (!input.HasExeOrDll)
            score += 10;

        // Step 4: clamp to [0, 100].
        if (score < 0) score = 0;
        if (score > 100) score = 100;
        return score;
    }
}
