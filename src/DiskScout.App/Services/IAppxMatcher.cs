using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Stage-[4] matcher: probes the snapshot's Appx (UWP/MSIX) package list for
/// evidence that the AppData candidate is the runtime data of an installed
/// package. One <see cref="MatcherHit"/> per matched package (capped at 3).
/// </summary>
/// <remarks>
/// New in Plan 10-04. Score delta -50 per CONTEXT.md.
/// </remarks>
public interface IAppxMatcher
{
    /// <summary>
    /// Returns the list of Appx-source matches against the snapshot.
    /// Empty list (never null) if no package matches.
    /// </summary>
    /// <param name="folderName">The leaf folder name (or significant parent name) of the candidate.</param>
    /// <param name="canonicalPublisher">Optional publisher canonical resolved by Plan 10-03's alias resolver.</param>
    /// <param name="parentDirectory">The candidate path's parent directory (used for InstallLocation prefix match).</param>
    /// <param name="snapshot">The pre-built machine snapshot.</param>
    IReadOnlyList<MatcherHit> Match(string folderName, string? canonicalPublisher, string? parentDirectory, MachineSnapshot snapshot);
}
