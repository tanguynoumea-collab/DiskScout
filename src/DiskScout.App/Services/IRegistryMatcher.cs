using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Stage-[4] matcher: probes the registry-installed-programs list for evidence
/// that the AppData candidate matches an installed program (by InstallLocation
/// prefix, by fuzzy publisher / displayName match, or by canonical-publisher
/// containment when the alias resolver from Plan 10-03 surfaced a hit).
/// One <see cref="MatcherHit"/> per matched program (capped at 3).
/// </summary>
/// <remarks>
/// New in Plan 10-04. Score delta -50 per CONTEXT.md.
/// </remarks>
public interface IRegistryMatcher
{
    /// <summary>
    /// Returns the list of registry-source matches against the installed-programs list.
    /// Empty list (never null) if no program matches.
    /// </summary>
    /// <param name="folderName">The leaf folder name (or significant parent name) of the candidate.</param>
    /// <param name="canonicalPublisher">Optional publisher canonical resolved by Plan 10-03's alias resolver.</param>
    /// <param name="candidateFullPath">The candidate's full path (for InstallLocation prefix match).</param>
    /// <param name="programs">The installed-programs list passed to OrphanDetectorService.DetectAsync.</param>
    IReadOnlyList<MatcherHit> Match(string folderName, string? canonicalPublisher, string candidateFullPath, IReadOnlyList<InstalledProgram> programs);
}
