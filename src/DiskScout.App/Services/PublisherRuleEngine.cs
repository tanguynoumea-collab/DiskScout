using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiskScout.Helpers;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// JSON rule engine. Loads embedded publisher rules from the assembly + user rules from
/// <c>%LocalAppData%\DiskScout\publisher-rules\*.json</c>; user rules with the same
/// <see cref="PublisherRule.Id"/> override embedded ones (last-write-wins).
/// Malformed JSON files and bad regexes are logged at Warning and skipped — the engine
/// never throws on bad input.
/// </summary>
public sealed class PublisherRuleEngine : IPublisherRuleEngine
{
    private const string EmbeddedResourcePrefix = "DiskScout.Resources.PublisherRules.";
    private const string UserRuleSearchPattern = "*.json";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger;
    private readonly string _userRulesFolder;
    private readonly Assembly _resourceAssembly;
    private List<PublisherRule> _allRules = new();

    /// <summary>Production constructor: uses <see cref="AppPaths.PublisherRulesFolder"/> for user rules
    /// and the engine's own assembly for embedded resources.</summary>
    public PublisherRuleEngine(ILogger logger)
        : this(logger, AppPaths.PublisherRulesFolder, typeof(PublisherRuleEngine).Assembly) { }

    /// <summary>Test seam: lets xUnit fixtures point the engine at a temp folder + arbitrary assembly.</summary>
    internal PublisherRuleEngine(ILogger logger, string userRulesFolder, Assembly resourceAssembly)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userRulesFolder = userRulesFolder ?? throw new ArgumentNullException(nameof(userRulesFolder));
        _resourceAssembly = resourceAssembly ?? throw new ArgumentNullException(nameof(resourceAssembly));
    }

    public IReadOnlyList<PublisherRule> AllRules => _allRules;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Dictionary keyed by Id so user rules can override embedded ones (last-write-wins).
        var byId = new Dictionary<string, PublisherRule>(StringComparer.OrdinalIgnoreCase);
        int embeddedCount = 0;
        int userCount = 0;

        // Step A: embedded rules.
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
                    _logger.Warning("Publisher rule resource {Name} returned null stream", resourceName);
                    continue;
                }

                var rule = await JsonSerializer
                    .DeserializeAsync<PublisherRule>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (rule is null || string.IsNullOrWhiteSpace(rule.Id))
                {
                    _logger.Warning("Embedded publisher rule {Name} produced null/empty rule", resourceName);
                    continue;
                }

                byId[rule.Id] = NormalizeRule(rule);
                embeddedCount++;
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "Bad embedded publisher rule {Name} — skipped", resourceName);
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

        // Step B: user rules from %LocalAppData%\DiskScout\publisher-rules\*.json.
        if (Directory.Exists(_userRulesFolder))
        {
            foreach (var path in Directory.EnumerateFiles(_userRulesFolder, UserRuleSearchPattern, SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await using var stream = File.OpenRead(path);
                    var rule = await JsonSerializer
                        .DeserializeAsync<PublisherRule>(stream, JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (rule is null || string.IsNullOrWhiteSpace(rule.Id))
                    {
                        _logger.Warning("User publisher rule {Path} produced null/empty rule — skipped", path);
                        continue;
                    }

                    byId[rule.Id] = NormalizeRule(rule);
                    userCount++;
                }
                catch (JsonException ex)
                {
                    _logger.Warning(ex, "Bad user publisher rule {Path} — skipped", path);
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
            "Loaded {Total} publisher rules ({Embedded} embedded, {User} user)",
            _allRules.Count, embeddedCount, userCount);
    }

    public IReadOnlyList<PublisherRuleMatch> Match(string? publisher, string displayName)
    {
        var safeDisplayName = displayName ?? string.Empty;
        var matches = new List<PublisherRuleMatch>();

        foreach (var rule in _allRules)
        {
            if (string.IsNullOrEmpty(rule.PublisherPattern))
                continue;

            bool publisherMatch;
            try
            {
                publisherMatch = !string.IsNullOrEmpty(publisher)
                    && Regex.IsMatch(publisher, rule.PublisherPattern, RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.Warning(ex, "Regex timeout on PublisherPattern for rule {Id} — skipped", rule.Id);
                continue;
            }
            catch (ArgumentException ex)
            {
                _logger.Warning(ex, "Invalid PublisherPattern regex for rule {Id} — skipped", rule.Id);
                continue;
            }

            if (!publisherMatch)
                continue;

            bool displayMatch;
            if (string.IsNullOrEmpty(rule.DisplayNamePattern))
            {
                displayMatch = true;
            }
            else
            {
                try
                {
                    displayMatch = Regex.IsMatch(safeDisplayName, rule.DisplayNamePattern, RegexOptions.IgnoreCase, RegexTimeout);
                }
                catch (RegexMatchTimeoutException ex)
                {
                    _logger.Warning(ex, "Regex timeout on DisplayNamePattern for rule {Id} — skipped", rule.Id);
                    continue;
                }
                catch (ArgumentException ex)
                {
                    _logger.Warning(ex, "Invalid DisplayNamePattern regex for rule {Id} — skipped", rule.Id);
                    continue;
                }
            }

            if (!displayMatch)
                continue;

            // Specificity: base 10 for any publisher match;
            // +100 when the rule narrows further by DisplayName regex (much more specific).
            int score = 10;
            if (!string.IsNullOrEmpty(rule.DisplayNamePattern))
                score += 100;

            matches.Add(new PublisherRuleMatch(rule, score));
        }

        matches.Sort((a, b) => b.SpecificityScore.CompareTo(a.SpecificityScore));
        return matches;
    }

    public string ExpandTokens(string template, string? publisher, string displayName)
    {
        if (string.IsNullOrEmpty(template))
            return template ?? string.Empty;

        // 1) Environment variables (%LocalAppData%, %ProgramFiles(x86)%, %UserProfile%, %Temp%, %WinDir%, ...).
        var expanded = Environment.ExpandEnvironmentVariables(template);

        // 2) Literal token substitution. Skip when value missing — leave token as-is so
        //    consumers can detect/ignore unfilled templates.
        if (!string.IsNullOrEmpty(publisher))
            expanded = expanded.Replace("{Publisher}", publisher, StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(displayName))
            expanded = expanded.Replace("{DisplayName}", displayName, StringComparison.Ordinal);

        // 3) Diagnostic: an unresolved %...% means the env var is unknown on this host.
        //    Log Debug (not Warning) — this is expected on systems that simply don't have
        //    the variable set; the consumer (ResidueScanner) will skip non-existent paths.
        if (expanded.Contains('%'))
            _logger.Debug("ExpandTokens left an unresolved %...% in result: {Result}", expanded);

        return expanded;
    }

    /// <summary>Defensive: deserializer leaves arrays null when JSON omits them.
    /// Normalize to empty arrays so downstream callers never need null checks.</summary>
    private static PublisherRule NormalizeRule(PublisherRule rule) => rule with
    {
        FilesystemPaths = rule.FilesystemPaths ?? Array.Empty<string>(),
        RegistryPaths = rule.RegistryPaths ?? Array.Empty<string>(),
        Services = rule.Services ?? Array.Empty<string>(),
        ScheduledTasks = rule.ScheduledTasks ?? Array.Empty<string>(),
    };
}
