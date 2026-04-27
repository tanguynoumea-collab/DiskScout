using System.IO;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Default <see cref="IServiceMatcher"/>. Iterates the snapshot's
/// <see cref="MachineSnapshot.Services"/> list and emits one
/// <see cref="MatcherHit"/> per service whose Name contains the candidate
/// folderName, whose DisplayName contains the canonical publisher, or whose
/// binary-path directory equals the candidate's parent directory.
/// Caps at 3 hits to avoid unbounded score collapse.
/// </summary>
public sealed class ServiceMatcher : IServiceMatcher
{
    private const int MaxHits = 3;

    // CONTEXT.md `<specifics>`:
    //   Service Windows actif (Running) : -60
    //   Service Windows arrêté         : -30
    // The current IServiceEnumerator (Plan 10-02) does not expose service state
    // (Running/Stopped). We use the average -45 until a future enrichment pass
    // adds the State column.
    // TODO: Phase 10.x distinguish Running vs Stopped (see CONTEXT.md `<specifics>`).
    private const int ScoreDelta = -45;

    private readonly ILogger _logger;

    public ServiceMatcher(ILogger logger)
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

        foreach (var service in snapshot.Services)
        {
            if (hits.Count >= MaxHits) break;

            bool matched = false;

            if (!string.IsNullOrWhiteSpace(folderName) &&
                !string.IsNullOrEmpty(service.Name) &&
                service.Name.Contains(folderName, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }
            else if (!string.IsNullOrWhiteSpace(canonicalPublisher) &&
                     !string.IsNullOrEmpty(service.DisplayName) &&
                     service.DisplayName.Contains(canonicalPublisher, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }
            else if (!string.IsNullOrWhiteSpace(parentDirectory) &&
                     !string.IsNullOrWhiteSpace(service.BinaryPath))
            {
                string? svcDir;
                try
                {
                    svcDir = Path.GetDirectoryName(service.BinaryPath);
                }
                catch
                {
                    svcDir = null;
                }
                if (!string.IsNullOrEmpty(svcDir) &&
                    svcDir.StartsWith(parentDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                }
            }

            if (matched && seen.Add(service.Name))
            {
                hits.Add(new MatcherHit("Service", $"Service:{service.Name}", ScoreDelta));
            }
        }

        return hits;
    }
}
