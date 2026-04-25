using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

public sealed class JsonInstallTraceStore : IInstallTraceStore
{
    private const string FileNamePrefix = "trace_";
    private const string FileNameExtension = ".json";
    private const string TempExtension = ".tmp";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly Regex SafeIdRegex = new("[^A-Za-z0-9_-]", RegexOptions.Compiled);

    private readonly ILogger _logger;
    private readonly string _folder;

    public JsonInstallTraceStore(ILogger logger, string folder)
    {
        _logger = logger;
        _folder = folder;
        Directory.CreateDirectory(_folder);
    }

    public JsonInstallTraceStore(ILogger logger)
        : this(logger, DiskScout.Helpers.AppPaths.InstallTracesFolder) { }

    public Task SaveAsync(InstallTrace trace, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_folder);

            var safeId = Sanitize(trace.Header.TraceId);
            var finalPath = Path.Combine(_folder, FileNamePrefix + safeId + FileNameExtension);
            var tempPath = finalPath + TempExtension;

            try
            {
                var json = JsonSerializer.Serialize(trace, SerializerOptions);
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, finalPath, overwrite: true);
                _logger.Information("Saved install trace {TraceId} -> {Path} ({Events} events)",
                    trace.Header.TraceId, finalPath, trace.Events.Count);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to save install trace {TraceId}", trace.Header.TraceId);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
                throw;
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<InstallTraceHeader>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var headers = new List<InstallTraceHeader>();
            if (!Directory.Exists(_folder))
                return (IReadOnlyList<InstallTraceHeader>)headers;

            foreach (var file in Directory.EnumerateFiles(_folder, FileNamePrefix + "*" + FileNameExtension))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip *.json.tmp files (they end with .tmp not .json) — defensive guard.
                if (file.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var json = File.ReadAllText(file);
                    var trace = JsonSerializer.Deserialize<InstallTrace>(json, SerializerOptions);
                    if (trace is not null)
                    {
                        headers.Add(trace.Header);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Warning(ex, "Skipping corrupt install trace {File}", file);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to read install trace {File}", file);
                }
            }

            return (IReadOnlyList<InstallTraceHeader>)headers
                .OrderByDescending(h => h.StartedUtc)
                .ToList();
        }, cancellationToken);
    }

    public Task<InstallTrace?> LoadAsync(string traceId, CancellationToken cancellationToken = default)
    {
        return Task.Run<InstallTrace?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safeId = Sanitize(traceId);
            var path = Path.Combine(_folder, FileNamePrefix + safeId + FileNameExtension);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<InstallTrace>(json, SerializerOptions);
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "Corrupt install trace {TraceId} at {Path}", traceId, path);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load install trace {TraceId}", traceId);
                return null;
            }
        }, cancellationToken);
    }

    public Task<bool> DeleteAsync(string traceId, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safeId = Sanitize(traceId);
            var path = Path.Combine(_folder, FileNamePrefix + safeId + FileNameExtension);
            if (!File.Exists(path)) return false;

            try
            {
                File.Delete(path);
                _logger.Information("Deleted install trace {TraceId}", traceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete install trace {TraceId}", traceId);
                return false;
            }
        }, cancellationToken);
    }

    private static string Sanitize(string traceId)
    {
        if (string.IsNullOrWhiteSpace(traceId)) return "untitled";
        return SafeIdRegex.Replace(traceId, "_");
    }
}
