using System.IO;
using DiskScout.Helpers;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

public sealed class OrphanDetectorService : IOrphanDetectorService
{
    private const double FuzzyThreshold = 0.7;
    private const long EmptyProgramFilesThresholdBytes = 1 * 1024 * 1024;
    private const int StaleTempDays = 30;

    private readonly ILogger _logger;

    public OrphanDetectorService(ILogger logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<OrphanCandidate>> DetectAsync(
        IReadOnlyList<FileSystemNode> nodes,
        IReadOnlyList<InstalledProgram> programs,
        CancellationToken cancellationToken)
    {
        var orphans = new List<OrphanCandidate>();

        var appDataRoots = GetAppDataRoots();
        var programFilesRoots = GetProgramFilesRoots();
        var tempRoots = GetTempRoots();
        var installerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Installer");

        var installedInstallLocations = programs
            .Select(p => p.InstallLocation)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l!.TrimEnd('\\', '/').ToUpperInvariant())
            .ToHashSet();

        var cutoffUtc = DateTime.UtcNow.AddDays(-StaleTempDays);

        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node.Kind is not (FileSystemNodeKind.Directory or FileSystemNodeKind.File)) continue;
            if (node.Depth == 0) continue;

            if (node.Kind == FileSystemNodeKind.Directory && IsUnderAny(node.FullPath, appDataRoots))
            {
                if (node.Depth > 4) continue;
                if (IsAppDataOrphanRoot(node, appDataRoots) && !MatchesAnyProgram(node, programs))
                {
                    orphans.Add(new OrphanCandidate(
                        node.Id,
                        node.FullPath,
                        node.SizeBytes,
                        OrphanCategory.AppDataOrphan,
                        $"Aucun programme installé ne correspond à '{node.Name}'.",
                        MatchScore: null));
                }
            }
            else if (node.Kind == FileSystemNodeKind.Directory && IsUnderAny(node.FullPath, programFilesRoots))
            {
                if (IsProgramFilesChildLeaf(node, programFilesRoots))
                {
                    if (node.SizeBytes < EmptyProgramFilesThresholdBytes &&
                        !installedInstallLocations.Contains(node.FullPath.TrimEnd('\\').ToUpperInvariant()))
                    {
                        orphans.Add(new OrphanCandidate(
                            node.Id,
                            node.FullPath,
                            node.SizeBytes,
                            OrphanCategory.EmptyProgramFiles,
                            $"Dossier Program Files quasi-vide (< {EmptyProgramFilesThresholdBytes / (1024 * 1024)} Mo).",
                            MatchScore: null));
                    }
                }
            }
            else if (node.Kind == FileSystemNodeKind.File && IsUnderAny(node.FullPath, tempRoots))
            {
                if (node.LastModifiedUtc < cutoffUtc && node.SizeBytes > 0)
                {
                    orphans.Add(new OrphanCandidate(
                        node.Id,
                        node.FullPath,
                        node.SizeBytes,
                        OrphanCategory.StaleTemp,
                        $"Non modifié depuis plus de {StaleTempDays} jours.",
                        MatchScore: null));
                }
            }
            else if (node.Kind == FileSystemNodeKind.File &&
                     node.FullPath.StartsWith(installerPath, StringComparison.OrdinalIgnoreCase) &&
                     node.FullPath.EndsWith(".msp", StringComparison.OrdinalIgnoreCase))
            {
                orphans.Add(new OrphanCandidate(
                    node.Id,
                    node.FullPath,
                    node.SizeBytes,
                    OrphanCategory.OrphanInstallerPatch,
                    "Patch MSI potentiellement orphelin dans Windows\\Installer.",
                    MatchScore: null));
            }
        }

        _logger.Information("Detected {Count} orphan candidates", orphans.Count);
        return Task.FromResult<IReadOnlyList<OrphanCandidate>>(orphans);
    }

    private static bool IsAppDataOrphanRoot(FileSystemNode node, IReadOnlyList<string> appDataRoots)
    {
        foreach (var root in appDataRoots)
        {
            var normalized = root.TrimEnd('\\');
            if (node.FullPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = node.FullPath.Substring(normalized.Length).TrimStart('\\');
                var depth = remainder.Count(c => c == '\\') + (remainder.Length > 0 ? 1 : 0);
                return depth is 1 or 2;
            }
        }
        return false;
    }

    private static bool MatchesAnyProgram(FileSystemNode node, IReadOnlyList<InstalledProgram> programs)
    {
        foreach (var program in programs)
        {
            if (!string.IsNullOrWhiteSpace(program.InstallLocation) &&
                node.FullPath.StartsWith(program.InstallLocation!, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (FuzzyMatcher.IsMatch(node.Name, program.Publisher, program.DisplayName, FuzzyThreshold))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsProgramFilesChildLeaf(FileSystemNode node, IReadOnlyList<string> programFilesRoots)
    {
        foreach (var root in programFilesRoots)
        {
            var normalized = root.TrimEnd('\\');
            if (node.FullPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = node.FullPath.Substring(normalized.Length).TrimStart('\\');
                return remainder.Length > 0 && !remainder.Contains('\\');
            }
        }
        return false;
    }

    private static bool IsUnderAny(string path, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static IReadOnlyList<string> GetAppDataRoots() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

    private static IReadOnlyList<string> GetProgramFilesRoots() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

    private static IReadOnlyList<string> GetTempRoots() =>
        new[]
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
        }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
