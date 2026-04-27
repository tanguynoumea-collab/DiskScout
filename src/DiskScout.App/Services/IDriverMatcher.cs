using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Stage-[4] matcher: probes the snapshot's third-party-driver list for evidence
/// that the AppData candidate's parent vendor still ships an installed driver.
/// One <see cref="MatcherHit"/> per matched driver (capped at 3 by the impl).
/// </summary>
/// <remarks>
/// New in Plan 10-04. Score delta -45 per CONTEXT.md.
/// </remarks>
public interface IDriverMatcher
{
    /// <summary>
    /// Returns the list of driver-source matches against the snapshot.
    /// Empty list (never null) if no driver matches.
    /// </summary>
    IReadOnlyList<MatcherHit> Match(string folderName, string? canonicalPublisher, MachineSnapshot snapshot);
}
