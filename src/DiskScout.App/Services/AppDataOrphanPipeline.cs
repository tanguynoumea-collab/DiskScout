using System.IO;
using DiskScout.Helpers;
using DiskScout.Models;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Default <see cref="IAppDataOrphanPipeline"/>. Composes the 7 stages
/// described in CONTEXT.md and the interface XML doc into a single
/// <see cref="EvaluateAsync"/> pass. Stateless (apart from the injected
/// dependencies' own state) and thread-safe.
/// </summary>
public sealed class AppDataOrphanPipeline : IAppDataOrphanPipeline
{
    private readonly ILogger _logger;
    private readonly IPathRuleEngine _pathRuleEngine;
    private readonly IParentContextAnalyzer _parentAnalyzer;
    private readonly IMachineSnapshotProvider _snapshotProvider;
    private readonly IPublisherAliasResolver _aliasResolver;
    private readonly IConfidenceScorer _scorer;
    private readonly IRiskLevelClassifier _classifier;
    private readonly IServiceMatcher _serviceMatcher;
    private readonly IDriverMatcher _driverMatcher;
    private readonly IAppxMatcher _appxMatcher;
    private readonly IRegistryMatcher _registryMatcher;

    public AppDataOrphanPipeline(
        ILogger logger,
        IPathRuleEngine pathRuleEngine,
        IParentContextAnalyzer parentAnalyzer,
        IMachineSnapshotProvider snapshotProvider,
        IPublisherAliasResolver aliasResolver,
        IConfidenceScorer scorer,
        IRiskLevelClassifier classifier,
        IServiceMatcher serviceMatcher,
        IDriverMatcher driverMatcher,
        IAppxMatcher appxMatcher,
        IRegistryMatcher registryMatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathRuleEngine = pathRuleEngine ?? throw new ArgumentNullException(nameof(pathRuleEngine));
        _parentAnalyzer = parentAnalyzer ?? throw new ArgumentNullException(nameof(parentAnalyzer));
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _aliasResolver = aliasResolver ?? throw new ArgumentNullException(nameof(aliasResolver));
        _scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _serviceMatcher = serviceMatcher ?? throw new ArgumentNullException(nameof(serviceMatcher));
        _driverMatcher = driverMatcher ?? throw new ArgumentNullException(nameof(driverMatcher));
        _appxMatcher = appxMatcher ?? throw new ArgumentNullException(nameof(appxMatcher));
        _registryMatcher = registryMatcher ?? throw new ArgumentNullException(nameof(registryMatcher));
    }

    public async Task<AppDataOrphanCandidate?> EvaluateAsync(
        FileSystemNode node,
        IReadOnlyList<InstalledProgram> programs,
        CancellationToken cancellationToken = default)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        programs ??= Array.Empty<InstalledProgram>();

        cancellationToken.ThrowIfCancellationRequested();

