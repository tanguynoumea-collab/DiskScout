namespace DiskScout.Models;

/// <summary>
/// Category of artifact left behind after a program is uninstalled.
/// Aligned with CONTEXT.md D-02 ("Revo Pro level"): all 7 categories MUST be implemented.
/// </summary>
public enum ResidueCategory
{
    /// <summary>Registry key (HKLM/HKCU/HKCR) — Path is the full hive\subkey string.</summary>
    Registry,

    /// <summary>File or directory on disk — Path is the absolute filesystem path.</summary>
    Filesystem,

    /// <summary>Start-menu / desktop / public-desktop *.lnk — Path is the .lnk path.</summary>
    Shortcut,

    /// <summary>Orphan MSI patch under %WINDIR%\Installer — Path is the .msp absolute path.</summary>
    MsiPatch,

    /// <summary>Windows service residual — Path is "Service:{ServiceName}".</summary>
    Service,

    /// <summary>Scheduled task residual — Path is "ScheduledTask:{TaskPath}".</summary>
    ScheduledTask,

    /// <summary>Shell extension (Explorer ContextMenuHandler / CLSID InprocServer32) — Path is the registry CLSID key.</summary>
    ShellExtension,
}

/// <summary>
/// Confidence the finding is genuinely orphan residue from the targeted program.
/// HighConfidence = trace-correlated (we logged the file/key being created during install).
/// MediumConfidence = name/publisher heuristic with strong fuzzy match.
/// LowConfidence = weak signal (e.g., publisher prefix only, file in shared cache dir).
/// </summary>
public enum ResidueTrustLevel
{
    HighConfidence,
    MediumConfidence,
    LowConfidence,
}

/// <summary>
/// Provenance of a residue finding — drives trust and UI badge.
/// </summary>
public enum ResidueSource
{
    /// <summary>Discovered via cross-reference with an InstallTrace from Plan 09-01.</summary>
    TraceMatch,

    /// <summary>Discovered via a Plan 09-04 publisher rule (precise pattern, e.g., "Adobe leaves AcroCEF in LocalAppData").</summary>
    PublisherRule,

    /// <summary>Discovered via the generic name / publisher fuzzy heuristic.</summary>
    NameHeuristic,
}

/// <summary>
/// One residual artifact discovered after uninstall.
/// </summary>
/// <param name="Category">Which residue surface this finding lives on.</param>
/// <param name="Path">
/// For Filesystem/Shortcut/MsiPatch: absolute filesystem path.
/// For Registry/ShellExtension: full hive\subkey string.
/// For Service: "Service:{name}". For ScheduledTask: "ScheduledTask:{taskPath}".
/// </param>
/// <param name="SizeBytes">Bytes that would be reclaimed. 0 for registry/services/tasks/shell-extensions.</param>
/// <param name="Reason">Human-readable explanation of which heuristic fired (grep-stable).</param>
/// <param name="Trust">Confidence — drives the wizard's default-checked state.</param>
/// <param name="Source">How the finding was discovered.</param>
public sealed record ResidueFinding(
    ResidueCategory Category,
    string Path,
    long SizeBytes,
    string Reason,
    ResidueTrustLevel Trust,
    ResidueSource Source);

/// <summary>
/// Identity of the program being scanned for residue.
/// Mirrors the subset of <see cref="InstalledProgram"/> needed by the scanner.
/// </summary>
/// <param name="DisplayName">User-visible name (e.g., "Adobe Acrobat DC").</param>
/// <param name="Publisher">Publisher name (e.g., "Adobe Inc."). Optional.</param>
/// <param name="InstallLocation">Install root directory if known. Optional.</param>
/// <param name="RegistryKeyName">The Uninstall subkey name (often a GUID for MSI). Optional.</param>
public sealed record ResidueScanTarget(
    string DisplayName,
    string? Publisher,
    string? InstallLocation,
    string? RegistryKeyName);
