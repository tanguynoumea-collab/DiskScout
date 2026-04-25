namespace DiskScout.Models;

/// <summary>
/// JSON-driven publisher residue rule. Each rule supplies four arrays of patterns
/// describing where a given publisher (matched via <see cref="PublisherPattern"/> regex,
/// optionally narrowed by <see cref="DisplayNamePattern"/>) leaves residues on disk,
/// in the registry, in services, and in scheduled tasks.
/// Patterns support `%ENVVAR%` expansion (via <c>Environment.ExpandEnvironmentVariables</c>)
/// and <c>{Publisher}</c> / <c>{DisplayName}</c> token substitution.
/// </summary>
public sealed record PublisherRule(
    string Id,
    string PublisherPattern,
    string? DisplayNamePattern,
    string[] FilesystemPaths,
    string[] RegistryPaths,
    string[] Services,
    string[] ScheduledTasks);

/// <summary>
/// Result of <c>IPublisherRuleEngine.Match</c>. Higher <see cref="SpecificityScore"/>
/// = more specific match (e.g., a rule with a non-null <see cref="PublisherRule.DisplayNamePattern"/>
/// that matched scores higher than a publisher-only rule).
/// </summary>
public sealed record PublisherRuleMatch(
    PublisherRule Rule,
    int SpecificityScore);
