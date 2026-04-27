using System.IO;
using System.Reflection;
using System.Text.Json;
using DiskScout.Helpers;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// JSON path-rule engine. Loads embedded path rules from the assembly + user
/// rules from <c>%LocalAppData%\DiskScout\path-rules\*.json</c>; user rules
/// with the same <see cref="PathRule.Id"/> override embedded ones
/// (last-write-wins). Mirrors <see cref="PublisherRuleEngine"/> verbatim
/// except embedded JSONs deserialize as <c>PathRule[]</c> arrays (not single
/// objects) — this lets one file ship many rules of the same category.
/// Malformed JSON files are logged at Warning and skipped — the engine never
/// throws on bad input.
/// </summary>
public sealed class PathRuleEngine : IPathRuleEngine
{
    private const string EmbeddedResourcePrefix = "DiskScout.Resources.PathRules.";
    private const string UserRuleSearchPattern = "*.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger;
    private readonly string _userRulesFolder;
    private readonly Assembly _resourceAssembly;
    private List<PathRule> _allRules = new();

    /// <summary>Production constructor: uses <see cref="AppPaths.PathRulesFolder"/> for user rules
    /// and the engine's own assembly for embedded resources.</summary>
    public PathRuleEngine(ILogger logger)
        : this(logger, AppPaths.PathRulesFolder, typeof(PathRuleEngine).Assembly) { }

    /// <summary>Test seam: lets xUnit fixtures point the engine at a temp folder + arbitrary assembly.</summary>
    internal PathRuleEngine(ILogger logger, string userRulesFolder, Assembly resourceAssembly)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userRulesFolder = userRulesFolder ?? throw new ArgumentNullException(nameof(userRulesFolder));
        _resourceAssembly = resourceAssembly ?? throw new ArgumentNullException(nameof(resourceAssembly));
    }

    public IReadOnlyList<PathRule> AllRules => _allRules;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Dictionary keyed by Id so user rules can override embedded ones (last-write-wins).
        var byId = new Dictionary<string, PathRule>(StringComparer.OrdinalIgnoreCase);
        int embeddedCount = 0;
        int userCount = 0;

        // Step A: embedded rules (JSON ARRAYS, one file ships many rules of one category).
        foreach (var resourceName in _resourceAssembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(EmbeddedResourcePrefix, StringComparison.Ordinal))
                continue;
            if (!resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var stream = _resourceAssembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    _logger.Warning("Path rule resource {Name} returned null stream", resourceName);
                    continue;
                }

                var rules = await JsonSerializer
                    .DeserializeAsync<PathRule[]>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (rules is null)
                {
                    _logger.Warning("Embedded path rule {Name} produced null array", resourceName);
                    continue;
                }

                foreach (var rule in rules)
                {
                    if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.PathPattern))
                    {
                        _logger.Warning("Embedded path rule {Name} contains an entry with empty Id/PathPattern — skipped", resourceName);
                        continue;
                    }

                    byId[rule.Id] = rule;
                    embeddedCount++;
                }
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "Bad embedded path rule {Name} — skipped", resourceName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Unexpected error reading embedded rule {Name} — skipped", resourceName);
            }
        }

        // Step B: user rules from %LocalAppData%\DiskScout\path-rules\*.json.
        // User files may contain either a SINGLE PathRule object or an ARRAY — accept both.
        if (Directory.Exists(_userRulesFolder))
        {
            foreach (var path in Directory.EnumerateFiles(_userRulesFolder, UserRuleSearchPattern, SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    var trimmed = json.TrimStart();

                    if (trimmed.StartsWith('['))
                    {
                        var rules = JsonSerializer.Deserialize<PathRule[]>(json, JsonOptions);
                        if (rules is null)
                        {
                            _logger.Warning("User path rule file {Path} produced null array — skipped", path);
                            continue;
                        }
                        foreach (var rule in rules)
                        {
                            if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.PathPattern))
                            {
                                _logger.Warning("User path rule {Path} contains an entry with empty Id/PathPattern — skipped", path);
                                continue;
                            }
                            byId[rule.Id] = rule;
                            userCount++;
                        }
                    }
                    else
                    {
                        var rule = JsonSerializer.Deserialize<PathRule>(json, JsonOptions);
                        if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.PathPattern))
                        {
                            _logger.Warning("User path rule {Path} produced null/empty rule — skipped", path);
                            continue;
                        }
                        byId[rule.Id] = rule;
                        userCount++;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.Warning(ex, "Bad user path rule {Path} — skipped", path);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Unexpected error reading user rule {Path} — skipped", path);
                }
            }
        }

        _allRules = byId.Values.ToList();
        _logger.Information(
            "Loaded {Total} path rules ({Embedded} embedded, {User} user)",
            _allRules.Count, embeddedCount, userCount);
    }

    public IReadOnlyList<RuleHit> Match(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return Array.Empty<RuleHit>();

        // hits with their expanded-pattern length so we can sort by specificity afterwards.
        var ranked = new List<(int Length, string Id, RuleHit Hit)>();

        foreach (var rule in _allRules)
        {
            if (string.IsNullOrEmpty(rule.PathPattern))
                continue;

            string expanded;
            try
            {
                expanded = Environment.ExpandEnvironmentVariables(rule.PathPattern);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to expand path pattern for rule {Id} — skipped", rule.Id);
                continue;
            }

            if (string.IsNullOrEmpty(expanded))
                continue;

            // If env-var expansion left an unresolved %...%, the host doesn't have that variable.
            // Skip silently (logged at Debug level for diagnostics).
            if (expanded.Contains('%'))
            {
                _logger.Debug("Path rule {Id} has unresolved env var in {Pattern} — skipped", rule.Id, expanded);
                continue;
            }

            if (fullPath.StartsWith(expanded, StringComparison.OrdinalIgnoreCase))
            {
                var reason = string.IsNullOrEmpty(rule.Reason) ? rule.Id : rule.Reason;
                ranked.Add((expanded.Length, rule.Id, new RuleHit(rule.Id, rule.Category, reason)));
            }
        }

        // Sort by specificity descending (longer expanded pattern = more specific),
        // tie-break by RuleId ascending for deterministic ordering.
        ranked.Sort((a, b) =>
        {
            int cmp = b.Length.CompareTo(a.Length);
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a.Id, b.Id);
        });

        var result = new List<RuleHit>(ranked.Count);
        foreach (var entry in ranked)
            result.Add(entry.Hit);
        return result;
    }
}
