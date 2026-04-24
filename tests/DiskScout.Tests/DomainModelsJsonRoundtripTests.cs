using System.Text.Json;
using DiskScout.Helpers;
using DiskScout.Models;
using FluentAssertions;

namespace DiskScout.Tests;

public class DomainModelsJsonRoundtripTests
{
    private readonly JsonSerializerOptions _options = new(DiskScoutJsonContext.Default.Options);

    [Fact]
    public void ScanResult_RoundTripsThroughSourceGenSerializer()
    {
        var original = new ScanResult(
            SchemaVersion: ScanResult.CurrentSchemaVersion,
            ScanId: "abc123",
            StartedUtc: new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc),
            CompletedUtc: new DateTime(2026, 4, 24, 12, 5, 0, DateTimeKind.Utc),
            ScannedDrives: new[] { "C:\\", "D:\\" },
            Nodes: new[]
            {
                new FileSystemNode(1, null, "C:", "C:\\", FileSystemNodeKind.Volume, 1_000_000L, 100, 10, DateTime.UtcNow, false, 0),
                new FileSystemNode(2, 1, "Windows", "C:\\Windows", FileSystemNodeKind.Directory, 500_000L, 50, 5, DateTime.UtcNow, false, 1),
            },
            Programs: new[]
            {
                new InstalledProgram("Key", RegistryHive.LocalMachine64, "Test Program", "Acme", "1.0", null, "C:\\Program Files\\Test", null, 0, 42L),
            },
            Orphans: new[]
            {
                new OrphanCandidate(2, "C:\\OrphanDir", 123_456L, OrphanCategory.AppDataOrphan, "No matching installed program", 0.42),
            });

        var json = JsonSerializer.Serialize(original, DiskScoutJsonContext.Default.ScanResult);
        var roundtripped = JsonSerializer.Deserialize(json, DiskScoutJsonContext.Default.ScanResult);

        roundtripped.Should().NotBeNull();
        roundtripped!.SchemaVersion.Should().Be(ScanResult.CurrentSchemaVersion);
        roundtripped.ScanId.Should().Be("abc123");
        roundtripped.ScannedDrives.Should().BeEquivalentTo(original.ScannedDrives);
        roundtripped.Nodes.Should().HaveCount(2);
        roundtripped.Programs.Should().HaveCount(1);
        roundtripped.Orphans.Should().HaveCount(1);
        roundtripped.Orphans[0].Category.Should().Be(OrphanCategory.AppDataOrphan);
    }

    [Fact]
    public void ScanProgress_IsValueTypeAndRoundTrips()
    {
        var progress = new ScanProgress(
            FilesProcessed: 1000,
            BytesScanned: 1_000_000,
            CurrentPath: "C:\\temp\\scan.txt",
            PercentComplete: 0.42,
            Phase: ScanPhase.ScanningFilesystem);

        typeof(ScanProgress).IsValueType.Should().BeTrue();

        var json = JsonSerializer.Serialize(progress, DiskScoutJsonContext.Default.ScanProgress);
        var roundtripped = JsonSerializer.Deserialize(json, DiskScoutJsonContext.Default.ScanProgress);

        roundtripped.FilesProcessed.Should().Be(1000);
        roundtripped.Phase.Should().Be(ScanPhase.ScanningFilesystem);
    }

    [Fact]
    public void DeltaResult_RoundTripsAllChangeTypes()
    {
        var delta = new DeltaResult(
            BeforeScanId: "before",
            AfterScanId: "after",
            BeforeCompletedUtc: DateTime.UtcNow.AddDays(-1),
            AfterCompletedUtc: DateTime.UtcNow,
            Entries: new[]
            {
                new DeltaEntry("C:\\a", DeltaChange.Added, null, 100, 100, null),
                new DeltaEntry("C:\\b", DeltaChange.Removed, 200, null, -200, null),
                new DeltaEntry("C:\\c", DeltaChange.Grew, 100, 300, 200, 2.0),
                new DeltaEntry("C:\\d", DeltaChange.Shrank, 500, 100, -400, -0.8),
            });

        var json = JsonSerializer.Serialize(delta, DiskScoutJsonContext.Default.DeltaResult);
        var roundtripped = JsonSerializer.Deserialize(json, DiskScoutJsonContext.Default.DeltaResult);

        roundtripped.Should().NotBeNull();
        roundtripped!.Entries.Should().HaveCount(4);
        roundtripped.Entries.Select(e => e.Change).Should().BeEquivalentTo(new[]
        {
            DeltaChange.Added, DeltaChange.Removed, DeltaChange.Grew, DeltaChange.Shrank,
        });
    }

    [Fact]
    public void FileSystemNode_IsRecordWithValueEquality()
    {
        var a = new FileSystemNode(1, null, "X", "C:\\X", FileSystemNodeKind.Directory, 0, 0, 0, DateTime.MinValue, false, 0);
        var b = new FileSystemNode(1, null, "X", "C:\\X", FileSystemNodeKind.Directory, 0, 0, 0, DateTime.MinValue, false, 0);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }
}
