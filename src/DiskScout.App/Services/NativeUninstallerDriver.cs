using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Production implementation of <see cref="INativeUninstallerDriver"/>.
///
/// <para><b>ParseCommand contract:</b></para>
/// <para>
/// Parses <c>UninstallString</c> (and optionally <c>QuietUninstallString</c>) into a normalized
/// <see cref="UninstallCommand"/>. Detects installer family (MSI / Inno Setup / NSIS / Generic)
/// and, when <c>preferSilent==true</c>, augments arguments with the family-appropriate silent
/// flags. Returns <c>null</c> when:
/// </para>
/// <list type="bullet">
///   <item>UninstallString is null/whitespace.</item>
///   <item><c>preferSilent==true</c> AND no silent variant can be derived (Generic / Unknown
///   without QuietUninstallString) — callers map this to <see cref="UninstallStatus.SilentNotSupported"/>.</item>
/// </list>
///
/// <para><b>RunAsync contract:</b></para>
/// <para>
/// Executes the command attached to a Win32 Job Object with
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> so cancellation tears down the entire process tree
/// (including descendants spawned by the uninstaller). Streams stdout+stderr lines via
/// <see cref="IProgress{T}"/> as they arrive. Enforces a 30-minute hard timeout.
/// </para>
/// </summary>
public sealed partial class NativeUninstallerDriver : INativeUninstallerDriver
{
    private readonly ILogger _logger;
    private readonly TimeSpan _hardTimeout;

    public NativeUninstallerDriver(ILogger logger)
        : this(logger, TimeSpan.FromMinutes(30))
    {
    }

    /// <summary>
    /// Test-only constructor allowing a shorter hard timeout to verify
    /// <see cref="UninstallStatus.Timeout"/> behaviour without waiting 30 minutes.
    /// </summary>
    internal NativeUninstallerDriver(ILogger logger, TimeSpan hardTimeout)
    {
        _logger = logger;
        _hardTimeout = hardTimeout;
    }

    /// <inheritdoc />
    public UninstallCommand? ParseCommand(
        InstalledProgram program,
        string? quietUninstallString,
        bool preferSilent)
    {
        ArgumentNullException.ThrowIfNull(program);

        // Step 1: when preferSilent and a quiet string is available, prefer it directly.
        if (preferSilent && !string.IsNullOrWhiteSpace(quietUninstallString))
        {
            var (qExe, qArgs) = SplitFirstToken(quietUninstallString!);
            if (string.IsNullOrEmpty(qExe))
            {
                return TryFallbackToUninstallString(program, preferSilent: true);
            }

            var qKind = DetectKind(qExe, qArgs);
            return new UninstallCommand(
                ExecutablePath: qExe,
                Arguments: qArgs,
                WorkingDirectory: ResolveWorkingDirectory(qExe),
                IsSilent: true,
                Kind: qKind);
        }

        return TryFallbackToUninstallString(program, preferSilent);
    }

    private UninstallCommand? TryFallbackToUninstallString(InstalledProgram program, bool preferSilent)
    {
        var raw = program.UninstallString;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var (exe, args) = SplitFirstToken(raw);
        if (string.IsNullOrEmpty(exe))
        {
            return null;
        }

        var kind = DetectKind(exe, args);

        if (!preferSilent)
        {
            return new UninstallCommand(
                ExecutablePath: exe,
                Arguments: args,
                WorkingDirectory: ResolveWorkingDirectory(exe),
                IsSilent: false,
                Kind: kind);
        }

        // preferSilent==true: try to add silent flags by family.
        if (kind is InstallerKind.Generic or InstallerKind.Unknown)
        {
            // We refuse to "guess" silent flags for unknown installers — running interactive
            // installers silently is dangerous. Wizard layer treats null+preferSilent as
            // SilentNotSupported.
            _logger.Information(
                "Cannot run silent uninstall for {DisplayName}; no silent variant available (Kind={Kind})",
                program.DisplayName,
                kind);
            return null;
        }

        var silentArgs = AppendSilentFlags(kind, args);
        // For MsiExec the executable may have been "MsiExec.exe" — use as-is (Process resolves via PATH).
        return new UninstallCommand(
            ExecutablePath: exe,
            Arguments: silentArgs,
            WorkingDirectory: ResolveWorkingDirectory(exe),
            IsSilent: true,
            Kind: kind);
    }

