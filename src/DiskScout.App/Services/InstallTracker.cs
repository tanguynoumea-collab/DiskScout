using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

public sealed class InstallTracker : IInstallTracker, IDisposable
{
    private const string TrackerVersion = "1.0";

    // Win32 RegNotifyChangeKeyValue constants
    private const int REG_NOTIFY_CHANGE_NAME = 0x1;
    private const int REG_NOTIFY_CHANGE_LAST_SET = 0x4;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        IntPtr hKey, bool watchSubtree, int notifyFilter, IntPtr hEvent, bool asynchronous);

    private static readonly IReadOnlyList<string> DefaultExcludedPathSubstrings = new[]
    {
        "\\$Recycle.Bin\\",
        "\\pagefile.sys",
        "\\hiberfil.sys",
        "\\swapfile.sys",
        "\\DiskScout\\diskscout.log",
        "\\AppData\\Local\\DiskScout\\install-traces\\",
        "\\AppData\\Local\\DiskScout\\quarantine\\",
    };

    private readonly ILogger _logger;
    private readonly IInstallTraceStore _store;
    private readonly IReadOnlyList<string> _roots;
    private readonly IReadOnlyList<string> _excludedPathSubstrings;

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _fsWatchers = new();
    private readonly List<RegistryWatch> _regWatches = new();
    private readonly HashSet<(InstallTraceEventKind Kind, string Path)> _seen =
        new(EventKeyComparer.Instance);
    private readonly List<InstallTraceEvent> _events = new();
    private readonly ManualResetEventSlim _stopEvent = new(initialState: false);

    private DateTime _startedUtc;
    private string? _commandLine;
    private string? _productHint;
    private bool _disposed;

    public InstallTracker(
        ILogger logger,
        IInstallTraceStore store,
        IReadOnlyList<string>? roots = null,
        IReadOnlyList<string>? excludedPathSubstrings = null)
    {
        _logger = logger;
        _store = store;
        _roots = roots ?? DefaultRoots();
        _excludedPathSubstrings = excludedPathSubstrings ?? DefaultExcludedPathSubstrings;
    }

    public bool IsTracking { get; private set; }

    public Task StartAsync(string? installerCommandLine, string? installerProductHint, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (IsTracking)
                throw new InvalidOperationException("Tracker already running.");

            _commandLine = installerCommandLine;
            _productHint = installerProductHint;
            _startedUtc = DateTime.UtcNow;
            _events.Clear();
            _seen.Clear();
            _stopEvent.Reset();

            StartFileSystemWatchers();
            StartRegistryWatchers();

            IsTracking = true;
        }

        _logger.Information("Install tracker started; roots={Roots}", string.Join(", ", _roots));
        return Task.CompletedTask;
    }

    public async Task<InstallTrace> StopAsync(CancellationToken cancellationToken = default)
    {
        Task[] regTasks;
        lock (_gate)
        {
            if (!IsTracking)
            {
                throw new InvalidOperationException("Tracker is not running.");
            }

            // Signal background reg watchers to exit.
            _stopEvent.Set();

            // Tear down FS watchers first (stops new events from being raised).
            foreach (var w in _fsWatchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error disposing FileSystemWatcher");
                }
            }
            _fsWatchers.Clear();

            regTasks = _regWatches.Select(r => r.Task).ToArray();
        }

        // Wait up to 2s for reg watchers to exit, then force-dispose handles.
        try
        {
            await Task.WhenAll(regTasks).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.Warning("Timeout waiting for registry watchers to stop");
        }
        catch (OperationCanceledException)
        {
            // honour caller cancellation but still proceed to dispose handles below
        }

        InstallTrace trace;
        lock (_gate)
        {
            foreach (var r in _regWatches)
            {
                r.Dispose();
            }
            _regWatches.Clear();

            var stoppedUtc = DateTime.UtcNow;
            var header = new InstallTraceHeader(
                TraceId: BuildTraceId(_startedUtc),
                TrackerVersion: TrackerVersion,
                StartedUtc: _startedUtc,
                StoppedUtc: stoppedUtc,
                InstallerCommandLine: _commandLine,
                InstallerProductHint: _productHint);

            var snapshot = _events.ToArray();
            trace = new InstallTrace(header, snapshot);

            IsTracking = false;

            _logger.Information(
                "Install tracker stopped; events={Count} duration={Sec}s",
                trace.Events.Count,
                (trace.Header.StoppedUtc - trace.Header.StartedUtc).TotalSeconds);
        }

        // Persist outside the lock — store implementations may be slow / IO-bound.
        try
        {
            await _store.SaveAsync(trace, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to persist install trace {TraceId}", trace.Header.TraceId);
        }

        return trace;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            lock (_gate)
            {
                _stopEvent.Set();
                foreach (var w in _fsWatchers)
                {
                    try { w.Dispose(); } catch { /* best-effort */ }
                }
                _fsWatchers.Clear();

                foreach (var r in _regWatches)
                {
                    try { r.Dispose(); } catch { /* best-effort */ }
                }
                _regWatches.Clear();
            }
        }
        finally
        {
            _stopEvent.Dispose();
        }
    }

    private static IReadOnlyList<string> DefaultRoots()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToArray();
    }

    private static string BuildTraceId(DateTime startedUtc)
    {
        return startedUtc.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6];
    }

    private void StartFileSystemWatchers()
    {
        foreach (var root in _roots)
        {
            FileSystemWatcher watcher;
            try
            {
                watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.CreationTime,
                    InternalBufferSize = 65536, // 64 KB max
                };
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to create FileSystemWatcher for {Root}", root);
                continue;
            }

            watcher.Created += OnFsCreated;
            watcher.Changed += OnFsChanged;
            watcher.Error += OnFsError;
            try
            {
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to enable FileSystemWatcher for {Root}", root);
                watcher.Dispose();
                continue;
            }
            _fsWatchers.Add(watcher);
        }
    }

    private void OnFsCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Distinguish file vs directory by current state.
            var kind = Directory.Exists(e.FullPath)
                ? InstallTraceEventKind.DirectoryCreated
                : InstallTraceEventKind.FileCreated;
            RecordEvent(kind, e.FullPath);
        }
        catch (Exception ex)
        {
            _logger.Verbose(ex, "FS Created handler error: {Path}", e.FullPath);
        }
    }

    private void OnFsChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Modify events for directories are noisy and uninteresting — only report files.
            if (Directory.Exists(e.FullPath)) return;
            RecordEvent(InstallTraceEventKind.FileModified, e.FullPath);
        }
        catch (Exception ex)
        {
            _logger.Verbose(ex, "FS Changed handler error: {Path}", e.FullPath);
        }
    }

    private void OnFsError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        if (ex is InternalBufferOverflowException)
        {
            _logger.Warning(ex, "FileSystemWatcher buffer overflow — events may have been lost");
        }
        else
        {
            _logger.Warning(ex, "FileSystemWatcher error");
        }
    }

    private void StartRegistryWatchers()
    {
        // HKLM\SOFTWARE (64-bit view), HKLM\SOFTWARE\WOW6432Node (32-bit redirected view), HKCU\SOFTWARE
        TryAddRegistryWatch(Microsoft.Win32.RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE", @"HKLM\SOFTWARE");
        TryAddRegistryWatch(Microsoft.Win32.RegistryHive.LocalMachine, RegistryView.Registry32, "SOFTWARE\\WOW6432Node", @"HKLM\SOFTWARE\WOW6432Node");
        TryAddRegistryWatch(Microsoft.Win32.RegistryHive.CurrentUser, RegistryView.Default, "SOFTWARE", @"HKCU\SOFTWARE");
    }

    private void TryAddRegistryWatch(Microsoft.Win32.RegistryHive hive, RegistryView view, string subKey, string displayPath)
    {
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(hive, view);
            var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key is null)
            {
                _logger.Verbose("Registry key not found: {Path}", displayPath);
                baseKey.Dispose();
                return;
            }

            var winEvent = new ManualResetEventSlim(initialState: false);
            var watch = new RegistryWatch(baseKey, key, winEvent, displayPath);

            // Acquire native handle and arm the first notification.
            var sh = key.Handle;
            bool addedRef = false;
            try
            {
                sh.DangerousAddRef(ref addedRef);
                watch.MarkRefAcquired();
                ArmRegistryNotification(watch);
            }
            catch
            {
                if (addedRef) sh.DangerousRelease();
                watch.MarkRefReleased();
                watch.Dispose();
                throw;
            }

            watch.Task = Task.Run(() => RegistryWatchLoop(watch));
            _regWatches.Add(watch);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to set up registry watch for {Path}", displayPath);
        }
    }

    private void ArmRegistryNotification(RegistryWatch watch)
    {
        var handle = watch.Key.Handle.DangerousGetHandle();
        var rc = RegNotifyChangeKeyValue(
            handle,
            watchSubtree: true,
            REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET,
            watch.Event.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            asynchronous: true);
        if (rc != 0)
        {
            _logger.Verbose("RegNotifyChangeKeyValue rc={Rc} for {Path}", rc, watch.DisplayPath);
        }
    }

    private void RegistryWatchLoop(RegistryWatch watch)
    {
        try
        {
            var handles = new[] { watch.Event.WaitHandle, _stopEvent.WaitHandle };
            while (!_stopEvent.IsSet)
            {
                var idx = WaitHandle.WaitAny(handles, TimeSpan.FromMilliseconds(500));
                if (idx == 1) break;       // stop signaled
                if (idx == WaitHandle.WaitTimeout) continue;

                // Registry change observed. Record at the parent path.
                RecordEvent(InstallTraceEventKind.RegistryValueWritten, watch.DisplayPath);
                watch.Event.Reset();
                if (_stopEvent.IsSet) break;

                try
                {
                    ArmRegistryNotification(watch);
                }
                catch (Exception ex)
                {
                    _logger.Verbose(ex, "Failed to re-arm registry watch for {Path}", watch.DisplayPath);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Registry watch loop crashed for {Path}", watch.DisplayPath);
        }
    }

    private void RecordEvent(InstallTraceEventKind kind, string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        // Filter against excluded substrings (case-insensitive).
        foreach (var sub in _excludedPathSubstrings)
        {
            if (path.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
        }

        var key = (kind, path);
        lock (_gate)
        {
            if (!IsTracking) return;
            if (_seen.Add(key))
            {
                _events.Add(new InstallTraceEvent(kind, path, DateTime.UtcNow));
            }
        }
    }

    private sealed class RegistryWatch : IDisposable
    {
        public RegistryKey BaseKey { get; }
        public RegistryKey Key { get; }
        public ManualResetEventSlim Event { get; }
        public string DisplayPath { get; }
        public Task Task { get; set; } = Task.CompletedTask;

        private bool _refAcquired;
        private bool _disposed;

        public RegistryWatch(RegistryKey baseKey, RegistryKey key, ManualResetEventSlim winEvent, string displayPath)
        {
            BaseKey = baseKey;
            Key = key;
            Event = winEvent;
            DisplayPath = displayPath;
        }

        public void MarkRefAcquired() => _refAcquired = true;
        public void MarkRefReleased() => _refAcquired = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_refAcquired)
                {
                    try { Key.Handle.DangerousRelease(); } catch { /* best-effort */ }
                    _refAcquired = false;
                }
                Key.Dispose();
                BaseKey.Dispose();
                Event.Dispose();
            }
            catch
            {
                // best-effort
            }
        }
    }

    private sealed class EventKeyComparer : IEqualityComparer<(InstallTraceEventKind Kind, string Path)>
    {
        public static readonly EventKeyComparer Instance = new();

        public bool Equals((InstallTraceEventKind Kind, string Path) x, (InstallTraceEventKind Kind, string Path) y)
        {
            return x.Kind == y.Kind && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((InstallTraceEventKind Kind, string Path) obj)
        {
            return HashCode.Combine((int)obj.Kind, obj.Path?.ToLowerInvariant());
        }
    }
}
