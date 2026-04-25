using System.IO;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Microsoft.Win32;
using Serilog;

namespace DiskScout.Tests;

[Trait("Category", "Integration")]
public class InstallTrackerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ILogger _logger;
    private readonly FakeInstallTraceStore _store;

    public InstallTrackerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DiskScoutTrackerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
        _store = new FakeInstallTraceStore();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    private static async Task WaitForEventAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Tracker_CapturesFileCreatedEvent_WhenFileIsCreatedUnderTrackedRoot()
    {
        using var tracker = new InstallTracker(
            _logger,
            _store,
            roots: new[] { _tempRoot },
            excludedPathSubstrings: Array.Empty<string>());

        await tracker.StartAsync(installerCommandLine: "test.exe", installerProductHint: "TestProduct");

        var targetFile = Path.Combine(_tempRoot, "installed.dat");
        await File.WriteAllBytesAsync(targetFile, new byte[64]);

        // Wait briefly for FileSystemWatcher to flush.
        await WaitForEventAsync(() => _store.LastTrace?.Events.Any(e =>
            e.Kind == InstallTraceEventKind.FileCreated &&
            string.Equals(e.Path, targetFile, StringComparison.OrdinalIgnoreCase)) == true,
            TimeSpan.FromSeconds(1));

        // Trigger the watcher to flush by stopping.
        var trace = await tracker.StopAsync();

        trace.Events.Should().Contain(e =>
            e.Kind == InstallTraceEventKind.FileCreated &&
            string.Equals(e.Path, targetFile, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tracker_CapturesRegistryEvent_WhenValueIsWrittenUnderHkcuSoftware()
    {
        var subKeyName = "DiskScoutTest_" + Guid.NewGuid().ToString("N");
        var fullSubKeyPath = "Software\\" + subKeyName;

        using var tracker = new InstallTracker(
            _logger,
            _store,
            roots: new[] { _tempRoot },
            excludedPathSubstrings: Array.Empty<string>());

        try
        {
            await tracker.StartAsync(installerCommandLine: null, installerProductHint: null);

            // Brief delay to ensure RegNotifyChangeKeyValue is armed before we change the key.
            await Task.Delay(200);

            using (var k = Registry.CurrentUser.CreateSubKey(fullSubKeyPath, writable: true))
            {
                k!.SetValue("InstallPath", @"C:\TestProduct");
                k.SetValue("Version", "1.0.0");
            }

            // Wait up to 2s for the registry watcher loop to record the event.
            await WaitForEventAsync(
                () => _store.LastTrace?.Events.Any(e => e.Kind == InstallTraceEventKind.RegistryValueWritten) == true ||
                      HasPendingRegistryEvent(tracker),
                TimeSpan.FromSeconds(2));

            // Give the loop another beat to record before stopping (loop signals on event, then re-arms).
            await Task.Delay(300);

            var trace = await tracker.StopAsync();

            trace.Events.Should().Contain(e =>
                e.Kind == InstallTraceEventKind.RegistryValueWritten &&
                e.Path.Contains(@"HKCU\SOFTWARE", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(fullSubKeyPath, throwOnMissingSubKey: false); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static bool HasPendingRegistryEvent(InstallTracker tracker)
    {
        // Best-effort placeholder: we cannot peek inside the tracker, but we can use IsTracking as a sentinel.
        return !tracker.IsTracking;
    }

    [Fact]
    public async Task Tracker_FilteredPaths_ProduceNoEvents()
    {
        var excludedRoot = Path.Combine(_tempRoot, "excluded-zone");
        Directory.CreateDirectory(excludedRoot);

        using var tracker = new InstallTracker(
            _logger,
            _store,
            roots: new[] { _tempRoot },
            // Use the excluded folder name as a substring filter
            excludedPathSubstrings: new[] { "excluded-zone" });

        await tracker.StartAsync(null, null);

        var excludedFile = Path.Combine(excludedRoot, "ignored.tmp");
        await File.WriteAllBytesAsync(excludedFile, new byte[16]);

        await Task.Delay(300); // give watcher time

        var trace = await tracker.StopAsync();

        trace.Events.Should().NotContain(e =>
            string.Equals(e.Path, excludedFile, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tracker_StopAsync_WithCancelledToken_StillReleasesWatchers()
    {
        var tracker = new InstallTracker(
            _logger,
            _store,
            roots: new[] { _tempRoot },
            excludedPathSubstrings: Array.Empty<string>());

        await tracker.StartAsync(null, null);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // StopAsync may surface OperationCanceledException because the await passes the cancelled token to WaitAsync.
        try
        {
            await tracker.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected — the contract is "still releases watchers". We assert below.
        }

        tracker.IsTracking.Should().BeFalse();
        tracker.Dispose();
    }

    [Fact]
    public async Task Tracker_StartAsync_TwiceWithoutStop_Throws()
    {
        using var tracker = new InstallTracker(
            _logger,
            _store,
            roots: new[] { _tempRoot },
            excludedPathSubstrings: Array.Empty<string>());

        await tracker.StartAsync(null, null);

        Func<Task> act = () => tracker.StartAsync(null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await tracker.StopAsync();
    }

    [Fact]
    public async Task Tracker_DeduplicatesRepeatedEventsOnSamePathAndKind()
    {
        using var tracker = new InstallTracker(
            _logger,
            _store,
            roots: new[] { _tempRoot },
            excludedPathSubstrings: Array.Empty<string>());

        await tracker.StartAsync(null, null);

        var target = Path.Combine(_tempRoot, "duplicate-test.bin");
        await File.WriteAllBytesAsync(target, new byte[10]);

        // Modify the same file several times — at most one FileCreated and one FileModified should be recorded.
        for (int i = 0; i < 5; i++)
        {
            await File.WriteAllBytesAsync(target, new byte[10 + i]);
            await Task.Delay(20);
        }

        await Task.Delay(300);

        var trace = await tracker.StopAsync();

        var sameFileCreated = trace.Events.Count(e =>
            e.Kind == InstallTraceEventKind.FileCreated &&
            string.Equals(e.Path, target, StringComparison.OrdinalIgnoreCase));

        var sameFileModified = trace.Events.Count(e =>
            e.Kind == InstallTraceEventKind.FileModified &&
            string.Equals(e.Path, target, StringComparison.OrdinalIgnoreCase));

        sameFileCreated.Should().BeLessThanOrEqualTo(1);
        sameFileModified.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Tracker_PersistsTraceViaInjectedStore()
    {
        // This test uses a FakeInstallTraceStore (purpose-built fake instead of Moq —
        // the test project does not depend on Moq and the project policy forbids new NuGet packages).
        using var tracker = new InstallTracker(
            _logger,
            _store,
            roots: new[] { _tempRoot },
            excludedPathSubstrings: Array.Empty<string>());

        await tracker.StartAsync("setup.exe /S", "FakeProduct");
        var trace = await tracker.StopAsync();

        _store.SaveCallCount.Should().Be(1);
        _store.LastTrace.Should().NotBeNull();
        _store.LastTrace!.Header.TraceId.Should().Be(trace.Header.TraceId);
        _store.LastTrace.Header.InstallerCommandLine.Should().Be("setup.exe /S");
        _store.LastTrace.Header.TrackerVersion.Should().Be("1.0");
    }

    /// <summary>
    /// Test double for IInstallTraceStore — records the most recently saved trace.
    /// Used in lieu of Moq (project policy forbids adding new NuGet packages).
    /// </summary>
    private sealed class FakeInstallTraceStore : IInstallTraceStore
    {
        public int SaveCallCount { get; private set; }
        public InstallTrace? LastTrace { get; private set; }

        public Task SaveAsync(InstallTrace trace, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            LastTrace = trace;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InstallTraceHeader>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InstallTraceHeader>>(
                LastTrace is null ? Array.Empty<InstallTraceHeader>() : new[] { LastTrace.Header });

        public Task<InstallTrace?> LoadAsync(string traceId, CancellationToken cancellationToken = default)
            => Task.FromResult(LastTrace?.Header.TraceId == traceId ? LastTrace : null);

        public Task<bool> DeleteAsync(string traceId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
