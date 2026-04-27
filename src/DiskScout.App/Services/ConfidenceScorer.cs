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
        if (input.MatcherHits is not null)
        {
            foreach (var hit in input.MatcherHits)
            {
                score -= Math.Abs(hit.ScoreDelta);
            }
        }

        // Step 2: PathCategory adjustment (CONTEXT.md `<specifics>`).
        switch (input.PathRuleCategory)
        {
            case PathCategory.PackageCache:
                score -= 90;
                break;
            case PathCategory.DriverData:
                score -= 70;
                break;
            case PathCategory.CorporateAgent:
                score -= 80;
                break;
            case PathCategory.VendorShared:
                score -= 50;
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
