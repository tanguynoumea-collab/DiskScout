namespace DiskScout.Services;

/// <summary>
/// Resolves a real-world folder name (and optional registry-supplied publisher /
/// display-name pair) to a canonical publisher or product label, bridging the
/// naming entropy between filesystem folders and the installed-programs registry
/// without weakening the underlying fuzzy threshold.
///
/// Stage [5] of the AppData orphan-detection pipeline. Combines an embedded
/// alias catalog (Resources/PathRules/aliases.json — exact + token-expansion
/// matching) with a fallback to <see cref="DiskScout.Helpers.FuzzyMatcher"/>
/// for the original folder/publisher/displayName triple. Returns <c>null</c>
/// when the maximum score is below threshold (default 0.7).
/// </summary>
public interface IPublisherAliasResolver
{
    /// <summary>
    /// Resolve <paramref name="folderName"/> to a canonical publisher / product
    /// using three strategies and taking the maximum score:
    /// <list type="number">
    ///   <item>Exact alias-table match (case-insensitive) on the folder name → score 1.0.</item>
    ///   <item>Token alias expansion — replace folder tokens with canonical
    ///   tokens before invoking <see cref="DiskScout.Helpers.FuzzyMatcher.ComputeMatch"/>.</item>
    ///   <item>Direct fallback to <see cref="DiskScout.Helpers.FuzzyMatcher.ComputeMatch"/>
    ///   with the original folder + publisher/displayName (no alias substitution).</item>
    /// </list>
    /// Returns <c>null</c> if max score &lt; 0.7.
    /// </summary>
    /// <param name="folderName">Filesystem folder leaf to resolve (e.g., "BcfManager", "RVT 2025", "Adobe AIR").</param>
    /// <param name="publisher">Optional registry Publisher (e.g., "Autodesk, Inc."). Used by the fuzzy fallback.</param>
    /// <param name="displayName">Optional registry DisplayName (e.g., "BCF Managers 6.5"). Used by the fuzzy fallback.</param>
    /// <param name="cancellationToken">Cancellation token honored before each strategy stage.</param>
    /// <returns>
    /// A tuple <c>(Score, MatchedCanonical)</c> when a match clears the 0.7
    /// threshold; <c>null</c> otherwise. <c>MatchedCanonical</c> is the canonical
    /// string from the alias catalog when the alias path won, or
    /// <see cref="displayName"/>/<see cref="publisher"/> when the fuzzy fallback won.
    /// </returns>
    Task<(double Score, string? MatchedCanonical)?> ResolveAsync(
        string folderName,
        string? publisher,
        string? displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent eager-load of the embedded alias catalog. Calling this is
    /// optional — <see cref="ResolveAsync"/> will auto-load on first use.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);
}
