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
    private const long MinSystemArtifactBytes = 50 * 1024 * 1024; // ignore tiny artefacts
    private const long MinDevCacheBytes = 50 * 1024 * 1024;

    private static readonly string[] DevCacheDirNames =
    {
        "node_modules",
        "packages", // .nuget
        "__pycache__",
        ".pytest_cache",
        ".mypy_cache",
        ".gradle",
        ".cargo",
        "venv",
        ".venv",
        "target", // Rust / Maven (checked with sibling heuristic below)
        ".tox",
        ".next",
        ".turbo",
        "dist",
        "build",
    };

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
        var devCaches = new HashSet<string>(DevCacheDirNames, StringComparer.OrdinalIgnoreCase);
        var seenDevCacheAncestors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node.Kind is not (FileSystemNodeKind.Directory or FileSystemNodeKind.File)) continue;
            if (node.Depth == 0) continue;

            // System artefacts (check BEFORE other heuristics to avoid double-tagging)
            if (TryClassifySystemArtifact(node, out var artifactReason))
            {
                orphans.Add(new OrphanCandidate(
                    node.Id, node.FullPath, node.SizeBytes,
                    OrphanCategory.SystemArtifact, artifactReason, MatchScore: null));
                continue;
            }

            // Dev caches — suppress nested matches once we've flagged an ancestor
            if (node.Kind == FileSystemNodeKind.Directory
                && devCaches.Contains(node.Name)
                && node.SizeBytes >= MinDevCacheBytes
                && !HasFlaggedAncestor(node.FullPath, seenDevCacheAncestors))
            {
                orphans.Add(new OrphanCandidate(
                    node.Id, node.FullPath, node.SizeBytes,
                    OrphanCategory.DevCache,
                    $"Cache de développement '{node.Name}' ({FormatBytes(node.SizeBytes)}).",
                    MatchScore: null));
                seenDevCacheAncestors.Add(node.FullPath.TrimEnd('\\') + "\\");
                continue;
            }

            if (node.Kind == FileSystemNodeKind.Directory && IsUnderAny(node.FullPath, appDataRoots))
            {
                if (node.Depth > 4) continue;
                if (IsAppDataOrphanRoot(node, appDataRoots) && !MatchesAnyProgram(node, programs))
                {
                    orphans.Add(new OrphanCandidate(
                        node.Id, node.FullPath, node.SizeBytes,
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
                            node.Id, node.FullPath, node.SizeBytes,
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
                        node.Id, node.FullPath, node.SizeBytes,
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
                    node.Id, node.FullPath, node.SizeBytes,
                    OrphanCategory.OrphanInstallerPatch,
                    "Patch MSI potentiellement orphelin dans Windows\\Installer.",
                    MatchScore: null));
            }
        }

        _logger.Information("Detected {Count} orphan candidates", orphans.Count);
        return Task.FromResult<IReadOnlyList<OrphanCandidate>>(orphans);
    }

    private static bool TryClassifySystemArtifact(FileSystemNode node, out string reason)
    {
        reason = string.Empty;
        if (node.SizeBytes < MinSystemArtifactBytes && node.Kind == FileSystemNodeKind.File) { /* allow small? no, require min */ }

        var path = node.FullPath;
        var name = node.Name;

        // Top-level system files at drive root
        if (node.Kind == FileSystemNodeKind.File && node.Depth == 1)
        {
            if (name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Fichier d'hibernation Windows ({FormatBytes(node.SizeBytes)}). Désactivable via 'powercfg /hibernate off'.";
                return node.SizeBytes >= MinSystemArtifactBytes;
            }
            if (name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Fichier d'échange Windows ({FormatBytes(node.SizeBytes)}). Taille configurable dans Paramètres système avancés.";
                return node.SizeBytes >= MinSystemArtifactBytes;
            }
            if (name.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Swapfile Windows ({FormatBytes(node.SizeBytes)}).";
                return node.SizeBytes >= MinSystemArtifactBytes;
            }
        }

        if (node.Kind != FileSystemNodeKind.Directory) return false;

        // Windows.old (leftover from major Windows upgrade)
        if (name.Equals("Windows.old", StringComparison.OrdinalIgnoreCase) && node.Depth == 1)
        {
            reason = $"Sauvegarde d'ancienne installation Windows ({FormatBytes(node.SizeBytes)}). Nettoyable via l'outil Nettoyage de disque.";
            return node.SizeBytes >= MinSystemArtifactBytes;
        }

        // Recycle Bin (per drive)
        if (name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) && node.Depth == 1)
        {
            reason = $"Corbeille Windows ({FormatBytes(node.SizeBytes)}). Vider via clic droit sur la corbeille.";
            return node.SizeBytes >= MinSystemArtifactBytes;
        }

        // Windows Update download cache
        if (path.EndsWith(@"\Windows\SoftwareDistribution\Download", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Cache Windows Update ({FormatBytes(node.SizeBytes)}). Arrêter le service wuauserv et nettoyer.";
            return node.SizeBytes >= MinSystemArtifactBytes;
        }

        // Prefetch
        if (path.EndsWith(@"\Windows\Prefetch", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Cache Prefetch Windows ({FormatBytes(node.SizeBytes)}).";
            return node.SizeBytes >= MinSystemArtifactBytes;
        }

        // CrashDumps
        if (name.Equals("CrashDumps", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Minidump", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LiveKernelReports", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Dumps de plantage ({FormatBytes(node.SizeBytes)}). Supprimables après analyse.";
            return node.SizeBytes >= MinSystemArtifactBytes;
        }

        // Delivery Optimization cache
        if (path.EndsWith(@"\Windows\SoftwareDistribution\DeliveryOptimization\Cache", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Cache Delivery Optimization ({FormatBytes(node.SizeBytes)}).";
            return node.SizeBytes >= MinSystemArtifactBytes;
        }

        return false;
    }

    private static bool HasFlaggedAncestor(string path, HashSet<string> ancestors)
    {
        foreach (var ancestor in ancestors)
        {
            if (path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {u[i]}";
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
