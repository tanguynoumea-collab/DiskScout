namespace DiskScout.Models;

public sealed record ScanResult(
    int SchemaVersion,
    string ScanId,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    IReadOnlyList<string> ScannedDrives,
    IReadOnlyList<FileSystemNode> Nodes,
    IReadOnlyList<InstalledProgram> Programs,
    IReadOnlyList<OrphanCandidate> Orphans)
{
    public const int CurrentSchemaVersion = 1;

    public static ScanResult CreateEmpty() => new(
        SchemaVersion: CurrentSchemaVersion,
        ScanId: Guid.NewGuid().ToString("N"),
        StartedUtc: DateTime.UtcNow,
        CompletedUtc: DateTime.UtcNow,
        ScannedDrives: Array.Empty<string>(),
        Nodes: Array.Empty<FileSystemNode>(),
        Programs: Array.Empty<InstalledProgram>(),
        Orphans: Array.Empty<OrphanCandidate>());
}
