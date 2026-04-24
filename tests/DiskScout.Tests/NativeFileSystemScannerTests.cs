using System.IO;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

public class NativeFileSystemScannerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ILogger _logger;

    public NativeFileSystemScannerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DiskScoutTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task Scanner_EnumeratesFilesAndAggregatesSizes()
    {
        var subdir = Path.Combine(_tempRoot, "sub");
        Directory.CreateDirectory(subdir);
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot, "a.bin"), new byte[1024]);
        await File.WriteAllBytesAsync(Path.Combine(subdir, "b.bin"), new byte[2048]);

        var scanner = new NativeFileSystemScanner(_logger);
        var progress = new Progress<ScanProgress>();
        var nodes = await scanner.ScanAsync(new[] { _tempRoot }, progress, CancellationToken.None);

        nodes.Should().NotBeEmpty();
        nodes.Should().Contain(n => n.Name == "a.bin" && n.SizeBytes == 1024);
        nodes.Should().Contain(n => n.Name == "b.bin" && n.SizeBytes == 2048);
        nodes.Should().Contain(n => n.Name == "sub" && n.Kind == FileSystemNodeKind.Directory && n.SizeBytes == 2048);
    }

    [Fact]
    public async Task Scanner_HandlesMissingPathGracefullyAndReturnsOnlyRoot()
    {
        var bogus = Path.Combine(_tempRoot, "does-not-exist");
        var scanner = new NativeFileSystemScanner(_logger);
        var progress = new Progress<ScanProgress>();

        var nodes = await scanner.ScanAsync(new[] { bogus }, progress, CancellationToken.None);

        nodes.Should().HaveCount(1);
        nodes[0].Kind.Should().Be(FileSystemNodeKind.Volume);
    }

    [Fact]
    public async Task Scanner_CancelledTokenThrowsImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scanner = new NativeFileSystemScanner(_logger);
        var progress = new Progress<ScanProgress>();

        Func<Task> act = () => scanner.ScanAsync(new[] { _tempRoot }, progress, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Scanner_RejectsEmptyDriveList()
    {
        var scanner = new NativeFileSystemScanner(_logger);
        var progress = new Progress<ScanProgress>();
        var nodes = await scanner.ScanAsync(Array.Empty<string>(), progress, CancellationToken.None);
        nodes.Should().BeEmpty();
    }
}
