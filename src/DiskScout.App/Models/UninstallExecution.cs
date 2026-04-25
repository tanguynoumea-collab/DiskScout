namespace DiskScout.Models;

/// <summary>
/// Family of uninstaller technology detected from <c>UninstallString</c>.
/// Drives silent-flag selection in <see cref="DiskScout.Services.INativeUninstallerDriver"/>.
/// </summary>
public enum InstallerKind
{
    Unknown,
    MsiExec,
    InnoSetup,
    NsisExec,
    WixBootstrapper,
    Generic,
}

/// <summary>
/// Terminal status of a native uninstaller execution. The wizard layer maps these to
/// user-facing messages and to deciding whether to proceed to residue scan.
/// </summary>
public enum UninstallStatus
{
    /// <summary>Process exited with code 0.</summary>
    Success,
    /// <summary>Process exited with non-zero code; <see cref="UninstallOutcome.ExitCode"/> populated.</summary>
    NonZeroExit,
    /// <summary>Silent execution requested but no silent variant could be derived (Generic / Unknown without QuietUninstallString).</summary>
    SilentNotSupported,
    /// <summary>Hard 30-minute timeout fired; process tree was killed.</summary>
    Timeout,
    /// <summary>Caller-provided <see cref="System.Threading.CancellationToken"/> fired; process tree was killed.</summary>
    Cancelled,
    /// <summary>UninstallString could not be parsed into a runnable command.</summary>
    ParseFailure,
    /// <summary>Process could not be started (file missing, JobObject creation failure, etc.). <see cref="UninstallOutcome.ErrorMessage"/> populated.</summary>
    ExecutionFailure,
}

/// <summary>
/// Normalized command produced by <c>INativeUninstallerDriver.ParseCommand</c>.
/// Contains the resolved executable, arguments, working directory, and silent-mode hint
/// derived from the registry's UninstallString / QuietUninstallString.
/// </summary>
public sealed record UninstallCommand(
    string ExecutablePath,
    string Arguments,
    string? WorkingDirectory,
    bool IsSilent,
    InstallerKind Kind);

/// <summary>
/// Result of running an <see cref="UninstallCommand"/> via <c>INativeUninstallerDriver.RunAsync</c>.
/// </summary>
public sealed record UninstallOutcome(
    UninstallStatus Status,
    int? ExitCode,
    TimeSpan Elapsed,
    int CapturedOutputLineCount,
    string? ErrorMessage);
