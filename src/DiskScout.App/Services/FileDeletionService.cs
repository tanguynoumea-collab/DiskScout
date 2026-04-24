using System.IO;
using DiskScout.Helpers;
using Microsoft.VisualBasic.FileIO;
using Serilog;

namespace DiskScout.Services;

public sealed class FileDeletionService : IFileDeletionService
{
    private readonly ILogger _logger;
    private readonly IQuarantineService _quarantine;

    public FileDeletionService(ILogger logger, IQuarantineService quarantine)
    {
        _logger = logger;
        _quarantine = quarantine;
    }

    public async Task<DeletionResult> DeleteAsync(
        IReadOnlyList<string> paths,
        DeleteMode mode,
        CancellationToken cancellationToken = default)
    {
        switch (mode)
        {
            case DeleteMode.DiskScoutQuarantine:
            {
                var entries = await _quarantine.MoveToQuarantineAsync(paths, cancellationToken);
                return new DeletionResult(entries
                    .Select(q => new DeletionEntry(q.OriginalPath, Success: true, q.BytesFreed, Error: null))
                    .ToList());
            }

            case DeleteMode.RecycleBin:
            case DeleteMode.Permanent:
            default:
                return await DeleteViaShellAsync(paths, mode == DeleteMode.RecycleBin, cancellationToken);
        }
    }

    private Task<DeletionResult> DeleteViaShellAsync(
        IReadOnlyList<string> paths,
        bool sendToRecycleBin,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var entries = new List<DeletionEntry>(paths.Count);
            var recycle = sendToRecycleBin ? RecycleOption.SendToRecycleBin : RecycleOption.DeletePermanently;

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path))
                {
                    entries.Add(new DeletionEntry(path ?? "", false, 0, "Chemin vide."));
                    continue;
                }

                try
                {
                    long bytesBefore = MeasureBytes(path);
                    if (Directory.Exists(path))
                        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, recycle, UICancelOption.DoNothing);
                    else if (File.Exists(path))
                        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, recycle, UICancelOption.DoNothing);
                    else
                    {
                        entries.Add(new DeletionEntry(path, false, 0, "Introuvable (déjà supprimé ?)"));
                        continue;
                    }
                    entries.Add(new DeletionEntry(path, true, bytesBefore, null));
                    _logger.Information("Deleted {Path} ({Bytes} bytes, recycle={Recycle})", path, bytesBefore, sendToRecycleBin);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    entries.Add(new DeletionEntry(path, false, 0, ex.Message));
                    _logger.Warning(ex, "Failed to delete {Path} (recycle={Recycle})", path, sendToRecycleBin);
                }
            }
            return new DeletionResult(entries);
        }, cancellationToken);
    }

    private static long MeasureBytes(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (Directory.Exists(path))
            {
                long total = 0;
                foreach (var f in Directory.EnumerateFiles(path, "*", System.IO.SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                return total;
            }
        }
        catch { }
        return 0;
    }
}
