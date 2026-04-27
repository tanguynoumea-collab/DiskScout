using DiskScout.Helpers;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Default <see cref="IRegistryMatcher"/>. Iterates the installed-programs
/// list and emits one <see cref="MatcherHit"/> per program whose
/// InstallLocation is a prefix of the candidate path, whose
/// Publisher/DisplayName fuzzy-matches the folder name (via
/// <see cref="FuzzyMatcher.IsMatch"/> at threshold 0.7), or whose Publisher
/// contains the canonical publisher resolved upstream by Plan 10-03's alias
/// resolver. Caps at 3 hits.
/// </summary>
public sealed class RegistryMatcher : IRegistryMatcher
{
    private const int MaxHits = 3;
    private const double FuzzyThreshold = 0.7;

    /// <summary>CONTEXT.md `<specifics>`: Registry matcher : -50.</summary>
    private const int ScoreDelta = -50;

    private readonly ILogger _logger;

    public RegistryMatcher(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<MatcherHit> Match(
        string folderName,
        string? canonicalPublisher,
        string candidateFullPath,
        IReadOnlyList<InstalledProgram> programs)
    {
        if (programs is null || programs.Count == 0) return Array.Empty<MatcherHit>();
        if (string.IsNullOrWhiteSpace(folderName) && string.IsNullOrWhiteSpace(canonicalPublisher))
            return Array.Empty<MatcherHit>();

        var hits = new List<MatcherHit>(MaxHits);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var program in programs)
        {
            if (hits.Count >= MaxHits) break;

            bool matched = false;

            if (!string.IsNullOrWhiteSpace(program.InstallLocation) &&
                !string.IsNullOrWhiteSpace(candidateFullPath) &&
                candidateFullPath.StartsWith(program.InstallLocation!, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }
            else if (!string.IsNullOrWhiteSpace(folderName) &&
                     FuzzyMatcher.IsMatch(folderName, program.Publisher, program.DisplayName, FuzzyThreshold))
            {
                matched = true;
            }
            else if (!string.IsNullOrWhiteSpace(canonicalPublisher) &&
                     !string.IsNullOrWhiteSpace(program.Publisher) &&
                     program.Publisher!.Contains(canonicalPublisher, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }

            if (matched && seen.Add(program.RegistryKey))
            {
                hits.Add(new MatcherHit(
                    "Registry",
                    $"Registry:{program.DisplayName}",
                    ScoreDelta));
            }
        }

        return hits;
    }
}
