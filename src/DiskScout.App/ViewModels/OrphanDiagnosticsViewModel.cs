using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Helpers;
using DiskScout.Models;

namespace DiskScout.ViewModels;

/// <summary>
/// Read-only view-model bound to the <c>OrphanDiagnosticsWindow</c> ("Pourquoi ?"
/// modal). Wraps a single <see cref="AppDataOrphanCandidate"/> and pre-formats
/// every field the XAML displays. All properties are read-only — assignment
/// happens in the constructor.
/// </summary>
public sealed partial class OrphanDiagnosticsViewModel : ObservableObject
{
    public string FullPath { get; }
    public string ParentSignificantPath { get; }
    public int ConfidenceScore { get; }
    public RiskLevel Risk { get; }
    public string RiskLabel => Risk.ToString();
    public RecommendedAction Action { get; }
    public string ActionLabel => Action.ToString();
    public string CategoryLabel { get; }
    public IReadOnlyList<string> TriggeredRulesLines { get; }
    public IReadOnlyList<string> MatchedSourcesLines { get; }
    public string SizeDisplay { get; }
    public string LastWriteDisplay { get; }
    public string Reason { get; }

    public OrphanDiagnosticsViewModel(AppDataOrphanCandidate diag)
    {
        ArgumentNullException.ThrowIfNull(diag);

        FullPath = diag.FullPath;
        ParentSignificantPath = diag.ParentSignificantPath;
        ConfidenceScore = diag.ConfidenceScore;
        Risk = diag.Risk;
        Action = diag.Action;
        CategoryLabel = diag.Category.ToString();
        Reason = diag.Reason;

        TriggeredRulesLines = diag.TriggeredRules is { Count: > 0 }
            ? diag.TriggeredRules
                .Select(r => $"{r.RuleId} — {r.Reason}")
                .ToArray()
            : Array.Empty<string>();

        MatchedSourcesLines = diag.MatchedSources is { Count: > 0 }
            ? diag.MatchedSources
                .Select(h => $"{h.Source}: {h.Evidence} ({h.ScoreDelta:+#;-#;+0} pts)")
                .ToArray()
            : Array.Empty<string>();

        SizeDisplay = ByteFormat.Fmt(diag.SizeBytes);
        LastWriteDisplay = diag.LastWriteUtc
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }
}
