using System.Globalization;
using System.IO;
using DiskScout.Models;
using Microsoft.Win32;
using Serilog;
using DsRegistryHive = DiskScout.Models.RegistryHive;
using MsRegistryHive = Microsoft.Win32.RegistryHive;

namespace DiskScout.Services;

public sealed class RegistryInstalledProgramsScanner : IInstalledProgramsScanner
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly (DsRegistryHive Hive, MsRegistryHive MsHive, RegistryView View)[] HiveViews =
    {
        (DsRegistryHive.LocalMachine64, MsRegistryHive.LocalMachine, RegistryView.Registry64),
        (DsRegistryHive.LocalMachine32, MsRegistryHive.LocalMachine, RegistryView.Registry32),
        (DsRegistryHive.CurrentUser64,  MsRegistryHive.CurrentUser,  RegistryView.Registry64),
        (DsRegistryHive.CurrentUser32,  MsRegistryHive.CurrentUser,  RegistryView.Registry32),
    };

    private readonly ILogger _logger;

    public RegistryInstalledProgramsScanner(ILogger logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<InstalledProgram>> ScanAsync(
        IReadOnlyList<FileSystemNode> nodes,
        CancellationToken cancellationToken)
    {
        var results = new List<InstalledProgram>(256);
        var pathIndex = BuildPathIndex(nodes);

        foreach (var (hive, msHive, view) in HiveViews)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var root = RegistryKey.OpenBaseKey(msHive, view);
                using var uninstall = root.OpenSubKey(UninstallSubKey, writable: false);
                if (uninstall is null) continue;

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var entry = uninstall.OpenSubKey(subKeyName, writable: false);
                    if (entry is null) continue;

                    var displayName = entry.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    var isSystemComponent = entry.GetValue("SystemComponent") switch
                    {
                        int i => i != 0,
                        _ => false,
                    };
                    if (isSystemComponent) continue;

                    var program = new InstalledProgram(
                        RegistryKey: subKeyName,
                        Hive: hive,
                        DisplayName: displayName.Trim(),
                        Publisher: entry.GetValue("Publisher") as string,
                        Version: entry.GetValue("DisplayVersion") as string,
                        InstallDate: ParseInstallDate(entry.GetValue("InstallDate") as string),
                        InstallLocation: NormalizeInstallLocation(entry.GetValue("InstallLocation") as string),
                        UninstallString: entry.GetValue("UninstallString") as string,
                        RegistryEstimatedSizeBytes: ParseEstimatedSize(entry.GetValue("EstimatedSize")),
                        ComputedSizeBytes: 0);

                    var enriched = program with
                    {
                        ComputedSizeBytes = LookupComputedSize(program.InstallLocation, pathIndex),
                    };
                    results.Add(enriched);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Registry enumeration failed for {Hive}/{View}", hive, view);
            }
        }

        _logger.Information("Enumerated {Count} installed programs across 4 registry views", results.Count);
        return Task.FromResult<IReadOnlyList<InstalledProgram>>(results);
    }

    private static Dictionary<string, long> BuildPathIndex(IReadOnlyList<FileSystemNode> nodes)
    {
        var index = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (node.Kind is FileSystemNodeKind.Directory or FileSystemNodeKind.Volume)
            {
                var normalized = Normalize(node.FullPath);
                index[normalized] = node.SizeBytes;
            }
        }
        return index;
    }

    private static long LookupComputedSize(string? installLocation, Dictionary<string, long> index)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) return 0;
        var key = Normalize(installLocation);
        return index.TryGetValue(key, out var size) ? size : 0;
    }

    private static string Normalize(string path) =>
        path.TrimEnd('\\', '/').Replace('/', '\\');

    private static string? NormalizeInstallLocation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim().Trim('"');
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static long ParseEstimatedSize(object? value) => value switch
    {
        int i => Math.Max(0L, (long)i) * 1024L, // EstimatedSize is in KB
        long l => Math.Max(0L, l) * 1024L,
        _ => 0L,
    };

    private static DateOnly? ParseInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8) return null;
        if (DateOnly.TryParseExact(raw[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }
        return null;
    }
}
