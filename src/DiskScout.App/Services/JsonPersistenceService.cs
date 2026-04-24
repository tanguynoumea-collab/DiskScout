using System.IO;
using System.Text.Json;
using DiskScout.Helpers;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

public sealed class JsonPersistenceService : IPersistenceService
{
    private readonly ILogger _logger;

    public JsonPersistenceService(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<string> SaveAsync(ScanResult result, CancellationToken cancellationToken)
    {
        var timestamp = result.CompletedUtc.ToLocalTime().ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(AppPaths.ScansFolder, $"scan_{timestamp}.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, result, DiskScoutJsonContext.Default.ScanResult, cancellationToken).ConfigureAwait(false);
        _logger.Information("Saved scan {Id} to {Path}", result.ScanId, path);
        return path;
    }

    public async Task<ScanResult?> LoadAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) return null;
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync(stream, DiskScoutJsonContext.Default.ScanResult, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ScanHistoryEntry>> ListAsync(CancellationToken cancellationToken)
    {
        var entries = new List<ScanHistoryEntry>();
        if (!Directory.Exists(AppPaths.ScansFolder)) return Task.FromResult<IReadOnlyList<ScanHistoryEntry>>(entries);

        foreach (var file in Directory.EnumerateFiles(AppPaths.ScansFolder, "scan_*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                using var stream = File.OpenRead(file);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                var scanId = root.TryGetProperty("scanId", out var idProp) ? idProp.GetString() ?? "" : "";
                var completed = root.TryGetProperty("completedUtc", out var cProp) ? cProp.GetDateTime() : info.LastWriteTimeUtc;
                var drives = new List<string>();
                if (root.TryGetProperty("scannedDrives", out var drvArr))
                {
                    foreach (var d in drvArr.EnumerateArray()) drives.Add(d.GetString() ?? "");
                }
                entries.Add(new ScanHistoryEntry(file, scanId, completed, drives, info.Length));
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to summarise scan file {File}", file);
            }
        }
        return Task.FromResult<IReadOnlyList<ScanHistoryEntry>>(entries);
    }
}
