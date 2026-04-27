namespace DiskScout.Services;

/// <summary>
/// Abstraction over the scheduled-task enumerator. Lets tests inject deterministic
/// task fixtures without launching <c>schtasks.exe</c>. The default production
/// implementation shells out to <c>schtasks /query /fo CSV /v</c> with a 10s timeout
/// (kept package-private inside <see cref="ResidueScanner"/> as <c>SchTasksEnumerator</c>).
/// </summary>
/// <remarks>
/// Promoted to <c>public</c> in Plan 10-02 (was <c>internal</c> in <see cref="ResidueScanner"/>)
/// so the new <c>MachineSnapshotProvider</c> + matchers under <c>Services/Matchers/*</c> can
/// consume it as a first-class injectable seam.
/// </remarks>
public interface IScheduledTaskEnumerator
{
    /// <summary>Tuple stream: (TaskPath, Author, ActionPath).</summary>
    IEnumerable<(string TaskPath, string? Author, string? ActionPath)> EnumerateTasks();
}
