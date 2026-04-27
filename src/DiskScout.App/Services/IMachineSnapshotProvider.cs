using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Lazy + TTL-cached provider of the per-machine state aggregate consumed by the
/// AppData orphan-detection pipeline (Phase 10) MultiSourceMatcher step.
/// </summary>
/// <remarks>
/// <para>
/// The default TTL is 5 minutes (see
/// <see cref="MachineSnapshotProvider"/> constructor). Repeat <see cref="GetAsync"/>
/// calls within the TTL window return the SAME <see cref="MachineSnapshot"/>
/// instance without re-running any enumerator. After the TTL expires (or after a
/// manual <see cref="Invalidate"/>), the next <c>GetAsync</c> rebuilds by running
/// all four enumerators concurrently via <c>Task.WhenAll</c>.
/// </para>
/// <para>
/// Implementations are thread-safe and re-entrant: concurrent <c>GetAsync</c>
/// callers during an in-flight build all see the same in-flight task and the
/// same final snapshot instance.
/// </para>
/// </remarks>
public interface IMachineSnapshotProvider
{
    /// <summary>
    /// Returns the cached snapshot if still fresh (age &lt; TTL), else builds a new
    /// one by running all four enumerators in parallel via <c>Task.WhenAll</c>.
    /// </summary>
    Task<MachineSnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Forces the next <see cref="GetAsync"/> call to rebuild from scratch.</summary>
    void Invalidate();
}