        // Step 2: ParentContextAnalyzer — normalize generic-leaf candidates
        // (Logs, Cache, en-us, ...) up to the meaningful vendor folder.
        var significantPath = _parentAnalyzer.GetSignificantParent(node.FullPath);
        var folderName = Path.GetFileName(significantPath);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            // Drive root or empty leaf — fall back to the original folder name.
            folderName = node.Name;
        }

        // Step 3: KnownPathRules — match against the engine. The hits are
        // sorted by specificity DESC by the engine; the first hit (most
        // specific) drives the path category and the floor lookup.
        var ruleHits = _pathRuleEngine.Match(significantPath);

        // Step 1: HardBlacklist gate (intentionally executed AFTER the engine
        // call so we get the rule hits in one pass). If ANY hit has category
        // OsCriticalDoNotPropose, return null — the user must never see these.
        for (int i = 0; i < ruleHits.Count; i++)
        {
            if (ruleHits[i].Category == PathCategory.OsCriticalDoNotPropose)
            {
                _logger.Debug(
                    "AppDataOrphanPipeline: HardBlacklist suppressed {Path} via rule {RuleId}",
                    node.FullPath, ruleHits[i].RuleId);
                return null;
            }
        }

        var pathCategory = ruleHits.Count > 0 ? ruleHits[0].Category : PathCategory.Generic;

        // Look up the MIN risk floor from any matched PathRule. The engine's
        // RuleHit doesn't carry MinRiskFloor (kept the Plan-10-01 contract
        // stable), so we re-resolve via PathRule.Id.
        RiskLevel? minRiskFloor = null;
        if (ruleHits.Count > 0)
        {
            foreach (var hit in ruleHits)
            {
                var rule = _pathRuleEngine.AllRules.FirstOrDefault(
                    r => string.Equals(r.Id, hit.RuleId, StringComparison.OrdinalIgnoreCase));
                if (rule?.MinRiskFloor is null) continue;
                if (minRiskFloor is null || (int)rule.MinRiskFloor.Value > (int)minRiskFloor.Value)
                {
                    minRiskFloor = rule.MinRiskFloor;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Step 7 prerequisite: snapshot for matchers.
        var snapshot = await _snapshotProvider.GetAsync(cancellationToken).ConfigureAwait(false);

        // Step 5: PublisherAliasResolver — bridge the folder name to a
        // canonical publisher. The resolver does its own fuzzy fallback;
        // it returns null below 0.7. Used by RegistryMatcher for the
        // canonical-publisher containment branch and by the
        // service / driver / appx matchers for cross-source overlap.
        string? canonicalPublisher = null;
        var distinctPublishers = programs
            .Where(p => !string.IsNullOrWhiteSpace(p.Publisher))
            .Select(p => p.Publisher!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Pass at most one publisher; if multiple, the resolver falls through
        // to fuzzy-only on the folder which is what we want (the canonical is
        // not driven by a single registry publisher when there are many).
        // For the typical case one publisher dominates the AppData neighborhood.
        var oneRepresentativePublisher = distinctPublishers.FirstOrDefault();
        var aliasResult = await _aliasResolver.ResolveAsync(
            folderName!, oneRepresentativePublisher, displayName: null, cancellationToken)
            .ConfigureAwait(false);
        canonicalPublisher = aliasResult?.MatchedCanonical;

        cancellationToken.ThrowIfCancellationRequested();

        // Step 4: MultiSourceMatcher — 4 matchers run sequentially (each is
        // already cheap once the snapshot is built; the Task.WhenAll ceremony
        // would add overhead with no measurable benefit at the 1-3 ms scale).
        var parentDir = SafeGetParentDirectory(node.FullPath);

        var allMatcherHits = new List<MatcherHit>();
        allMatcherHits.AddRange(_registryMatcher.Match(folderName!, canonicalPublisher, node.FullPath, programs));
        allMatcherHits.AddRange(_serviceMatcher.Match(folderName!, canonicalPublisher, parentDir, snapshot));
        allMatcherHits.AddRange(_driverMatcher.Match(folderName!, canonicalPublisher, snapshot));
        allMatcherHits.AddRange(_appxMatcher.Match(folderName!, canonicalPublisher, parentDir, snapshot));

        cancellationToken.ThrowIfCancellationRequested();

        // Determine HasExeOrDll: cheap check — list .exe / .dll in the
        // top-level directory. If the directory can't be enumerated, assume
        // true (more conservative, less likely to over-bonus the score).
        bool hasExeOrDll = ProbeForBinaries(node.FullPath);

        // Step 6: ConfidenceScorer.
        var scorerInput = new AppDataOrphanInput(
            FullPath: node.FullPath,
            SizeBytes: node.SizeBytes,
            LastWriteUtc: node.LastModifiedUtc,
            HasExeOrDll: hasExeOrDll,
            PathRuleCategory: ruleHits.Count > 0 ? pathCategory : (PathCategory?)null,
            MatcherHits: allMatcherHits);
        int score = _scorer.Compute(scorerInput);

        // Step 7: RiskLevelClassifier.
        var (risk, action) = _classifier.Classify(score, ruleHits.Count > 0 ? pathCategory : (PathCategory?)null, minRiskFloor);

        // Defense-in-depth boundary: the pipeline must never propose a path
        // that the existing ResiduePathSafety whitelist (Phase 9) would block.
        // If a path slips past the PathRule engine but trips the safety net,
        // force Critique / NePasToucher.
        if (!ResiduePathSafety.IsSafeToPropose(node.FullPath))
        {
            _logger.Debug(
                "AppDataOrphanPipeline: ResiduePathSafety override forced Critique on {Path}",
                node.FullPath);
            risk = RiskLevel.Critique;
            action = RecommendedAction.NePasToucher;
        }

        var reason = BuildReason(ruleHits.Count, allMatcherHits.Count, score);

        return new AppDataOrphanCandidate(
            NodeId: node.Id,
            FullPath: node.FullPath,
            SizeBytes: node.SizeBytes,
            LastWriteUtc: node.LastModifiedUtc,
            ParentSignificantPath: significantPath,
            Category: pathCategory,
            MatchedSources: allMatcherHits,
            TriggeredRules: ruleHits,
            ConfidenceScore: score,
            Risk: risk,
            Action: action,
            Reason: reason);
    }

    private static string BuildReason(int ruleCount, int matcherCount, int score)
    {
        return $"{ruleCount} règle(s), {matcherCount} signal(aux) machine, score {score}/100";
    }

    private static string? SafeGetParentDirectory(string fullPath)
    {
        try
        {
            return Path.GetDirectoryName(fullPath);
        }
        catch
        {
            return null;
        }
    }

    private bool ProbeForBinaries(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath)) return false;
            foreach (var _ in Directory.EnumerateFiles(folderPath, "*.exe", SearchOption.TopDirectoryOnly))
                return true;
            foreach (var _ in Directory.EnumerateFiles(folderPath, "*.dll", SearchOption.TopDirectoryOnly))
                return true;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Conservative: if we can't enumerate, assume there ARE binaries
            // (denies the +10 bonus, makes the score more cautious).
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "AppDataOrphanPipeline: ProbeForBinaries failed for {Path}", folderPath);
            return true;
        }
    }
}
