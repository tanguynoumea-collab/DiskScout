namespace DiskScout.Models;

/// <summary>
/// Category of a <see cref="PathRule"/>. Drives downstream pipeline behavior in
/// the AppData orphan detector (Phase 10).
/// </summary>
public enum PathCategory
{
    /// <summary>
    /// HardBlacklist — forces Risk=Critique + Action=NePasToucher in the pipeline.
    /// Used for Windows OS components only (System32, WinSxS, drivers, Microsoft.NET,
    /// servicing, assembly, WindowsApps, regid.*, Package Cache MSI store, etc.).
    /// Items matching <see cref="OsCriticalDoNotPropose"/> are suppressed from output entirely.
    /// </summary>
    OsCriticalDoNotPropose,

    /// <summary>
    /// MSI / Visual C++ Redistributable / Office C2R package caches. Carries an
    /// implicit <see cref="RiskLevel.Eleve"/> floor — never auto-Supprimer.
    /// </summary>
    PackageCache,

    /// <summary>
    /// %ProgramData%\Intel, NVIDIA, AMD, Realtek driver data. No floor by default;
    /// pipeline still consults active drivers via the matchers stage.
    /// </summary>
    DriverData,

    /// <summary>
    /// Corporate management / RMM / antivirus agents (NinjaRMM, Bitdefender,
    /// Zscaler, Matrix42, Empirum, Splashtop). Carries an implicit
    /// <see cref="RiskLevel.Eleve"/> floor — never auto-Supprimer.
    /// </summary>
    CorporateAgent,

    /// <summary>
    /// Shared vendor workspaces (%ProgramData%\Adobe, Autodesk, JetBrains,
    /// Microsoft\OneDrive). No floor — score-driven via the matchers stage.
    /// </summary>
    VendorShared,

    /// <summary>
    /// Generic match: rule matched but no special semantic; lets downstream
    /// scoring decorate the candidate normally.
    /// </summary>
    Generic,
}

/// <summary>
/// Risk level assigned by the pipeline RiskLevelClassifier (stage [7]).
/// Explicit integer values pin ordering for comparisons (higher = more dangerous).
/// </summary>
public enum RiskLevel
{
    /// <summary>Score &gt;= 80 — safe to delete (true residue).</summary>
    Aucun = 0,

    /// <summary>Score 60-79 — minor concern (e.g., empty log folders).</summary>
    Faible = 1,

    /// <summary>Score 40-59 — verify before deletion (caches, ambiguous matches).</summary>
    Moyen = 2,

    /// <summary>Score 20-39 — high concern (active vendor, corporate agents).</summary>
    Eleve = 3,

    /// <summary>Score &lt; 20 OR <see cref="PathCategory.OsCriticalDoNotPropose"/> hit — never touch.</summary>
    Critique = 4,
}

/// <summary>
/// Recommendation surfaced to the user. Mapped from <see cref="RiskLevel"/> by
/// the RiskLevelClassifier.
/// </summary>
public enum RecommendedAction
{
    /// <summary>Safe — matches <see cref="RiskLevel.Aucun"/> (score &gt;= 80).</summary>
    Supprimer,

    /// <summary>Recoverable deletion — matches <see cref="RiskLevel.Faible"/>.</summary>
    CorbeilleOk,

    /// <summary>Manual review — matches <see cref="RiskLevel.Moyen"/>.</summary>
    VerifierAvant,

    /// <summary>Never propose — matches <see cref="RiskLevel.Eleve"/> / <see cref="RiskLevel.Critique"/>.</summary>
    NePasToucher,
}

/// <summary>
/// JSON-driven path rule. Each rule supplies a single path-prefix pattern (with
/// %ENVVAR% expansion) and a <see cref="PathCategory"/>. Patterns are matched
/// case-insensitively against the candidate full path. Rules with
/// <see cref="MinRiskFloor"/> set bound the candidate's risk from below
/// (e.g., PackageCache rules cap descent at <see cref="RiskLevel.Eleve"/>).
/// </summary>
public sealed record PathRule(
    string Id,
    string PathPattern,
    PathCategory Category,
    RiskLevel? MinRiskFloor,
    string? Reason);

/// <summary>
/// A single rule that fired against a candidate path. Returned by
/// <c>IPathRuleEngine.Match</c> and embedded into the orphan candidate's
/// trace for the "Pourquoi ?" UI panel.
/// </summary>
public sealed record RuleHit(string RuleId, PathCategory Category, string Reason);

/// <summary>
/// A signal from one of the parallel matchers (Registry, Service, Driver, Appx,
/// ScheduledTask, Process). <see cref="ScoreDelta"/> is the (typically negative)
/// number applied to the base 100 confidence score by the ConfidenceScorer.
/// </summary>
/// <param name="Source">"Registry" | "Service" | "Driver" | "Appx" | "ScheduledTask" | "Process"</param>
/// <param name="Evidence">Free-text description, e.g. "HKLM\SOFTWARE\Microsoft\NET" or "Service:wuauserv (Running)".</param>
/// <param name="ScoreDelta">Score delta applied to the base 100. Negative = candidate is NOT a residue (subtract from score). Positive bonuses (empty folder, age) are also expressed via this field with positive values.</param>
public sealed record MatcherHit(string Source, string Evidence, int ScoreDelta);
