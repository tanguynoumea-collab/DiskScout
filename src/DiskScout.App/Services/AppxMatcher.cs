using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Default <see cref="IAppxMatcher"/>. Iterates the snapshot's
/// <see cref="MachineSnapshot.AppxPackages"/> list and emits one
/// <see cref="MatcherHit"/> per package whose InstallLocation is a prefix of
/// the candidate's parent directory, whose PackageFamilyName contains the
/// folder name, or whose Publisher contains the canonical publisher. Caps
/// at 3 hits.
/// </summary>
public sealed class AppxMatcher : IAppxMatcher
{
    private const int MaxHits = 3;

    /// <summary>CONTEXT.md `<specifics>`: Appx package installed : -50.</summary>
    private const int ScoreDelta = -50;

    private readonly ILogger _logger;

    public AppxMatcher(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<MatcherHit> Match(
        string folderName,
        string? canonicalPublisher,
        string? parentDirectory,
        MachineSnapshot snapshot)
    {
        if (snapshot is null) return Array.Empty<MatcherHit>();
        if (string.IsNullOrWhiteSpace(folderName) && string.IsNullOrWhiteSpace(canonicalPublisher) && string.IsNullOrWhiteSpace(parentDirectory))
            return Array.Empty<MatcherHit>();

        var hits = new List<MatcherHit>(MaxHits);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pkg in snapshot.AppxPackages)
        {
            if (hits.Count >= MaxHits) break;

            bool matched = false;

            if (!string.IsNullOrWhiteSpace(parentDirectory) &&
                !string.IsNullOrWhiteSpace(pkg.InstallLocation) &&
                parentDirectory.StartsWith(pkg.InstallLocation!, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }
            else if (!string.IsNullOrWhiteSpace(folderName) &&
                     !string.IsNullOrWhiteSpace(pkg.PackageFamilyName) &&
                     pkg.PackageFamilyName!.Contains(folderName, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }
            else if (!string.IsNullOrWhiteSpace(canonicalPublisher) &&
                     !string.IsNullOrWhiteSpace(pkg.Publisher) &&
                     pkg.Publisher!.Contains(canonicalPublisher, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }

            if (matched && seen.Add(pkg.PackageFullName))
            {
                hits.Add(new MatcherHit("Appx", $"Appx:{pkg.PackageFullName}", ScoreDelta));
            }
        }

        return hits;
    }
}
