using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class OrphansViewModel : ObservableObject
{
    private readonly IFileDeletionService _deletion;
    private readonly ILogger _logger;
    private readonly HashSet<OrphanCategory> _acceptedCategories;

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour détecter les fichiers rémanents.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private long _selectedBytes;

    public ObservableCollection<OrphanCategoryGroup> Groups { get; } = new();

    public OrphansViewModel(
        IFileDeletionService deletion,
        ILogger logger,
        IEnumerable<OrphanCategory>? acceptedCategories = null,
        string? emptyStateMessage = null)
    {
        _deletion = deletion;
        _logger = logger;
        _acceptedCategories = acceptedCategories is null
            ? new HashSet<OrphanCategory>(Enum.GetValues<OrphanCategory>())
            : new HashSet<OrphanCategory>(acceptedCategories);

        if (emptyStateMessage is not null) EmptyStateMessage = emptyStateMessage;
    }

    public void Load(IEnumerable<OrphanCandidate> candidates)
    {
        foreach (var g in Groups)
        {
            foreach (var r in g.Rows) r.PropertyChanged -= OnRowPropertyChanged;
            g.Rows.CollectionChanged -= OnGroupRowsChanged;
        }
        Groups.Clear();

        long total = 0;
        int totalCount = 0;

        var byCategory = candidates
            .Where(c => _acceptedCategories.Contains(c.Category))
            .GroupBy(c => c.Category)
            .OrderByDescending(g => g.Sum(c => c.SizeBytes));

        foreach (var g in byCategory)
        {
            var rows = g.OrderByDescending(c => c.SizeBytes).Select(c => new OrphanRow(c)).ToList();
            var group = new OrphanCategoryGroup(g.Key, rows);
            foreach (var r in rows)
            {
                r.PropertyChanged += OnRowPropertyChanged;
                total += r.SizeBytes;
                totalCount++;
            }
            group.Rows.CollectionChanged += OnGroupRowsChanged;
            Groups.Add(group);
        }

        Count = totalCount;
        TotalBytes = total;
        SelectedCount = 0;
        SelectedBytes = 0;
        HasResults = totalCount > 0;
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OrphanRow.IsSelected)) return;
        RecomputeSelection();
    }

    private void OnGroupRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (OrphanRow r in e.OldItems) r.PropertyChanged -= OnRowPropertyChanged;
        }
        RecomputeSelection();
        RecomputeTotals();
    }

    private void RecomputeSelection()
    {
        int sel = 0;
        long bytes = 0;
        foreach (var g in Groups)
        {
            int groupSel = 0;
            foreach (var r in g.Rows)
            {
                if (r.IsSelected) { sel++; bytes += r.SizeBytes; groupSel++; }
            }
            g.SelectedCount = groupSel;
            g.UpdateAllSelectedFlag();
        }
        SelectedCount = sel;
        SelectedBytes = bytes;
    }

    private void RecomputeTotals()
    {
        long total = 0;
        int totalCount = 0;
        foreach (var g in Groups)
        {
            g.RecomputeStats();
            total += g.TotalBytes;
            totalCount += g.Count;
        }
        Count = totalCount;
        TotalBytes = total;
        HasResults = totalCount > 0;
    }

    [RelayCommand]
    private void ToggleCategorySelection(OrphanCategoryGroup? group)
    {
        if (group is null) return;
        var newValue = !group.AreAllSelected;
        foreach (var r in group.Rows) r.IsSelected = newValue;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var g in Groups)
            foreach (var r in g.Rows) r.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var g in Groups)
            foreach (var r in g.Rows) r.IsSelected = false;
    }

    [RelayCommand]
    private void GenerateAiAuditPrompt()
    {
        var selected = Groups.SelectMany(g => g.Rows).Where(r => r.IsSelected).ToList();
        // Enrich each item's Reason with its DiskScout category label so the LLM knows
        // exactly which heuristic triggered.
        var items = selected.Select(r =>
        {
            var categoryLabel = CategoryLabelFor(r.Category);
            var reason = string.IsNullOrWhiteSpace(r.Reason)
                ? $"[{categoryLabel}]"
                : $"[{categoryLabel}] {r.Reason}";
            return new AuditItem(r.FullPath, r.SizeBytes, reason);
        }).ToList();
        var context = _acceptedCategories.Contains(OrphanCategory.AppDataOrphan)
            ? TabContexts.Remnants
            : TabContexts.Cleanup;
        AuditPromptBuilder.BuildAndCopy(context, items);
    }

    private static string CategoryLabelFor(OrphanCategory c) => c switch
    {
        OrphanCategory.AppDataOrphan => "AppData orphelin",
        OrphanCategory.EmptyProgramFiles => "Program Files vide",
        OrphanCategory.StaleTemp => "Temp ancien",
        OrphanCategory.OrphanInstallerPatch => "Patch MSI orphelin",
        OrphanCategory.SystemArtifact => "Artefact système",
        OrphanCategory.DevCache => "Cache de développement",
        OrphanCategory.BrowserCache => "Cache de navigateur",
        OrphanCategory.EmptyFolder => "Dossier vide",
        OrphanCategory.BrokenShortcut => "Raccourci cassé",
        _ => "Autre",
    };

    [RelayCommand]
    private void CopyPath(OrphanRow? row)
    {
        if (row is null || string.IsNullOrEmpty(row.FullPath)) return;
        try { Clipboard.SetText(row.FullPath); } catch { /* clipboard busy */ }
    }

    [RelayCommand]
    private async Task DeleteAsync(OrphanRow? row)
    {
        if (row is null) return;
        var summary = $"Supprimer :{Environment.NewLine}{row.FullPath}{Environment.NewLine}Taille : {DeletePrompt.FormatBytes(row.SizeBytes)}";
        var mode = DeletePrompt.Ask(summary);
        if (mode == DeleteMode.Cancelled) return;

        var result = await _deletion.DeleteAsync(new[] { row.FullPath }, mode);
        DeletePrompt.ShowResult(result, mode);

        if (result.SuccessCount > 0) RemoveRow(row);
    }

    [RelayCommand]
    private async Task PurgeSelectedAsync()
    {
        var selected = Groups.SelectMany(g => g.Rows).Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var totalBytes = selected.Sum(r => r.SizeBytes);
        var risky = selected.Where(r => FileSafety.Score(r.FullPath) <= 10).ToList();
        var summary =
            $"{selected.Count} élément(s) sélectionné(s) — {ByteFormat.Fmt(totalBytes)} à libérer." +
            Environment.NewLine + Environment.NewLine +
            "Premiers éléments :" + Environment.NewLine +
            string.Join(Environment.NewLine, selected.Take(6).Select(r => $"  • {r.FullPath}")) +
            (selected.Count > 6 ? Environment.NewLine + $"  … et {selected.Count - 6} autre(s)" : string.Empty) +
            (risky.Count > 0
                ? Environment.NewLine + Environment.NewLine +
                  $"⚠ {risky.Count} élément(s) à risque détecté(s) (projet actif avec .git/package.json/*.sln ou chemin OS-critique) :" +
                  Environment.NewLine +
                  string.Join(Environment.NewLine, risky.Take(4).Select(r => $"  ⚠ {r.FullPath}"))
                : string.Empty);

        var mode = DeletePrompt.Ask(summary);
        if (mode == DeleteMode.Cancelled) return;

        var paths = selected.Select(r => r.FullPath).ToArray();
        var result = await _deletion.DeleteAsync(paths, mode);
        DeletePrompt.ShowResult(result, mode);

        if (result.SuccessCount > 0)
        {
            var successPaths = new HashSet<string>(
                result.Entries.Where(e => e.Success).Select(e => e.Path),
                StringComparer.OrdinalIgnoreCase);
            var toRemove = selected.Where(r => successPaths.Contains(r.FullPath)).ToList();
            foreach (var r in toRemove) RemoveRow(r);
        }
    }

    private void RemoveRow(OrphanRow row)
    {
        foreach (var g in Groups)
        {
            if (g.Rows.Remove(row))
            {
                row.PropertyChanged -= OnRowPropertyChanged;
                break;
            }
        }
        for (int i = Groups.Count - 1; i >= 0; i--)
        {
            if (Groups[i].Rows.Count == 0)
            {
                Groups[i].Rows.CollectionChanged -= OnGroupRowsChanged;
                Groups.RemoveAt(i);
            }
        }
        RecomputeTotals();
        RecomputeSelection();
    }
}

