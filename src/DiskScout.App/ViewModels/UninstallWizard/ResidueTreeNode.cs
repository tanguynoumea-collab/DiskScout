using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;

namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// Hierarchical, tri-state-checkable node for the Confirm Delete tree view.
/// Roots represent <see cref="ResidueCategory"/> groupings; children are leaf <see cref="ResidueFinding"/>s.
/// Default <see cref="IsChecked"/> is <c>false</c> — the user MUST tick boxes (must_haves truth #4).
/// </summary>
public sealed partial class ResidueTreeNode : ObservableObject
{
    /// <summary>User-visible label (category title or path).</summary>
    public string Label { get; init; } = "";

    /// <summary>Resolved path on disk / in registry. Null for category/group nodes.</summary>
    public string? Path { get; init; }

    /// <summary>Bytes that would be reclaimed if this leaf is selected. 0 for groups / non-fs categories.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Residue category this node belongs to.</summary>
    public ResidueCategory Category { get; init; }

    /// <summary>Confidence the finding is genuinely residue (default Medium for non-trace findings).</summary>
    public ResidueTrustLevel Trust { get; init; }

    /// <summary>Provenance: trace match / publisher rule / name heuristic.</summary>
    public ResidueSource Source { get; init; }

    /// <summary>Human-readable explanation of which heuristic produced the finding.</summary>
    public string? Reason { get; init; }

    /// <summary>Children (empty for leaves).</summary>
    public ObservableCollection<ResidueTreeNode> Children { get; } = new();

    /// <summary>
    /// Tri-state checkbox: false = unchecked, true = fully checked, null = mixed (some children checked).
    /// Defaults to <c>false</c> — every leaf starts UNCHECKED per CONTEXT.md safety requirement.
    /// </summary>
    [ObservableProperty]
    private bool? _isChecked = false;

    /// <summary>Pretty-printed size for the UI.</summary>
    public string SizeDisplay => DiskScout.Helpers.ByteFormat.Fmt(SizeBytes);

    /// <summary>Pretty-printed trust level (French — UI language).</summary>
    public string TrustDisplay => Trust switch
    {
        ResidueTrustLevel.HighConfidence => "Confiance haute",
        ResidueTrustLevel.MediumConfidence => "Confiance moyenne",
        ResidueTrustLevel.LowConfidence => "Confiance basse",
        _ => "",
    };

    /// <summary>
    /// When the user toggles a parent checkbox, propagate the state to all children.
    /// (When IsChecked becomes null = mixed, we don't propagate — that state is set programmatically
    /// from RecomputeTotals when a child changes independently.)
    /// </summary>
    partial void OnIsCheckedChanged(bool? value)
    {
        if (Children.Count == 0 || value is null) return;
        foreach (var child in Children) child.IsChecked = value;
    }

    /// <summary>
    /// Recursively yield every leaf (Path != null) whose <see cref="IsChecked"/> is true.
    /// Used by ConfirmDeleteStepViewModel to compute <c>SelectedPaths</c> + <c>TotalSelectedBytes</c>.
    /// </summary>
    public IEnumerable<ResidueTreeNode> EnumerateLeavesChecked()
    {
        if (Path is not null && IsChecked == true) yield return this;
        foreach (var c in Children)
        {
            foreach (var leaf in c.EnumerateLeavesChecked()) yield return leaf;
        }
    }
}