    /// <summary>
    /// Split a raw registry uninstall string into executable + arguments, honouring quoting.
    /// If the string starts with <c>"</c>, the executable is the chars between the first pair of
    /// quotes; remaining text (trimmed) becomes arguments. Otherwise, splits at the first
    /// whitespace.
    /// </summary>
    private static (string exe, string args) SplitFirstToken(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return (string.Empty, string.Empty);

        if (trimmed[0] == '"')
        {
            var closing = trimmed.IndexOf('"', startIndex: 1);
            if (closing < 0)
            {
                // Unterminated quote — best effort: treat the rest as exe path.
                return (trimmed.TrimStart('"'), string.Empty);
            }
            var exe = trimmed.Substring(1, closing - 1);
            var rest = closing + 1 < trimmed.Length ? trimmed[(closing + 1)..].TrimStart() : string.Empty;
            return (exe, rest);
        }

        var firstSpace = IndexOfWhitespace(trimmed);
        if (firstSpace < 0)
        {
            return (trimmed, string.Empty);
        }
        return (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].TrimStart());
    }

    private static int IndexOfWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsWhiteSpace(s[i])) return i;
        }
        return -1;
    }

    /// <summary>
    /// Detect installer family from executable filename and existing arguments.
    /// </summary>
    private static InstallerKind DetectKind(string exePath, string args)
    {
        if (string.IsNullOrEmpty(exePath)) return InstallerKind.Unknown;

        var fileName = SafeGetFileName(exePath);
        if (string.IsNullOrEmpty(fileName)) return InstallerKind.Unknown;

        if (fileName.Equals("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
        {
            return InstallerKind.MsiExec;
        }

        // NSIS check first: uninstall.exe is the canonical NSIS uninstaller name and must NOT
        // be misclassified as InnoSetup (which matches `unins*.exe`).
        if (fileName.Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase))
        {
            return InstallerKind.NsisExec;
        }

        // InnoSetup convention: filenames like unins000.exe, unins001.exe, uninst.exe.
        if (fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return InstallerKind.InnoSetup;
        }

        // Weak signal: bare "/S" token (not "/SILENT") in args suggests NSIS.
        if (ContainsTokenIgnoreCase(args, "/S"))
        {
            return InstallerKind.NsisExec;
        }

        return InstallerKind.Generic;
    }

    private static string SafeGetFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Append the family-appropriate silent flags. For MsiExec this also rewrites
    /// <c>/I{guid}</c> -> <c>/X{guid}</c> (an Install string mistakenly used as Uninstall).
    /// </summary>
    private static string AppendSilentFlags(InstallerKind kind, string args)
    {
        switch (kind)
        {
            case InstallerKind.MsiExec:
            {
                var rewritten = RewriteMsiInstallToUninstall(args);
                if (!ContainsTokenIgnoreCase(rewritten, "/qn"))
                {
                    rewritten = (rewritten.Length == 0 ? "" : rewritten + " ") + "/qn";
                }
                if (!ContainsTokenIgnoreCase(rewritten, "/norestart"))
                {
                    rewritten += " /norestart";
                }
                return rewritten.Trim();
            }
            case InstallerKind.InnoSetup:
            {
                var s = args.Length == 0 ? "" : args + " ";
                s += "/SILENT /SUPPRESSMSGBOXES /NORESTART";
                return s.Trim();
            }
            case InstallerKind.NsisExec:
            {
                if (ContainsTokenIgnoreCase(args, "/S"))
                {
                    return args;
                }
                return (args.Length == 0 ? "" : args + " ") + "/S";
            }
            default:
                return args;
        }
    }

    private static string RewriteMsiInstallToUninstall(string args)
    {
        // Replace "/I{" with "/X{" preserving any existing "/X{...}" untouched.
        // Case-insensitive search; preserve original GUID casing.
        var idx = 0;
        var result = args;
        while (idx < result.Length)
        {
            var found = result.IndexOf("/I{", idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;
            // Confirm this is exactly "/I{" with case-insensitive compare on the slash+letter.
            // Replace just the 'I' (or 'i') with 'X' preserving the brace.
            var ch = result[found + 1];
            var replacement = char.IsUpper(ch) ? 'X' : 'x';
            result = result[..(found + 1)] + replacement + result[(found + 2)..];
            idx = found + 3;
        }
        return result;
    }

    private static bool ContainsTokenIgnoreCase(string args, string token)
    {
        // Word-ish boundary: token must be preceded by start-of-string or whitespace.
        if (args.Length == 0) return false;
        var idx = 0;
        while (idx < args.Length)
        {
            var found = args.IndexOf(token, idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            var precededByBoundary = found == 0 || char.IsWhiteSpace(args[found - 1]);
            var endsCleanly = found + token.Length == args.Length ||
                              char.IsWhiteSpace(args[found + token.Length]);
            if (precededByBoundary && endsCleanly) return true;
            idx = found + 1;
        }
        return false;
    }

    private static string? ResolveWorkingDirectory(string exePath)
    {
        try
        {
            if (!Path.IsPathRooted(exePath)) return null;
            var dir = Path.GetDirectoryName(exePath);
            return string.IsNullOrEmpty(dir) ? null : dir;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UninstallOutcome> RunAsync(
        UninstallCommand command,
        IProgress<string>? output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1. Pre-flight: refuse to start a process whose executable does not exist on disk.
        //    MsiExec.exe is the one acceptable bare filename — Process resolves it via PATH.
        if (!IsLikelyResolvableExecutable(command.ExecutablePath))
        {
            return new UninstallOutcome(
                UninstallStatus.ExecutionFailure,
                ExitCode: null,
                Elapsed: TimeSpan.Zero,
                CapturedOutputLineCount: 0,
                ErrorMessage: $"Executable not found: {command.ExecutablePath}");
        }

        // 2. Create a Job Object with KILL_ON_JOB_CLOSE so any child processes spawned by the
        //    uninstaller die when we close the handle. This is the linchpin of safe cancellation.
        var hJob = CreateJobObject(IntPtr.Zero, lpName: null);
        if (hJob == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            return new UninstallOutcome(
                UninstallStatus.ExecutionFailure,
                ExitCode: null,
                Elapsed: TimeSpan.Zero,
                CapturedOutputLineCount: 0,
                ErrorMessage: $"CreateJobObject failed (Win32 error {err})");
        }

        var jobObjectClosed = false;

        try
        {
            // 3. Tell the Job Object to kill all assigned processes when its handle closes.
            if (!ConfigureJobKillOnClose(hJob, out var jobErr))
            {
                _logger.Warning(
                    "SetInformationJobObject(KILL_ON_JOB_CLOSE) failed for {Exe} (Win32 error {Err}); falling back to Process.Kill(entireProcessTree)",
                    command.ExecutablePath,
                    jobErr);
            }

            var lineCount = 0;
            var stopwatch = Stopwatch.StartNew();

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command.ExecutablePath,
                    Arguments = command.Arguments ?? string.Empty,
                    WorkingDirectory = command.WorkingDirectory ?? string.Empty,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = command.IsSilent,
                },
                EnableRaisingEvents = false,
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                Interlocked.Increment(ref lineCount);
                output?.Report(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                Interlocked.Increment(ref lineCount);
                output?.Report(e.Data);
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to start uninstaller {Exe}", command.ExecutablePath);
                return new UninstallOutcome(
                    UninstallStatus.ExecutionFailure,
                    ExitCode: null,
                    Elapsed: stopwatch.Elapsed,
                    CapturedOutputLineCount: 0,
                    ErrorMessage: ex.Message);
            }

            // 4. Assign the freshly-started process to our Job Object IMMEDIATELY so any
            //    descendants (also assigned automatically by Windows) die with us on cancel.
            //    On failure we keep going — Process.Kill(entireProcessTree:true) is the fallback.
            try
            {
                if (!AssignProcessToJobObject(hJob, process.Handle))
                {
                    var assignErr = Marshal.GetLastWin32Error();
                    _logger.Warning(
                        "AssignProcessToJobObject failed for PID {Pid} ({Exe}) — Win32 error {Err}; relying on Process.Kill fallback",
                        process.Id,
                        command.ExecutablePath,
                        assignErr);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "AssignProcessToJobObject threw for PID {Pid}", process.Id);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_hardTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                stopwatch.Stop();

                // Drain output streams (BeginOutputReadLine guarantees flush after WaitForExit).
                try { process.WaitForExit(2000); } catch { /* best-effort */ }

                var exitCode = SafeReadExitCode(process);
                var status = exitCode == 0 ? UninstallStatus.Success : UninstallStatus.NonZeroExit;
                _logger.Information(
                    "Uninstaller exited: {Status} code={Code} elapsed={Sec}s lines={Lines} exe={Exe}",
                    status, exitCode, stopwatch.Elapsed.TotalSeconds, lineCount, command.ExecutablePath);

                return new UninstallOutcome(
                    status,
                    ExitCode: exitCode,
                    Elapsed: stopwatch.Elapsed,
                    CapturedOutputLineCount: lineCount,
                    ErrorMessage: null);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();

                var userCancelled = cancellationToken.IsCancellationRequested;
                var status = userCancelled ? UninstallStatus.Cancelled : UninstallStatus.Timeout;

                // Tree-kill: closing the job handle fires KILL_ON_JOB_CLOSE on every assigned process.
                // CloseHandle(hJob) below (in finally) ultimately does this; we additionally call
                // Process.Kill(entireProcessTree:true) as belt-and-braces in case the Job assign failed.
                TreeKillBestEffort(process, command.ExecutablePath);

                // Wait briefly for the child to actually exit so we can drain output streams.
                try { process.WaitForExit(2000); } catch { /* best-effort */ }

                _logger.Information(
                    "Uninstaller {Status} after {Sec}s lines={Lines} exe={Exe}",
                    status, stopwatch.Elapsed.TotalSeconds, lineCount, command.ExecutablePath);

                return new UninstallOutcome(
                    status,
                    ExitCode: SafeReadExitCode(process),
                    Elapsed: stopwatch.Elapsed,
                    CapturedOutputLineCount: lineCount,
                    ErrorMessage: null);
            }
        }
        finally
        {
            // Closing the Job handle fires KILL_ON_JOB_CLOSE for any still-running assigned
            // processes (children spawned after we set the limit flag).
            if (!jobObjectClosed)
            {
                CloseHandle(hJob);
                jobObjectClosed = true;
            }
        }
    }

    private static bool IsLikelyResolvableExecutable(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        if (File.Exists(exePath)) return true;

        // For non-rooted commands, assume Windows can resolve them via PATH.
        // (MsiExec.exe is the canonical case; cmd.exe is similar in tests.)
        return !Path.IsPathRooted(exePath);
    }

    private static int? SafeReadExitCode(Process p)
    {
        try
        {
            return p.HasExited ? p.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private void TreeKillBestEffort(Process process, string exeForLog)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Process.Kill(entireProcessTree) failed for {Exe}", exeForLog);
        }
    }

    private static bool ConfigureJobKillOnClose(IntPtr hJob, out int win32Error)
    {
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
            var ok = SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, ptr, (uint)size);
            win32Error = ok ? 0 : Marshal.GetLastWin32Error();
            return ok;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // ----- Win32 P/Invoke for Job Object lifecycle ---------------------------------------

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
