namespace DiskScout.Models;

/// <summary>
/// Rich diagnostic record produced by the AppData orphan-detection pipeline
/// (Plan 10-04). Captures every signal that informed the final
/// <see cref="ConfidenceScore"/> + <see cref="Risk"/> + <see cref="Action"/>
/// triplet so the UI's "Pourquoi ?" panel (Plan 10-06) and the audit CSV
/// (Plan 10-06) can show the user exactly which rules and matchers fired
/// for any candidate.
/// </summary>
/// <param name="NodeId">FileSystemNode.Id of the original candidate.</param>
/// <param name="FullPath">The candidate's full path (post-ParentContextAnalyzer normalization may
/// be reflected in <see cref="ParentSignificantPath"/>; <see cref="FullPath"/> is the candidate
/// path the pipeline emitted, which is the same as the source FileSystemNode's FullPath).</param>
/// <param name="SizeBytes">Recursive size in bytes of the candidate folder.</param>
/// <param name="LastWriteUtc">Last-modified timestamp.</param>
/// <param name="ParentSignificantPath">Output of
/// <see cref="DiskScout.Services.IParentContextAnalyzer.GetSignificantParent"/> applied to
/// <see cref="FullPath"/>. May equal <see cref="FullPath"/> if the leaf was already significant.</param>
/// <param name="Category">First PathRule hit's <see cref="PathCategory"/>, or
/// <see cref="PathCategory.Generic"/> when no PathRule matched.</param>
/// <param name="MatchedSources">All matcher hits (Registry / Service / Driver / Appx).
/// Empty when nothing matched.</param>
/// <param name="TriggeredRules">All PathRule hits sorted by specificity DESC.
/// Empty when no rule matched.</param>
/// <param name="ConfidenceScore">Integer 0..100 from <see cref="DiskScout.Services.IConfidenceScorer"/>.
/// 100 = high probability of true residue (safe to delete).
/// 0   = high probability of an active artifact (do NOT touch).</param>
/// <param name="Risk">Risk band from <see cref="DiskScout.Services.IRiskLevelClassifier"/>.</param>
/// <param name="Action">User-facing recommended action mapped from <see cref="Risk"/>.</param>
/// <param name="Reason">Human-readable summary of the rule + matcher counts and final score.</param>
public sealed record AppDataOrphanCandidate(
    long NodeId,
    string FullPath,
    long SizeBytes,
    DateTime LastWriteUtc,
    string ParentSignificantPath,
    PathCategory Category,
    IReadOnlyList<MatcherHit> MatchedSources,
    IReadOnlyList<RuleHit> TriggeredRules,
    int ConfidenceScore,
    RiskLevel Risk,
    RecommendedAction Action,
    string Reason);
