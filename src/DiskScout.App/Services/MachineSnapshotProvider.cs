using System.Diagnostics;
using System.IO;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Production <see cref="IMachineSnapshotProvider"/>: lazy build, 5-minute TTL by
/// default, parallel population of the four enumerator sources via
/// <see cref="Task.WhenAll(Task[])"/>, thread-safe via a single
/// <see cref="SemaphoreSlim"/> gate, graceful per-enumerator degradation
/// (one source throwing falls back to an empty list rather than failing the
/// whole snapshot).
/// </summary>
/// <remarks>
/// <para>
/// Cache + concurrency contract:
/// </para>
/// <list type="number">
/// <item>Fast path: read of the cached snapshot field (without taking the gate). If
///   <c>UtcNow - CapturedUtc &lt; TTL</c>, return immediately.</item>
/// <item>Slow path: <c>await _gate.WaitAsync(ct)</c>. Re-check freshness (another
///   caller may have built it). Else launch four <c>Task.Run</c> blocks (one per
///   enumerator), <c>Task.WhenAll</c>, build the snapshot + indexes, assign to the
///   field, release the gate.</item>
/// <item><c>Invalidate</c> takes the gate, nulls the cache field, releases.</item>
/// </list>
/// <para>
/// CancellationToken: each enumerator <c>Task.Run</c> checks <c>ct.ThrowIfCancellationRequested</c>
/// once at entry. The underlying enumerators are synchronous IO and cannot be
/// cancelled mid-stream — this is an accepted limitation for Phase 10.
/// </para>
/// </remarks>
public sealed class MachineSnapshotProvider : IMachineSnapshotProvider
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly ILogger _logger;
    private readonly IServiceEnumerator _services;
    private readonly IDriverEnumerator _drivers;
    private readonly IAppxEnumerator _appx;
    private readonly IScheduledTaskEnumerator _tasks;
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private MachineSnapshot? _cached;

    public MachineSnapshotProvider(
        ILogger logger,
        IServiceEnumerator services,
        IDriverEnumerator drivers,
        IAppxEnumerator appx,
        IScheduledTaskEnumerator tasks,
        TimeSpan? ttl = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _drivers = drivers ?? throw new ArgumentNullException(nameof(drivers));
        _appx = appx ?? throw new ArgumentNullException(nameof(appx));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _ttl = ttl ?? DefaultTtl;
    }

    public async Task<MachineSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: cache hit, no gate needed.
        var existing = Volatile.Read(ref _cached);
        if (existing is not null && DateTime.UtcNow - existing.CapturedUtc < _ttl)
        {
            return existing;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after taking the gate (another caller may have just built it).
            existing = _cached;
            if (existing is not null && DateTime.UtcNow - existing.CapturedUtc < _ttl)
            {
                return existing;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();

            // Run all four enumerators in parallel. Each Task.Run wraps the
            // synchronous enumerator + materializes the result. A per-task
            // try/catch substitutes an empty list on failure so the snapshot
            // always succeeds even if one source is broken (graceful degradation).
            var serviceTask = Task.Run(() => SafeMaterializeServices(cancellationToken), cancellationToken);
            var driverTask = Task.Run(() => SafeMaterializeDrivers(cancellationToken), cancellationToken);
            var appxTask = Task.Run(() => SafeMaterializeAppx(cancellationToken), cancellationToken);
            var taskTask = Task.Run(() => SafeMaterializeTasks(cancellationToken), cancellationToken);

            await Task.WhenAll(serviceTask, driverTask, appxTask, taskTask).ConfigureAwait(false);

            var serviceList = await serviceTask.ConfigureAwait(false);
            var driverList = await driverTask.ConfigureAwait(false);
            var appxList = await appxTask.ConfigureAwait(false);
            var taskList = await taskTask.ConfigureAwait(false);

            var snapshot = new MachineSnapshot(
                CapturedUtc: DateTime.UtcNow,
                Services: serviceList,
                Drivers: driverList,
                AppxPackages: appxList,
                ScheduledTasks: taskList)
            {
                ServiceBinaryPathPrefixes = BuildServiceBinaryPathPrefixes(serviceList),
                DriverProviderTokens = BuildDriverProviderTokens(driverList),
                AppxInstallLocationPrefixes = BuildAppxInstallLocationPrefixes(appxList),
            };

            sw.Stop();
            _logger.Information(
                "MachineSnapshot built in {ElapsedMs}ms — {ServiceCount} services, {DriverCount} drivers, {AppxCount} appx, {TaskCount} tasks",
                sw.ElapsedMilliseconds, serviceList.Count, driverList.Count, appxList.Count, taskList.Count);

            Volatile.Write(ref _cached, snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        // Take the gate so we don't null out a snapshot mid-build by another caller.
        _gate.Wait();
        try
        {
            _cached = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- Per-source materialization with graceful degradation ----------

    private IReadOnlyList<ServiceEntry> SafeMaterializeServices(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = new List<ServiceEntry>(256);
            foreach (var (name, displayName, binaryPath) in _services.EnumerateServices())
            {
                result.Add(new ServiceEntry(name, displayName, binaryPath));
            }
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Warning(ex, "MachineSnapshot: IServiceEnumerator threw — substituting empty list");
            return Array.Empty<ServiceEntry>();
        }
    }

    private IReadOnlyList<DriverEntry> SafeMaterializeDrivers(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = new List<DriverEntry>(128);
            foreach (var (publishedName, originalFileName, provider, className) in _drivers.EnumerateDrivers())
            {
                result.Add(new DriverEntry(publishedName, originalFileName, provider, className));
            }
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Warning(ex, "MachineSnapshot: IDriverEnumerator threw — substituting empty list");
            return Array.Empty<DriverEntry>();
        }
    }

    private IReadOnlyList<AppxEntry> SafeMaterializeAppx(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = new List<AppxEntry>(128);
            foreach (var (full, family, publisher, location) in _appx.EnumerateAppxPackages())
            {
                result.Add(new AppxEntry(full, family, publisher, location));
            }
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Warning(ex, "MachineSnapshot: IAppxEnumerator threw — substituting empty list");
            return Array.Empty<AppxEntry>();
        }
    }

    private IReadOnlyList<ScheduledTaskEntry> SafeMaterializeTasks(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = new List<ScheduledTaskEntry>(128);
            foreach (var (taskPath, author, actionPath) in _tasks.EnumerateTasks())
            {
                result.Add(new ScheduledTaskEntry(taskPath, author, actionPath));
            }
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Warning(ex, "MachineSnapshot: IScheduledTaskEnumerator threw — substituting empty list");
            return Array.Empty<ScheduledTaskEntry>();
        }
    }

    // ---------- Index builders ----------

    private static IReadOnlySet<string> BuildServiceBinaryPathPrefixes(IReadOnlyList<ServiceEntry> services)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in services)
        {
            if (string.IsNullOrWhiteSpace(s.BinaryPath)) continue;
            string? dir;
            try
            {
                dir = Path.GetDirectoryName(s.BinaryPath);
            }
            catch
            {
                // Some ImagePath entries contain non-canonical characters that
                // break Path.GetDirectoryName — skip rather than fail the whole index.
                continue;
            }
            if (string.IsNullOrEmpty(dir)) continue;
            set.Add(dir);
        }
        return set;
    }

    private static IReadOnlySet<string> BuildDriverProviderTokens(IReadOnlyList<DriverEntry> drivers)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in drivers)
        {
            if (string.IsNullOrWhiteSpace(d.Provider)) continue;
            foreach (var token in d.Provider!.Split(
                new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length < 2) continue;
                set.Add(token);
            }
        }
        return set;
    }

    private static IReadOnlySet<string> BuildAppxInstallLocationPrefixes(IReadOnlyList<AppxEntry> appx)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in appx)
        {
            if (string.IsNullOrWhiteSpace(a.InstallLocation)) continue;
            set.Add(a.InstallLocation!);
        }
        return set;
    }
}
