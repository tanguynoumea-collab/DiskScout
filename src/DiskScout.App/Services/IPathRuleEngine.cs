using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// Loads embedded path-rule JSONs (<c>DiskScout.Resources.PathRules.*</c>) plus
/// any user JSON files in <c>%LocalAppData%\DiskScout\path-rules</c>, then matches
/// candidate paths against the resulting catalog. User rules with the same
/// <see cref="PathRule.Id"/> as an embedded rule override the embedded one
/// (last-write-wins). Mirrors the contract of <see cref="IPublisherRuleEngine"/>.
/// </summary>
public interface IPathRuleEngine
{
    /// <summary>All loaded rules (embedded + user, post-merge). Empty until
    /// <see cref="LoadAsync"/> has run successfully.</summary>
    IReadOnlyList<PathRule> AllRules { get; }

    /// <summary>Loads embedded resources and user-folder JSONs. Idempotent — safe
    /// to call multiple times (replaces the in-memory rule set on each call).
    /// Never throws on malformed JSON; bad files are logged at Warning and skipped.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns ALL rules whose expanded <see cref="PathRule.PathPattern"/> is a
    /// case-insensitive prefix of <paramref name="fullPath"/>, sorted by
    /// specificity descending (longer expanded pattern = more specific = first).
    /// Empty list if no rule matches. Rules whose pattern fails env-var expansion
    /// (still contains <c>%</c> after <c>Environment.ExpandEnvironmentVariables</c>)
    /// are silently skipped.
    /// </summary>
    IReadOnlyList<RuleHit> Match(string fullPath);
}
