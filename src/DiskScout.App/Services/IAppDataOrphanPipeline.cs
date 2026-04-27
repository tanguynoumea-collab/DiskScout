using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// 7-step AppData orphan-detection orchestrator (Plan 10-04). Wires the
/// foundations from Plans 10-01..10-03 into a single per-candidate evaluation
/// pass:
/// <list type="number">
///   <item>HardBlacklist: <see cref="IPathRuleEngine.Match"/> hit on
///   <see cref="PathCategory.OsCriticalDoNotPropose"/> -> returns <c>null</c>
///   (candidate is suppressed entirely; user never sees it).</item>
///   <item>ParentContextAnalyzer: walk up generic leaves to the significant parent.</item>
///   <item>KnownPathRules: collect non-OsCritical rule hits + min risk floor.</item>
///   <item>MultiSourceMatcher: Registry + Service + Driver + Appx run against
///   <see cref="MachineSnapshot"/>.</item>
///   <item>PublisherAliasResolver: bridge folder name to canonical publisher
///   (used by RegistryMatcher for the canonical containment check).</item>
///   <item>ConfidenceScorer: integer 0..100.</item>
///   <item>RiskLevelClassifier: (RiskLevel, RecommendedAction) with floor clamp.</item>
/// </list>
/// </summary>
public interface IAppDataOrphanPipeline
{
    /// <summary>
    /// Evaluate <paramref name="node"/> through the full pipeline. Returns
    /// <c>null</c> ONLY when the HardBlacklist gate (step 1) fires; every
    /// other code path produces a non-null <see cref="AppDataOrphanCandidate"/>
    /// (the UI shows ALL flagged AppData entries with their score so the user
    /// can sort and review).
    /// </summary>
    /// <param name="node">The FileSystemNode under an AppData root.</param>
    /// <param name="programs">The installed-programs list passed to <c>OrphanDetectorService.DetectAsync</c>.</param>
    /// <param name="cancellationToken">Cancellation token honored at every stage.</param>
    Task<AppDataOrphanCandidate?> EvaluateAsync(
        FileSystemNode node,
        IReadOnlyList<InstalledProgram> programs,
        CancellationToken cancellationToken = default);
}
