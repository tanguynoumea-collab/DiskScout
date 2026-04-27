namespace DiskScout.Models;

public enum OrphanCategory
{
    AppDataOrphan,
    EmptyProgramFiles,
    StaleTemp,
    OrphanInstallerPatch,
    SystemArtifact,
    DevCache,
    BrowserCache,
    EmptyFolder,
    BrokenShortcut,
}

/// <summary>
/// Surface candidate emitted by <see cref="DiskScout.Services.IOrphanDetectorService"/>.
/// The positional constructor signature is unchanged from Phase 9 — every existing
/// caller continues to work without modification. The optional
/// <see cref="Diagnostics"/> init-only property is populated ONLY by the
/// AppData branch (via Plan 10-04's <c>AppDataOrphanPipeline</c>); for the
/// 8 other categories it stays <c>null</c>. Backward-compatible.
/// </summary>
public sealed record OrphanCandidate(
    long NodeId,
    string FullPath,
    long SizeBytes,
    OrphanCategory Category,
    string Reason,
    double? MatchScore)
{
    /// <summary>
    /// Optional rich diagnostics — populated only for AppData entries by
    /// <c>AppDataOrphanPipeline</c> (Phase 10). Null for every other
    /// <see cref="OrphanCategory"/>. Existing consumers that ignore this
    /// property continue to work unchanged (init-only, default null).
    /// </summary>
    public AppDataOrphanCandidate? Diagnostics { get; init; }
}
