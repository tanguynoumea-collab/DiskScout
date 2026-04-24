using System.IO;
using Microsoft.VisualBasic.FileIO;
using Serilog;

namespace DiskScout.Services;

public sealed class FileDeletionService : IFileDeletionService
{
    private readonly ILogger _logger;

    public FileDeletionService(ILogger logger)
    {
        _logger = logger;
    }

    public Task<DeletionResult> DeleteAsync(
        IReadOnlyList<string> paths,
        bool sendToRecycleBin,
        CancellationToken cancellationToken = default)
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
                    entries.Add(new DeletionEntry(path ?? string.Empty, false, 0, "Chemin vide."));
                    continue;
                }

                try
                {
                    long bytesBefore = MeasureBytes(path);

                    if (Directory.Exists(path))
                    {
                        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, recycle, UICancelOption.DoNothing);
                    }
                    else if (File.Exists(path))
                    {
                        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, recycle, UICancelOption.DoNothing);
                    }
                    else
                    {
                        entries.Add(new DeletionEntry(path, false, 0, "Introuvable (déjà supprimé ?)"));
                        continue;
                    }

                    entries.Add(new DeletionEntry(path, true, bytesBefore, null));
                    _logger.Information("Deleted {Path} ({Bytes} bytes, recycle={Recycle})",
                        path, bytesBefore, sendToRecycleBin);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
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
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }
            if (Directory.Exists(path))
            {
                long total = 0;
                foreach (var file in Directory.EnumerateFiles(path, "*", System.IO.SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch { /* skip inaccessible */ }
                }
                return total;
            }
        }
        catch
        {
            // Best-effort sizing only — doesn't block deletion if it fails.
        }
        return 0;
    }
}
