using System.IO;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

public class InstallTraceStoreTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly ILogger _logger;
    private readonly JsonInstallTraceStore _store;

    public InstallTraceStoreTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "DiskScoutTraceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
        _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
        _store = new JsonInstallTraceStore(_logger, _tempFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempFolder, recursive: true); } catch { /* best-effort */ }
    }

    private static InstallTrace BuildTrace(string id, DateTime started)
    {
        var header = new InstallTraceHeader(
            TraceId: id,
            TrackerVersion: "1.0",
            StartedUtc: started,
            StoppedUtc: started.AddMinutes(2),
            InstallerCommandLine: "setup.exe /S",
            InstallerProductHint: "Test Product");

        var events = new List<InstallTraceEvent>
        {
            new(InstallTraceEventKind.FileCreated, @"C:\Program Files\Test\app.exe", started.AddSeconds(5)),
            new(InstallTraceEventKind.RegistryValueWritten, @"HKLM\SOFTWARE\Test", started.AddSeconds(10)),
        };

        return new InstallTrace(header, events);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllFields()
    {
        var trace = BuildTrace("trace-001", new DateTime(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc));

        await _store.SaveAsync(trace);

        // File written with expected name pattern
        Directory.GetFiles(_tempFolder, "trace_*.json").Should().HaveCount(1);

        var loaded = await _store.LoadAsync("trace-001");

        loaded.Should().NotBeNull();
        loaded!.Header.TraceId.Should().Be("trace-001");
        loaded.Header.TrackerVersion.Should().Be("1.0");
        loaded.Header.InstallerCommandLine.Should().Be("setup.exe /S");
        loaded.Events.Should().HaveCount(2);
        loaded.Events[0].Kind.Should().Be(InstallTraceEventKind.FileCreated);
        loaded.Events[1].Kind.Should().Be(InstallTraceEventKind.RegistryValueWritten);
    }

    [Fact]
    public async Task ListAsync_ReturnsHeadersSortedByStartedUtcDescending()
    {
        var older = BuildTrace("trace-old", new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc));
        var newer = BuildTrace("trace-new", new DateTime(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc));
        var middle = BuildTrace("trace-mid", new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc));

        await _store.SaveAsync(older);
        await _store.SaveAsync(newer);
        await _store.SaveAsync(middle);

        var headers = await _store.ListAsync();

        headers.Should().HaveCount(3);
        headers[0].TraceId.Should().Be("trace-new");
        headers[1].TraceId.Should().Be("trace-mid");
        headers[2].TraceId.Should().Be("trace-old");
    }

    [Fact]
    public async Task LoadAsync_ReturnsNullForMissingTrace()
    {
        var loaded = await _store.LoadAsync("does-not-exist");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileAndReturnsTrueThenFalse()
    {
        var trace = BuildTrace("trace-del", new DateTime(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc));
        await _store.SaveAsync(trace);

        var first = await _store.DeleteAsync("trace-del");
        var second = await _store.DeleteAsync("trace-del");

        first.Should().BeTrue();
        second.Should().BeFalse();
        Directory.GetFiles(_tempFolder, "trace_*.json").Should().BeEmpty();
    }

    [Fact]
    public async Task CorruptJson_IsSkippedByListAndReturnsNullFromLoad()
    {
        // Save one valid trace
        var good = BuildTrace("good", new DateTime(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc));
        await _store.SaveAsync(good);

        // Drop a corrupt file in the folder
        var corruptPath = Path.Combine(_tempFolder, "trace_corrupt.json");
        await File.WriteAllTextAsync(corruptPath, "{ not valid json !!!");

        var headers = await _store.ListAsync();
        var loadedCorrupt = await _store.LoadAsync("corrupt");

        // List does not throw, returns only the valid one.
        headers.Should().ContainSingle()
            .Which.TraceId.Should().Be("good");

        loadedCorrupt.Should().BeNull();
    }
}
