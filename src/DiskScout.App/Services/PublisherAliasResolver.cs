using System.Reflection;
using System.Text;
using System.Text.Json;
using DiskScout.Helpers;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Default <see cref="IPublisherAliasResolver"/> — loads the embedded
/// <c>DiskScout.Resources.PathRules.aliases.json</c> catalog (a
/// <see cref="PublisherAlias"/> array) and exposes a 3-stage resolver with
/// <see cref="FuzzyMatcher"/> fallback.
///
/// Stages (max-score wins, threshold 0.7, returns null below):
/// <list type="number">
///   <item>Exact alias match (case-insensitive) on the folder name → 1.0.</item>
///   <item>Token alias expansion — substitute folder tokens that appear as
///   alias entries with their canonical, then fuzzy-match against the canonical.</item>
///   <item>Direct <see cref="FuzzyMatcher.ComputeMatch"/> fallback on the
///   original folder + publisher/displayName triple — guarantees no regression
///   vs the pre-Plan-10 detector.</item>
/// </list>
/// Idempotent <see cref="LoadAsync"/> with <see cref="SemaphoreSlim"/> gate;
/// auto-loads on first <see cref="ResolveAsync"/> call. Bad / missing /
/// wrong-shape JSON is logged at Warning and absorbed (resolver still works
/// via the FuzzyMatcher fallback path with an empty alias dictionary).
/// </summary>
public sealed class PublisherAliasResolver : IPublisherAliasResolver
{
    private const string EmbeddedResourceName = "DiskScout.Resources.PathRules.aliases.json";

    /// <summary>Default 0.7 threshold (matches the existing FuzzyMatcher.IsMatch default).</summary>
    public const double DefaultThreshold = 0.7;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger;
    private readonly Assembly _resourceAssembly;
    private readonly double _threshold;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Canonical-token-set keyed by the lower-cased canonical string. Used for
    // stage 2 (token expansion).
    private Dictionary<string, HashSet<string>> _canonicalTokens =
        new(StringComparer.OrdinalIgnoreCase);

    // Alias-token reverse lookup: lower-cased alias token → canonical. A
    // single alias can appear in only one canonical (last-write-wins).
    private Dictionary<string, string> _aliasTokenToCanonical =
        new(StringComparer.OrdinalIgnoreCase);

    // Whole-string alias lookup: full alias string → canonical. Used for
    // stage 1 (exact match).
    private Dictionary<string, string> _aliasFullToCanonical =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _loaded;

    /// <summary>Production constructor — uses the engine's own assembly + default 0.7 threshold.</summary>
    public PublisherAliasResolver(ILogger logger)
        : this(logger, typeof(PublisherAliasResolver).Assembly, DefaultThreshold) { }

    /// <summary>Test seam — lets fixtures point at an arbitrary assembly + threshold.</summary>
    internal PublisherAliasResolver(ILogger logger, Assembly resourceAssembly, double threshold = DefaultThreshold)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceAssembly = resourceAssembly ?? throw new ArgumentNullException(nameof(resourceAssembly));
        if (threshold <= 0 || threshold > 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Must be in (0, 1].");
        _threshold = threshold;
    }

    /// <summary>Total number of canonical entries loaded (for diagnostics + tests).</summary>
    public int CanonicalCount => _canonicalTokens.Count;

    /// <inheritdoc/>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded) return;

