using DiskScout.Models;

namespace DiskScout.Services;

/// <summary>
/// JSON rule engine knowing per-publisher residue patterns (Adobe, Autodesk, JetBrains,
/// Mozilla, Microsoft, Steam, Epic, ...). Rules are embedded in the assembly at build time
/// via <c>&lt;EmbeddedResource&gt;</c> and merged with user-supplied rules from
/// <c>%LocalAppData%\DiskScout\publisher-rules\*.json</c> at <see cref="LoadAsync"/> time.
/// </summary>
public interface IPublisherRuleEngine
{
    /// <summary>Loads embedded + user rules. Last-write-wins by <c>Id</c>: a user rule with
    /// the same id overrides the embedded one. Malformed JSON is logged at Warning and skipped.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>All rules currently loaded (embedded merged with user). Empty before <see cref="LoadAsync"/>.</summary>
    IReadOnlyList<PublisherRule> AllRules { get; }

    /// <summary>Returns rules that match the given <paramref name="publisher"/> + <paramref name="displayName"/>,
    /// sorted by <see cref="PublisherRuleMatch.SpecificityScore"/> descending.
    /// A rule whose <c>DisplayNamePattern</c> matches scores higher than one that only matched on publisher.</summary>
    IReadOnlyList<PublisherRuleMatch> Match(string? publisher, string displayName);

    /// <summary>Expand <c>%ENVVAR%</c> (via <c>Environment.ExpandEnvironmentVariables</c>) and
    /// <c>{Publisher}</c> / <c>{DisplayName}</c> tokens in a path or registry pattern.</summary>
    string ExpandTokens(string template, string? publisher, string displayName);
}
