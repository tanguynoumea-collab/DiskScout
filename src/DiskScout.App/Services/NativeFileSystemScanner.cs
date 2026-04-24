using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DiskScout.Helpers;
using DiskScout.Helpers.Win32;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

public sealed class NativeFileSystemScanner : IFileSystemScanner
{
    private const int ProgressThrottleMs = 100;
    private const string LongPathPrefix = @"\\?\";

    private readonly ILogger _logger;
    private long _nextId;

    public NativeFileSystemScanner(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<FileSystemNode>> ScanAsync(
        IReadOnlyList<string> drives,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        if (drives.Count == 0) return Array.Empty<FileSystemNode>();

        var nodes = new ConcurrentBag<FileSystemNode>();
        var filesCount = 0L;
        var bytesScanned = 0L;
        var currentPath = string.Empty;
        var lastProgress = Environment.TickCount64;

        void ReportProgress(bool force = false, ScanPhase phase = ScanPhase.ScanningFilesystem)
        {
            var now = Environment.TickCount64;
            if (!force && now - lastProgress < ProgressThrottleMs) return;
            lastProgress = now;
            progress.Report(new ScanProgress(
                FilesProcessed: Interlocked.Read(ref filesCount),
                BytesScanned: Interlocked.Read(ref bytesScanned),
                CurrentPath: currentPath,
                PercentComplete: null,
                Phase: phase));
        }

        ReportProgress(force: true, phase: ScanPhase.EnumeratingDrives);

        var stopwatch = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            Parallel.ForEach(
                drives.Distinct(),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(drives.Count, Environment.ProcessorCount),
                    CancellationToken = cancellationToken,
                },
                drive =>
                {
                    var rootPath = NormalizeRootPath(drive);
                    var rootNode = CreateRootNode(rootPath);
                    nodes.Add(rootNode);

                    try
                    {
                        var aggregatedSize = ScanDirectory(
                            parentId: rootNode.Id,
                            parentPath: rootPath,
                            depth: 1,
                            nodes,
                            ref filesCount,
                            ref bytesScanned,
                            ref currentPath,
                            ReportProgress,
                            cancellationToken);

                        var updatedRoot = rootNode with { SizeBytes = aggregatedSize };
                        nodes.Add(updatedRoot);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Unexpected scan error at drive root {Drive}", rootPath);
                    }
                });
        }, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        _logger.Information(
            "FileSystem scan finished: {Files} files, {Bytes} bytes, {Elapsed:c}",
            Interlocked.Read(ref filesCount),
            Interlocked.Read(ref bytesScanned),
            stopwatch.Elapsed);

        ReportProgress(force: true, phase: ScanPhase.Completed);

        var unique = nodes
            .GroupBy(n => n.Id)
            .Select(g => g.OrderByDescending(n => n.SizeBytes).First())
            .OrderBy(n => n.Id)
            .ToArray();

