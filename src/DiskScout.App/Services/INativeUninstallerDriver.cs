using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Driver that parses an <see cref="InstalledProgram"/>'s registry uninstall strings into a
/// normalized command and runs the underlying uninstaller as a child process attached to a
/// Win32 Job Object (so cancellation reliably tears down the entire installer process tree).
/// Streams stdout/stderr line-by-line via <see cref="IProgress{T}"/> while the process is running.
/// </summary>
public interface INativeUninstallerDriver
{
    /// <summary>
    /// Parse <see cref="InstalledProgram.UninstallString"/> (and optional
    /// <paramref name="quietUninstallString"/>) into a normalized command.
    /// If <paramref name="preferSilent"/> is <c>true</c> and a silent variant is supported,
    /// <see cref="UninstallCommand.IsSilent"/> on the result will be <c>true</c>.
    /// Returns <c>null</c> on parse failure (no usable executable identified) or when
    /// <paramref name="preferSilent"/> is <c>true</c> and no silent variant can be derived
    /// (callers should treat <c>null</c> + <c>preferSilent==true</c> as
    /// <see cref="UninstallStatus.SilentNotSupported"/>).
    /// </summary>
    UninstallCommand? ParseCommand(
        InstalledProgram program,
        string? quietUninstallString,
        bool preferSilent);

    /// <summary>
    /// Execute the command. Streams stdout+stderr lines through <paramref name="output"/> as they arrive.
    /// Hard timeout: 30 minutes. Cancellation kills the process tree within 2 seconds via Job Object.
    /// </summary>
    Task<UninstallOutcome> RunAsync(
        UninstallCommand command,
        IProgress<string>? output,
        CancellationToken cancellationToken);
}
