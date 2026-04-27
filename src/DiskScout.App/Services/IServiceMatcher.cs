using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Stage-[4] matcher: probes the snapshot's Windows-services list for evidence
/// that the AppData candidate's parent vendor still ships an installed service.
/// One <see cref="MatcherHit"/> per matched service (capped at 3 by the impl to
/// prevent unbounded score collapse when many service names overlap).
/// </summary>
/// <remarks>
/// New in Plan 10-04. Production implementation is <see cref="ServiceMatcher"/>
/// which iterates <see cref="MachineSnapshot.Services"/>. The Service running /
/// stopped state is NOT exposed by the current <see cref="IServiceEnumerator"/>
/// (Plan 10-02 left this for a future enrichment pass), so the matcher uses the
/// average score delta of -45 (between the -60 Running and -30 Stopped values
/// from CONTEXT.md) and surfaces a <c>// TODO</c> marker.
/// </remarks>
public interface IServiceMatcher
{
    /// <summary>
    /// Returns the list of service-source matches against the snapshot.
    /// Empty list (never null) if no service name overlaps.
    /// </summary>
    /// <param name="folderName">The leaf folder name (or significant parent name) of the candidate.</param>
    /// <param name="canonicalPublisher">Optional publisher canonical resolved by Plan 10-03's alias resolver.</param>
    /// <param name="parentDirectory">The candidate path's parent directory (used for binary-path prefix match).</param>
    /// <param name="snapshot">The pre-built machine snapshot from <see cref="IMachineSnapshotProvider"/>.</param>
    IReadOnlyList<MatcherHit> Match(string folderName, string? canonicalPublisher, string? parentDirectory, MachineSnapshot snapshot);
}
