using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Stage-[6] of the AppData orphan-detection pipeline: computes the integer
/// confidence score (0..100) from the matcher hits + path-rule category +
/// residue-bonus inputs (folder size, last-write age, presence of binaries).
/// 100 = high probability of true residue (safe to delete).
/// 0   = high probability of an active artifact (do NOT touch).
/// </summary>
public interface IConfidenceScorer
{
    /// <summary>
    /// Compute the score per CONTEXT.md scoring table:
    /// <list type="bullet">
    ///   <item>Initial 100.</item>
    ///   <item>Subtract |MatcherHit.ScoreDelta| for each matcher hit.</item>
    ///   <item>PathCategory adjustment dominates: OsCriticalDoNotPropose -> 0;
    ///   PackageCache -90; CorporateAgent -80; DriverData -70; VendorShared -50.</item>
    ///   <item>Residue bonuses: Size==0 +20; LastWrite >365d +15; >180d +10;
    ///   no .exe/.dll inside +10.</item>
    /// </list>
    /// Result clamped to [0, 100].
    /// </summary>
    int Compute(AppDataOrphanInput input);
}

/// <summary>
/// Pure inputs for <see cref="IConfidenceScorer.Compute"/>. Built once per
/// AppData candidate by the pipeline before invoking the scorer.
/// </summary>
/// <param name="FullPath">The candidate full path (for diagnostics only).</param>
/// <param name="SizeBytes">Recursive size of the candidate.</param>
/// <param name="LastWriteUtc">Most-recent write timestamp of any file under the candidate.</param>
/// <param name="HasExeOrDll">True if the candidate's directory contains any .exe or .dll file.</param>
/// <param name="PathRuleCategory">First-matching PathRule's <see cref="PathCategory"/>, or null.</param>
/// <param name="MatcherHits">All hits from the 4 matchers (Registry / Service / Driver / Appx).</param>
public sealed record AppDataOrphanInput(
    string FullPath,
    long SizeBytes,
    DateTime LastWriteUtc,
    bool HasExeOrDll,
    PathCategory? PathRuleCategory,
    IReadOnlyList<MatcherHit> MatcherHits);