public sealed partial class OrphanCategoryGroup : ObservableObject
{
    public OrphanCategory Category { get; }
    public string Label { get; }
    public ObservableCollection<OrphanRow> Rows { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _areAllSelected;

    [ObservableProperty]
    private System.Windows.Media.SolidColorBrush _accentBrush;

    public string TotalBytesDisplay => FormatBytes(TotalBytes);
    public string CountLabel => $"{Count} élément{(Count > 1 ? "s" : "")}";
    public string SelectedLabel =>
        SelectedCount == 0 ? string.Empty : $"  ({SelectedCount} sélectionné{(SelectedCount > 1 ? "s" : "")})";

    public OrphanCategoryGroup(OrphanCategory category, IEnumerable<OrphanRow> rows)
    {
        Category = category;
        Label = LabelFor(category);
        Rows = new ObservableCollection<OrphanRow>(rows);
        AccentBrush = BrushFor(category);
        RecomputeStats();
    }

    public void RecomputeStats()
    {
        Count = Rows.Count;
        TotalBytes = Rows.Sum(r => r.SizeBytes);
        OnPropertyChanged(nameof(TotalBytesDisplay));
        OnPropertyChanged(nameof(CountLabel));
    }

    partial void OnSelectedCountChanged(int value)
        => OnPropertyChanged(nameof(SelectedLabel));

    public void UpdateAllSelectedFlag()
        => AreAllSelected = Rows.Count > 0 && Rows.All(r => r.IsSelected);

    private static string LabelFor(OrphanCategory c) => c switch
    {
        OrphanCategory.AppDataOrphan => "AppData orphelins",
        OrphanCategory.EmptyProgramFiles => "Program Files vides",
        OrphanCategory.StaleTemp => "Fichiers Temp anciens",
        OrphanCategory.OrphanInstallerPatch => "Patches MSI orphelins",
        OrphanCategory.SystemArtifact => "Artefacts système",
        OrphanCategory.DevCache => "Caches de développement",
        OrphanCategory.BrowserCache => "Caches de navigateurs",
        OrphanCategory.EmptyFolder => "Dossiers vides",
        OrphanCategory.BrokenShortcut => "Raccourcis cassés",
        _ => "Autres",
    };

    private static System.Windows.Media.SolidColorBrush BrushFor(OrphanCategory c) => c switch
    {
        OrphanCategory.AppDataOrphan        => new(System.Windows.Media.Color.FromRgb(0xE7, 0x4C, 0x3C)),
        OrphanCategory.EmptyProgramFiles    => new(System.Windows.Media.Color.FromRgb(0xE6, 0x7E, 0x22)),
        OrphanCategory.StaleTemp            => new(System.Windows.Media.Color.FromRgb(0xF1, 0xC4, 0x0F)),
        OrphanCategory.OrphanInstallerPatch => new(System.Windows.Media.Color.FromRgb(0x9B, 0x59, 0xB6)),
        OrphanCategory.SystemArtifact       => new(System.Windows.Media.Color.FromRgb(0x34, 0x98, 0xDB)),
        OrphanCategory.DevCache             => new(System.Windows.Media.Color.FromRgb(0x1A, 0xBC, 0x9C)),
        OrphanCategory.BrowserCache         => new(System.Windows.Media.Color.FromRgb(0xE9, 0x1E, 0x63)),
        OrphanCategory.EmptyFolder          => new(System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6)),
        OrphanCategory.BrokenShortcut       => new(System.Windows.Media.Color.FromRgb(0x79, 0x55, 0x48)),
        _                                    => new(System.Windows.Media.Color.FromRgb(0x7F, 0x8C, 0x8D)),
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {u[i]}";
    }
}