        return unique;
    }

    private FileSystemNode CreateRootNode(string rootPath)
    {
        var id = Interlocked.Increment(ref _nextId);
        return new FileSystemNode(
            Id: id,
            ParentId: null,
            Name: rootPath.TrimEnd('\\'),
            FullPath: rootPath,
            Kind: FileSystemNodeKind.Volume,
            SizeBytes: 0,
            FileCount: 0,
            DirectoryCount: 0,
            LastModifiedUtc: DateTime.UtcNow,
            IsReparsePoint: false,
            Depth: 0);
    }

    private long ScanDirectory(
        long parentId,
        string parentPath,
        int depth,
        ConcurrentBag<FileSystemNode> nodes,
        ref long filesCount,
        ref long bytesScanned,
        ref string currentPath,
        Action<bool, ScanPhase> reportProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var searchPattern = BuildSearchPattern(parentPath);
        currentPath = parentPath;

        using var handle = Win32Native.FindFirstFileEx(
            searchPattern,
            Win32Native.FindExInfoBasic,
            out var findData,
            Win32Native.FindExSearchNameMatch,
            IntPtr.Zero,
            Win32Native.FIND_FIRST_EX_LARGE_FETCH);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is not (Win32Native.ERROR_FILE_NOT_FOUND
                or Win32Native.ERROR_PATH_NOT_FOUND
                or Win32Native.ERROR_ACCESS_DENIED))
            {
                _logger.Debug("FindFirstFileEx failed ({Error}) at {Path}", error, parentPath);
            }
            return 0;
        }

        long totalSize = 0;

        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var name = findData.cFileName;
                if (string.IsNullOrEmpty(name) || name == "." || name == "..") continue;

                var fullPath = CombinePath(parentPath, name);
                var isDir = findData.IsDirectory;
                var isReparse = findData.IsReparsePoint;
                var reparseTag = findData.dwReserved0;
                var isCloudReparse = isReparse && IsCloudReparseTag(reparseTag);
                var followReparse = isReparse && isCloudReparse;
                var isCloudPlaceholder = !isDir && findData.IsCloudPlaceholder;
                var logicalSize = isDir ? 0 : findData.FileSizeBytes;
                var physicalSize = isCloudPlaceholder ? 0 : logicalSize;
                var kind = DetermineKind(isDir, isReparse);
                var id = Interlocked.Increment(ref _nextId);

                if (isDir && (!isReparse || followReparse))
                {
                    long subtreeSize = 0;
                    int subFileCount = 0;
                    int subDirCount = 0;
                    try
                    {
                        subtreeSize = ScanDirectory(
                            parentId: id,
                            parentPath: fullPath,
                            depth: depth + 1,
                            nodes,
                            ref filesCount,
                            ref bytesScanned,
                            ref currentPath,
                            reportProgress,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Verbose(ex, "Sub-scan error at {Path}", fullPath);
                    }

                    var dirNode = new FileSystemNode(
                        Id: id,
                        ParentId: parentId,
                        Name: name,
                        FullPath: fullPath,
                        Kind: kind,
                        SizeBytes: subtreeSize,
                        FileCount: subFileCount,
                        DirectoryCount: subDirCount,
                        LastModifiedUtc: findData.LastWriteUtc,
                        IsReparsePoint: isReparse,
                        Depth: depth,
                        LogicalSizeBytes: subtreeSize,
                        IsCloudPlaceholder: false);
                    nodes.Add(dirNode);

                    totalSize += subtreeSize;
                }
                else
                {
                    var fileNode = new FileSystemNode(
                        Id: id,
                        ParentId: parentId,
                        Name: name,
                        FullPath: fullPath,
                        Kind: kind,
                        SizeBytes: physicalSize,
                        FileCount: 1,
                        DirectoryCount: 0,
                        LastModifiedUtc: findData.LastWriteUtc,
                        IsReparsePoint: isReparse,
                        Depth: depth,
                        LogicalSizeBytes: logicalSize,
                        IsCloudPlaceholder: isCloudPlaceholder);
                    nodes.Add(fileNode);

                    totalSize += physicalSize;
                    Interlocked.Increment(ref filesCount);
                    Interlocked.Add(ref bytesScanned, physicalSize);
                }

                reportProgress(false, ScanPhase.ScanningFilesystem);
            }
            while (Win32Native.FindNextFile(handle, out findData));

            var lastError = Marshal.GetLastWin32Error();
            if (lastError != 0 && lastError != Win32Native.ERROR_NO_MORE_FILES)
            {
                _logger.Debug("FindNextFile stopped with error {Error} at {Path}", lastError, parentPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return totalSize;
    }

    private static FileSystemNodeKind DetermineKind(bool isDirectory, bool isReparsePoint) =>
        isReparsePoint ? FileSystemNodeKind.ReparsePoint
        : isDirectory ? FileSystemNodeKind.Directory
        : FileSystemNodeKind.File;

    private static bool IsCloudReparseTag(uint tag)
    {
        // IO_REPARSE_TAG_CLOUD + all 16 sub-tags (0x9000001A to 0x9000F01A) are OneDrive / SharePoint placeholders.
        // These are storage, not loop-prone links — safe to follow.
        if (tag == Win32Native.IO_REPARSE_TAG_ONEDRIVE) return true;
        if ((tag & 0xFFFF00FFu) == 0x9000001Au) return true;
        if (tag == Win32Native.IO_REPARSE_TAG_APPEXECLINK) return false;
        return false;
    }

    private static string NormalizeRootPath(string drive)
    {
        var trimmed = drive.Trim();
        if (trimmed.Length == 2 && trimmed[1] == ':') trimmed += "\\";
        return trimmed.EndsWith('\\') ? trimmed : trimmed + "\\";
    }

    private static string BuildSearchPattern(string directory)
    {
        var withLongPrefix = EnsureLongPath(directory);
        return withLongPrefix.EndsWith('\\')
            ? withLongPrefix + "*"
            : withLongPrefix + "\\*";
    }

    private static string CombinePath(string parent, string child)
    {
        if (parent.EndsWith('\\')) return parent + child;
        return parent + "\\" + child;
    }

    private static string EnsureLongPath(string path)
    {
        if (path.StartsWith(LongPathPrefix, StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path[2..];
        return LongPathPrefix + path;
    }
}