            var aliasFull = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var aliasToken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var canonicalTokens = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var stream = _resourceAssembly.GetManifestResourceStream(EmbeddedResourceName);
                if (stream is null)
                {
                    _logger.Warning(
                        "PublisherAliasResolver: embedded resource {Name} not found — resolver falls back to FuzzyMatcher only",
                        EmbeddedResourceName);
                }
                else
                {
                    var entries = await JsonSerializer
                        .DeserializeAsync<PublisherAlias[]>(stream, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (entries is null)
                    {
                        _logger.Warning("PublisherAliasResolver: embedded {Name} produced null array", EmbeddedResourceName);
                    }
                    else
                    {
                        foreach (var entry in entries)
                        {
                            if (entry is null || string.IsNullOrWhiteSpace(entry.Canonical))
                                continue;

                            // Always include the canonical itself as an alias-of-itself.
                            aliasFull[entry.Canonical] = entry.Canonical;

                            var canonTokens = Tokenize(entry.Canonical);
                            canonicalTokens[entry.Canonical] = canonTokens;

                            if (entry.Aliases is null) continue;

                            foreach (var alias in entry.Aliases)
                            {
                                if (string.IsNullOrWhiteSpace(alias)) continue;
                                aliasFull[alias] = entry.Canonical;

                                // Map each lower-cased alias TOKEN back to the canonical
                                // so a multi-token folder name can be expanded.
                                foreach (var token in Tokenize(alias))
                                {
                                    aliasToken[token] = entry.Canonical;
                                }
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "PublisherAliasResolver: bad embedded {Name} — alias dict empty, fuzzy-only fallback active", EmbeddedResourceName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "PublisherAliasResolver: unexpected error loading {Name} — alias dict empty", EmbeddedResourceName);
            }

            _aliasFullToCanonical = aliasFull;
            _aliasTokenToCanonical = aliasToken;
            _canonicalTokens = canonicalTokens;
            _loaded = true;

            _logger.Information(
                "PublisherAliasResolver loaded {Canonical} canonical entries / {Aliases} alias mappings",
                _canonicalTokens.Count, _aliasFullToCanonical.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<(double Score, string? MatchedCanonical)?> ResolveAsync(
        string folderName,
        string? publisher,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return null;

        if (!_loaded)
            await LoadAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        double bestScore = 0;
        string? bestCanonical = null;

        // Stage 1: Exact alias match (case-insensitive whole-string).
        if (_aliasFullToCanonical.TryGetValue(folderName, out var exact))
        {
            bestScore = 1.0;
            bestCanonical = exact;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Stage 2: Token alias expansion. Build the folder's expanded token
        // set by rewriting any alias token to its canonical tokens. For each
        // canonical referenced, score the rewritten folder against the
        // canonical via Jaccard on the expanded token set. The presence of a
        // recognized alias token is itself strong evidence — we floor the
        // score at 0.85 when at least one alias token hit AND the canonical
        // tokens are fully covered by the rewritten folder (subset match).
        var folderTokens = Tokenize(folderName);
        var aliasHitsByCanonical = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in folderTokens)
        {
            if (!_aliasTokenToCanonical.TryGetValue(token, out var canonical)) continue;
            if (!aliasHitsByCanonical.TryGetValue(canonical, out var hitTokens))
            {
                hitTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                aliasHitsByCanonical[canonical] = hitTokens;
            }
            hitTokens.Add(token);
        }

        foreach (var (canonical, _) in aliasHitsByCanonical)
        {
            // Build rewritten tokens: every alias token becomes the canonical's tokens.
            var rewritten = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in folderTokens)
            {
                if (_aliasTokenToCanonical.TryGetValue(t, out var canonForToken)
                    && string.Equals(canonForToken, canonical, StringComparison.OrdinalIgnoreCase)
                    && _canonicalTokens.TryGetValue(canonical, out var canonTokens))
                {
                    foreach (var ct in canonTokens) rewritten.Add(ct);
                }
                else
                {
                    rewritten.Add(t);
                }
            }

            // Subset check: if every token of the canonical now appears in the
            // rewritten folder, the alias path strongly identifies the canonical.
            // Score floored at 0.85 (above the 0.7 threshold) to surface this
            // signal cleanly. Otherwise fall through to a Jaccard ratio.
            double aliasScore = 0;
            if (_canonicalTokens.TryGetValue(canonical, out var canTokens) && canTokens.Count > 0)
            {
                var coversCanonical = canTokens.All(t => rewritten.Contains(t));
                if (coversCanonical)
                {
                    aliasScore = 0.85;
                }
                else
                {
                    int inter = canTokens.Count(t => rewritten.Contains(t));
                    int union = canTokens.Count + rewritten.Count - inter;
                    aliasScore = union == 0 ? 0 : (double)inter / union;
                }
            }

            if (aliasScore > bestScore)
            {
                bestScore = aliasScore;
                bestCanonical = canonical;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Stage 3: Direct FuzzyMatcher fallback on the original triple. This
        // is the no-regression guarantee — anything the legacy detector would
        // have caught is still caught here.
        if (!string.IsNullOrWhiteSpace(publisher) || !string.IsNullOrWhiteSpace(displayName))
        {
            var fallbackScore = FuzzyMatcher.ComputeMatch(folderName, publisher, displayName);
            if (fallbackScore > bestScore)
            {
                bestScore = fallbackScore;
                // The fallback didn't go through the alias dict — surface the
                // displayName (preferred, more specific) or publisher.
                bestCanonical = !string.IsNullOrWhiteSpace(displayName) ? displayName : publisher;
            }
        }

        if (bestScore < _threshold)
            return null;

        return (bestScore, bestCanonical);
    }

    /// <summary>
    /// Tokenize a string the same way FuzzyMatcher does (alphanumeric
    /// runs of length &gt;= 2, lower-cased) so token-set logic stays consistent
    /// across both layers.
    /// </summary>
    private static HashSet<string> Tokenize(string? input)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input)) return set;

        var buffer = new StringBuilder();
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer.Append(char.ToLowerInvariant(c));
            }
            else if (buffer.Length > 0)
            {
                if (buffer.Length >= 2) set.Add(buffer.ToString());
                buffer.Clear();
            }
        }
        if (buffer.Length >= 2) set.Add(buffer.ToString());
        return set;
    }
}