public sealed partial class OrphanRow : ObservableObject
{
    public OrphanRow(OrphanCandidate c)
    {
        FullPath = c.FullPath;
        SizeBytes = c.SizeBytes;
        Reason = c.Reason;
        Category = c.Category;
        Diagnostics = c.Diagnostics;
    }

    public string FullPath { get; }
    public long SizeBytes { get; }
    public string Reason { get; }
    public OrphanCategory Category { get; }

    /// <summary>
    /// Phase 10 rich diagnostics — populated only for AppData rows by
    /// <c>AppDataOrphanPipeline</c>. Null for every other category, which
    /// drives the Score badge / "Pourquoi ?" button visibility in XAML.
    /// </summary>
    public AppDataOrphanCandidate? Diagnostics { get; }

    /// <summary>True when <see cref="Diagnostics"/> is set; bound to badge + button visibility.</summary>
    public bool HasDiagnostics => Diagnostics is not null;

    /// <summary>0-100 confidence score, or null when no diagnostics attached.</summary>
    public int? ConfidenceScore => Diagnostics?.ConfidenceScore;

    /// <summary>Risk band, or null when no diagnostics attached.</summary>
    public RiskLevel? Risk => Diagnostics?.Risk;

    /// <summary>Score formatted as a string for the badge TextBlock; null when no diagnostics.</summary>
    public string? ScoreBadgeText =>
        Diagnostics is null ? null : Diagnostics.ConfidenceScore.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool _isSelected;

    public string SizeDisplay
    {
        get
        {
            if (SizeBytes <= 0) return "—";
            string[] u = { "o", "Ko", "Mo", "Go", "To" };
            double v = SizeBytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {u[i]}";
        }
    }
}
