namespace DiskScout.Services;

/// <summary>
/// Abstraction over the Windows service enumerator. Lets tests inject deterministic
/// service fixtures without spinning up real services. The default production
/// implementation reads <c>HKLM\SYSTEM\CurrentControlSet\Services</c> via the registry
/// (kept package-private inside <see cref="ResidueScanner"/> as <c>WmiServiceEnumerator</c>).
/// </summary>
/// <remarks>
/// Promoted to <c>public</c> in Plan 10-02 (was <c>internal</c> in <see cref="ResidueScanner"/>)
/// so the new <c>MachineSnapshotProvider</c> + matchers under <c>Services/Matchers/*</c> can
/// consume it as a first-class injectable seam.
/// </remarks>
public interface IServiceEnumerator
{
    /// <summary>
    /// Tuple stream: (Name, DisplayName, BinaryPath). State is added in the snapshot
    /// layer for now — the production implementation does not expose state today;
    /// the snapshot adds <c>Status="Unknown"</c> until a future enrichment pass.
    /// </summary>
    IEnumerable<(string Name, string DisplayName, string? BinaryPath)> EnumerateServices();
}
