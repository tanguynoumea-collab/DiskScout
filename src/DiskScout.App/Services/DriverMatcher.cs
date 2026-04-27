using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Default <see cref="IDriverMatcher"/>. Iterates the snapshot's
/// <see cref="MachineSnapshot.Drivers"/> list and emits one
/// <see cref="MatcherHit"/> per driver whose Provider contains the canonical
/// publisher, or whose OriginalFileName contains the candidate folderName.
/// Caps at 3 hits.
/// </summary>
public sealed class DriverMatcher : IDriverMatcher
{
    private const int MaxHits = 3;

    /// <summary>CONTEXT.md `<specifics>`: Driver présent : -45.</summary>
    private const int ScoreDelta = -45;

    private readonly ILogger _logger;

    public DriverMatcher(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<MatcherHit> Match(
        string folderName,
        string? canonicalPublisher,
        MachineSnapshot snapshot)
    {
        if (snapshot is null) return Array.Empty<MatcherHit>();
        if (string.IsNullOrWhiteSpace(folderName) && string.IsNullOrWhiteSpace(canonicalPublisher))
            return Array.Empty<MatcherHit>();

        var hits = new List<MatcherHit>(MaxHits);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in snapshot.Drivers)
        {
            if (hits.Count >= MaxHits) break;

            bool matched = false;

            if (!string.IsNullOrWhiteSpace(canonicalPublisher) &&
                !string.IsNullOrWhiteSpace(driver.Provider) &&
                driver.Provider!.Contains(canonicalPublisher, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }
            else if (!string.IsNullOrWhiteSpace(folderName) &&
                     !string.IsNullOrWhiteSpace(driver.OriginalFileName) &&
                     driver.OriginalFileName!.Contains(folderName, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }

            if (matched && seen.Add(driver.PublishedName))
            {
                hits.Add(new MatcherHit("Driver", $"Driver:{driver.PublishedName}", ScoreDelta));
            }
        }

        return hits;
    }
}
