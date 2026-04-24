using System.IO;
using System.Text.Json;
using DiskScout.Helpers;
using Serilog;

namespace DiskScout.Services;

public sealed class QuarantineService : IQuarantineService
{
    private const string ManifestFileName = "manifest.json";
    private readonly ILogger _logger;

    public QuarantineService(ILogger logger)
    {
        _logger = logger;
    }

    private static string Root => Path.Combine(AppPaths.AppDataFolder, "quarantine");

    public Task<IReadOnlyList<QuarantineEntry>> MoveToQuarantineAsync(
        IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(Root);
            var sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6];
            var sessionDir = Path.Combine(Root, sessionId);
            Directory.CreateDirectory(sessionDir);

            var entries = new List<QuarantineEntry>();
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(path) && !Directory.Exists(path))
                    {
                        _logger.Warning("Quarantine skip (not found): {Path}", path);
                        continue;
                    }

                    // Preserve path structure under session dir (drive letter -> single folder)
                    var driveLetter = Path.GetPathRoot(path)?.TrimEnd(':', '\\', '/') ?? "root";
                    var relative = path.Substring(Path.GetPathRoot(path)?.Length ?? 0).TrimStart('\\', '/');
                    var destination = Path.Combine(sessionDir, driveLetter, relative);

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    long size = 0;
                    if (File.Exists(path))
                    {
                        size = new FileInfo(path).Length;
                        File.Move(path, destination, overwrite: false);
                    }
                    else if (Directory.Exists(path))
                    {
                        size = GetDirectorySize(path);
                        Directory.Move(path, destination);
                    }

                    var entry = new QuarantineEntry(path, destination, size, DateTime.UtcNow, sessionId);
                    entries.Add(entry);
                    _logger.Information("Quarantined {Path} -> {Dest} ({Bytes} bytes)", path, destination, size);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Quarantine failed for {Path}", path);
                }
            }

            if (entries.Count > 0)
            {
                WriteSessionManifest(sessionDir, entries);
            }

            return (IReadOnlyList<QuarantineEntry>)entries;
        }, cancellationToken);
    }

    public Task<QuarantineRestoreResult> RestoreAsync(
        IReadOnlyList<QuarantineEntry> entries, CancellationToken cancellationToken = default)
    {
        return Task.Run<QuarantineRestoreResult>(() =>
        {
            var restored = new List<string>();
            var failures = new List<(string, string)>();

            foreach (var e in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(e.QuarantinePath) && !Directory.Exists(e.QuarantinePath))
                    {
                        failures.Add((e.OriginalPath, "Fichier manquant en quarantaine (déjà purgé ?)"));
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(e.OriginalPath)!);

                    if (File.Exists(e.OriginalPath) || Directory.Exists(e.OriginalPath))
                    {
                        failures.Add((e.OriginalPath, "Un fichier existe déjà à l'emplacement d'origine."));
                        continue;
                    }

                    if (File.Exists(e.QuarantinePath))
                        File.Move(e.QuarantinePath, e.OriginalPath);
                    else
                        Directory.Move(e.QuarantinePath, e.OriginalPath);

                    restored.Add(e.OriginalPath);
                    _logger.Information("Restored {Path}", e.OriginalPath);
                }
                catch (Exception ex)
                {
                    failures.Add((e.OriginalPath, ex.Message));
                    _logger.Warning(ex, "Restore failed for {Path}", e.OriginalPath);
                }
            }

            // Prune manifest of restored entries
            if (restored.Count > 0)
            {
                var bySession = entries.GroupBy(e => e.SessionId);
                foreach (var group in bySession)
                {
                    var sessionDir = Path.Combine(Root, group.Key);
                    if (Directory.Exists(sessionDir)) RebuildManifest(sessionDir);
                }
            }

            return new QuarantineRestoreResult(restored, failures);
        }, cancellationToken);
    }

    public Task<long> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(Root)) return 0L;

            long freed = 0;
            var cutoff = DateTime.UtcNow - olderThan;

            foreach (var sessionDir in Directory.EnumerateDirectories(Root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var entries = ReadManifest(sessionDir) ?? new List<QuarantineEntry>();
                    if (entries.Count == 0 || entries[0].QuarantinedUtc > cutoff) continue;

                    var size = GetDirectorySize(sessionDir);
                    Directory.Delete(sessionDir, recursive: true);
                    freed += size;
                    _logger.Information("Purged quarantine session {Session} ({Bytes} bytes)",
                        Path.GetFileName(sessionDir), size);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Purge failed for {Session}", sessionDir);
                }
            }

            return freed;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<QuarantineEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var all = new List<QuarantineEntry>();
            if (!Directory.Exists(Root)) return (IReadOnlyList<QuarantineEntry>)all;

            foreach (var sessionDir in Directory.EnumerateDirectories(Root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entries = ReadManifest(sessionDir);
                if (entries is not null) all.AddRange(entries);
            }

            return (IReadOnlyList<QuarantineEntry>)all;
        }, cancellationToken);
    }

    private static void WriteSessionManifest(string sessionDir, IReadOnlyList<QuarantineEntry> entries)
    {
        var path = Path.Combine(sessionDir, ManifestFileName);
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static void RebuildManifest(string sessionDir)
    {
        var manifestPath = Path.Combine(sessionDir, ManifestFileName);
        var entries = ReadManifest(sessionDir) ?? new List<QuarantineEntry>();
        var alive = entries.Where(e => File.Exists(e.QuarantinePath) || Directory.Exists(e.QuarantinePath)).ToList();
        if (alive.Count == 0)
        {
            try { Directory.Delete(sessionDir, recursive: true); } catch { /* ignore */ }
            return;
        }
        var json = JsonSerializer.Serialize(alive, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
    }

    private static List<QuarantineEntry>? ReadManifest(string sessionDir)
    {
        var path = Path.Combine(sessionDir, ManifestFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<QuarantineEntry>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(f).Length; } catch { /* ignore */ }
        }
        return total;
    }
}
