namespace DiskScout.Models;

public enum FileSystemNodeKind
{
    Directory,
    File,
    Volume,
    ReparsePoint,
}

public sealed record FileSystemNode(
    long Id,
    long? ParentId,
    string Name,
    string FullPath,
    FileSystemNodeKind Kind,
    long SizeBytes,
    int FileCount,
    int DirectoryCount,
    DateTime LastModifiedUtc,
    bool IsReparsePoint,
    int Depth,
    long LogicalSizeBytes = 0,
    bool IsCloudPlaceholder = false);
