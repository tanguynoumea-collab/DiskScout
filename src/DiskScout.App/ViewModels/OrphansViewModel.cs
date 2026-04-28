using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
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

    /// <summary>
    /// Phase 10 — current sort key. Default is <see cref="OrphanSortKey.Size"/>.
    /// Combined with <see cref="OrphanSortDirection"/> to produce the actual ordering.
    /// </summary>
    [ObservableProperty]
    private OrphanSortKey _sortKey = OrphanSortKey.Size;

    /// <summary>
    /// Sort direction (Descending = largest/most-confident/most-risky first).
    /// </summary>
    [ObservableProperty]
    private OrphanSortDirection _sortDirection = OrphanSortDirection.Descending;

    /// <summary>Toolbar toggle button label — flips between ↓ and ↑.</summary>
    public string SortDirectionLabel => SortDirection == OrphanSortDirection.Descending ? "↓" : "↑";

    /// <summary>Toolbar toggle button tooltip.</summary>
    public string SortDirectionTooltip => SortDirection == OrphanSortDirection.Descending
        ? "Décroissant — clique pour passer en croissant"
        : "Croissant — clique pour passer en décroissant";

    /// <summary>
    /// Phase 10 — risk-based filter applied to every group's rows.
    /// Default <see cref="OrphanFilterMode.All"/> shows everything.
    /// </summary>
    [ObservableProperty]
    private OrphanFilterMode _filterMode = OrphanFilterMode.All;

    /// <summary>ComboBox source for the sort-key picker (enum → French label).</summary>
    public IReadOnlyList<KeyValuePair<OrphanSortKey, string>> SortKeyOptions { get; } = new[]
    {
        new KeyValuePair<OrphanSortKey, string>(OrphanSortKey.Size,  "Taille"),
        new KeyValuePair<OrphanSortKey, string>(OrphanSortKey.Score, "Score"),
        new KeyValuePair<OrphanSortKey, string>(OrphanSortKey.Risk,  "Risque"),
    };

    /// <summary>ComboBox source for the filter-mode picker.</summary>
    public IReadOnlyList<KeyValuePair<OrphanFilterMode, string>> FilterModeOptions { get; } = new[]
    {
        new KeyValuePair<OrphanFilterMode, string>(OrphanFilterMode.All,          "Tous"),
        new KeyValuePair<OrphanFilterMode, string>(OrphanFilterMode.SafeOnly,     "Sûrs uniquement (≥ 80)"),
        new KeyValuePair<OrphanFilterMode, string>(OrphanFilterMode.SafeAndLow,   "Sûrs + faible risque (≥ 60)"),
        new KeyValuePair<OrphanFilterMode, string>(OrphanFilterMode.NonCritical,  "Tout sauf critique (≥ 20)"),
        new KeyValuePair<OrphanFilterMode, string>(OrphanFilterMode.CriticalOnly, "Critique seul (audit)"),
    };

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
            var rows = SortRows(g.Select(c => new OrphanRow(c)), SortKey, SortDirection).ToList();
            var group = new OrphanCategoryGroup(g.Key, rows);
            foreach (var r in rows)
            {
                r.PropertyChanged += OnRowPropertyChanged;
                total += r.SizeBytes;
                totalCount++;
            }
            group.Rows.CollectionChanged += OnGroupRowsChanged;
            // Apply current filter immediately so re-loads honor the user's setting.
            group.RowsView.Filter = RowMatchesFilter;
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

    /// <summary>
    /// Phase 10 — toggle sort direction (↓ ↔ ↑) on the current <see cref="SortKey"/>.
    /// </summary>
    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortDirection = SortDirection == OrphanSortDirection.Descending
            ? OrphanSortDirection.Ascending
            : OrphanSortDirection.Descending;
    }

    partial void OnSortKeyChanged(OrphanSortKey value) => ApplySort();

    partial void OnSortDirectionChanged(OrphanSortDirection value)
    {
        OnPropertyChanged(nameof(SortDirectionLabel));
        OnPropertyChanged(nameof(SortDirectionTooltip));
        ApplySort();
    }

    partial void OnFilterModeChanged(OrphanFilterMode value) => ApplyFilter();

    /// <summary>Re-orders every group's Rows in place per the current sort settings.</summary>
    private void ApplySort()
    {
        foreach (var g in Groups)
        {
            var sorted = SortRows(g.Rows, SortKey, SortDirection).ToList();
            // Detach to avoid double RecomputeSelection/Totals during the rebuild;
            // OrphanRow.PropertyChanged subscriptions persist (same instances).
            g.Rows.CollectionChanged -= OnGroupRowsChanged;
            g.Rows.Clear();
            foreach (var r in sorted) g.Rows.Add(r);
            g.Rows.CollectionChanged += OnGroupRowsChanged;
            // Re-apply the filter to the rebuilt view (Clear/Add invalidates it).
            g.RowsView.Filter = RowMatchesFilter;
        }
    }

    /// <summary>Re-applies the current <see cref="FilterMode"/> to every group's view.</summary>
    private void ApplyFilter()
    {
        foreach (var g in Groups)
        {
            g.RowsView.Filter = RowMatchesFilter;
            g.RowsView.Refresh();
            g.RecomputeFilteredStats();
        }
    }

    /// <summary>Filter predicate consumed by <see cref="ICollectionView.Filter"/>.</summary>
    private bool RowMatchesFilter(object o) => o is OrphanRow r && IsRowVisible(r, FilterMode);

    /// <summary>
    /// Score-band filter: each mode either passes everything, or thresholds against
    /// the row's <see cref="OrphanRow.Score"/>. Critical-only inverts the test.
    /// </summary>
    private static bool IsRowVisible(OrphanRow r, OrphanFilterMode mode) => mode switch
    {
        OrphanFilterMode.All          => true,
        OrphanFilterMode.SafeOnly     => r.Score >= 80,  // Aucun
        OrphanFilterMode.SafeAndLow   => r.Score >= 60,  // Aucun + Faible
        OrphanFilterMode.NonCritical  => r.Score >= 20,  // tout sauf Critique
        OrphanFilterMode.CriticalOnly => r.Score < 20,
        _ => true,
    };

    /// <summary>
    /// Multi-key sort. Each key picks the relevant ordering value (size, score, risk
    /// numeric) and applies the requested direction. Tie-breakers fall through to
    /// size desc so the within-bucket order is stable across modes.
    /// </summary>
    private static IEnumerable<OrphanRow> SortRows(
        IEnumerable<OrphanRow> rows, OrphanSortKey key, OrphanSortDirection direction)
    {
        bool desc = direction == OrphanSortDirection.Descending;
        return key switch
        {
            OrphanSortKey.Score =>
                (desc
                    ? rows.OrderByDescending(r => r.Score)
                    : rows.OrderBy(r => r.Score))
                .ThenByDescending(r => r.SizeBytes),
            OrphanSortKey.Risk =>
                (desc
                    ? rows.OrderByDescending(r => (int)r.Risk)
                    : rows.OrderBy(r => (int)r.Risk))
                .ThenByDescending(r => r.SizeBytes),
            _ /* Size */ =>
                desc
                    ? rows.OrderByDescending(r => r.SizeBytes)
                    : rows.OrderBy(r => r.SizeBytes),
        };
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

    /// <summary>
    /// Phase 10-06 — "Pourquoi ?" button on AppData rows. Opens the
    /// <c>OrphanDiagnosticsWindow</c> bound to a fresh
    /// <see cref="OrphanDiagnosticsViewModel"/> wrapping the row's
    /// <see cref="AppDataOrphanCandidate"/>. No-op for non-AppData rows
    /// (Diagnostics is null) — XAML hides the button via <c>HasDiagnostics</c>
    /// so this guard is purely defensive.
    /// </summary>
    [RelayCommand]
    private void ShowDiagnostics(OrphanRow? row)
    {
        if (row?.Diagnostics is null) return;

        var vm = new OrphanDiagnosticsViewModel(row.Diagnostics);
        var window = new Views.OrphanDiagnosticsWindow
        {
            DataContext = vm,
            Owner = Application.Current?.MainWindow,
        };
        window.ShowDialog();
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

    /// <summary>
    /// Phase 10 (post-launch tweak) — filtered + sorted view of <see cref="Rows"/>
    /// bound by the XAML ItemsControl. The parent <see cref="OrphansViewModel"/>
    /// installs the predicate so categories share filter logic.
    /// </summary>
    public ICollectionView RowsView { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private long _visibleBytes;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _areAllSelected;

    [ObservableProperty]
    private System.Windows.Media.SolidColorBrush _accentBrush;

    public string TotalBytesDisplay => FormatBytes(TotalBytes);
    public string CountLabel => VisibleCount == Count
        ? $"{Count} élément{(Count > 1 ? "s" : "")}"
        : $"{VisibleCount} / {Count} élément{(Count > 1 ? "s" : "")} (filtré)";
    public string SelectedLabel =>
        SelectedCount == 0 ? string.Empty : $"  ({SelectedCount} sélectionné{(SelectedCount > 1 ? "s" : "")})";

    public OrphanCategoryGroup(OrphanCategory category, IEnumerable<OrphanRow> rows)
    {
        Category = category;
        Label = LabelFor(category);
        Rows = new ObservableCollection<OrphanRow>(rows);
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        AccentBrush = BrushFor(category);
        RecomputeStats();
    }

    public void RecomputeStats()
    {
        Count = Rows.Count;
        TotalBytes = Rows.Sum(r => r.SizeBytes);
        RecomputeFilteredStats();
        OnPropertyChanged(nameof(TotalBytesDisplay));
    }

    /// <summary>Recompute counts that depend on the current filter (visible only).</summary>
    public void RecomputeFilteredStats()
    {
        int visible = 0;
        long visibleBytes = 0;
        foreach (var r in Rows)
        {
            if (RowsView.Filter is null || RowsView.Filter(r))
            {
                visible++;
                visibleBytes += r.SizeBytes;
            }
        }
        VisibleCount = visible;
        VisibleBytes = visibleBytes;
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

        // Phase 10 (post-launch tweak): every Rémanents row gets a score + risk band,
        // not just AppData orphans. AppData rows use the pipeline's verdict;
        // every other category uses a category-default heuristic so the user
        // sees a uniform colour-coded badge across all tabs.
        if (Diagnostics is not null)
        {
            Score = Diagnostics.ConfidenceScore;
            Risk = Diagnostics.Risk;
            RecommendedAction = Diagnostics.Action;
        }
        else
        {
            var defaults = DefaultsFor(c.Category);
            Score = defaults.Score;
            Risk = defaults.Risk;
            RecommendedAction = defaults.Action;
        }

        // ScoreBand is the risk derived from the RAW score (ignores MinRiskFloor).
        // Badge colour binds to ScoreBand → same score = same colour, always.
        // Risk (above) is the FINAL clamped value — drives the recommended action.
        ScoreBand = BandFromScore(Score);

        // True only for AppData rows where a PathRule with MinRiskFloor pushed the
        // final risk above the score band. UI shows a 🛡 prefix in the badge so the
        // user knows the safety floor overrode the score.
        HasFloorApplied = Diagnostics is not null && (int)Risk > (int)ScoreBand;
    }

    public string FullPath { get; }
    public long SizeBytes { get; }
    public string Reason { get; }
    public OrphanCategory Category { get; }

    /// <summary>
    /// Phase 10 rich diagnostics — populated only for AppData rows by
    /// <c>AppDataOrphanPipeline</c>. Null for every other category; the row
    /// still gets a synthesized <see cref="Score"/> + <see cref="Risk"/>
    /// from <see cref="DefaultsFor"/>.
    /// </summary>
    public AppDataOrphanCandidate? Diagnostics { get; }

    /// <summary>True only for AppData rows; gates the "Pourquoi ?" modal button.</summary>
    public bool HasDiagnostics => Diagnostics is not null;

    /// <summary>0-100 confidence score — always present (synthesized for non-AppData).</summary>
    public int Score { get; }

    /// <summary>Final risk band — clamped by MinRiskFloor for AppData; raw default elsewhere.</summary>
    public RiskLevel Risk { get; }

    /// <summary>Recommended user action — always present.</summary>
    public RecommendedAction RecommendedAction { get; }

    /// <summary>
    /// Risk band derived purely from <see cref="Score"/> ignoring any safety floor.
    /// This is what the badge colour binds to — guarantees same-score = same-colour
    /// regardless of category-specific floor rules.
    /// </summary>
    public RiskLevel ScoreBand { get; }

    /// <summary>True when MinRiskFloor pushed final Risk above ScoreBand. Drives the 🛡 marker.</summary>
    public bool HasFloorApplied { get; }

    /// <summary>Badge text — score with optional 🛡 prefix when a safety floor applied.</summary>
    public string ScoreBadgeText
    {
        get
        {
            var s = Score.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return HasFloorApplied ? "🛡 " + s : s;
        }
    }

    /// <summary>Tooltip line shown when the safety floor lifted the final risk.</summary>
    public string? FloorNotice => HasFloorApplied
        ? $"🛡 Plancher de sûreté actif — score brut → {ScoreBand}, mais une règle force {Risk}."
        : null;

    /// <summary>
    /// Category-default score + risk + action heuristics for non-AppData rows.
    /// Conservative: only categories where the orphan detector is highly
    /// confident get a green/Aucun default; ambiguous ones (system artifacts,
    /// orphan installer patches) start at Moyen/VerifierAvant.
    /// </summary>
    private static (int Score, RiskLevel Risk, RecommendedAction Action) DefaultsFor(OrphanCategory c) => c switch
    {
        // Cleanup-tab categories (high confidence — these heuristics are deterministic).
        OrphanCategory.StaleTemp            => (90, RiskLevel.Aucun,  RecommendedAction.Supprimer),
        OrphanCategory.BrowserCache         => (90, RiskLevel.Aucun,  RecommendedAction.Supprimer),
        OrphanCategory.DevCache             => (85, RiskLevel.Aucun,  RecommendedAction.Supprimer),
        OrphanCategory.EmptyFolder          => (95, RiskLevel.Aucun,  RecommendedAction.Supprimer),
        OrphanCategory.BrokenShortcut       => (95, RiskLevel.Aucun,  RecommendedAction.Supprimer),
        // Borderline — uninstaller may have left these but the user might still
        // need them for repair / re-install flows.
        OrphanCategory.OrphanInstallerPatch => (65, RiskLevel.Faible, RecommendedAction.CorbeilleOk),
        OrphanCategory.EmptyProgramFiles    => (75, RiskLevel.Faible, RecommendedAction.CorbeilleOk),
        // System artifacts (hiberfil/pagefile/Windows.old/$Recycle.Bin/Prefetch/CrashDumps)
        // have specific OS roles — never blanket-suppress.
        OrphanCategory.SystemArtifact       => (50, RiskLevel.Moyen,  RecommendedAction.VerifierAvant),
        // Defensive fallback (also covers AppDataOrphan when Diagnostics unexpectedly null).
        _                                   => (50, RiskLevel.Moyen,  RecommendedAction.VerifierAvant),
    };

    /// <summary>RiskLevel band from a raw score (mirrors <c>RiskLevelClassifier</c> bands, no floor).</summary>
    private static RiskLevel BandFromScore(int score) => score switch
    {
        >= 80 => RiskLevel.Aucun,
        >= 60 => RiskLevel.Faible,
        >= 40 => RiskLevel.Moyen,
        >= 20 => RiskLevel.Eleve,
        _     => RiskLevel.Critique,
    };

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

/// <summary>
/// Phase 10 — sort key for the Rémanents tab rows. Combined with
/// <see cref="System.ComponentModel.OrphanSortDirection"/> to control row ordering.
/// </summary>
public enum OrphanSortKey
{
    /// <summary>Default — by row size in bytes.</summary>
    Size,
    /// <summary>By confidence score 0-100 (higher = more likely true residue).</summary>
    Score,
    /// <summary>By risk band (Critique > Eleve > Moyen > Faible > Aucun).</summary>
    Risk,
}

/// <summary>
/// Phase 10 — risk-based filter for the Rémanents tab. Each mode either passes
/// every row or thresholds against <see cref="OrphanRow.Score"/>.
/// </summary>
public enum OrphanFilterMode
{
    /// <summary>No filter — show every row.</summary>
    All,
    /// <summary>Only score ≥ 80 (Aucun) — safe-to-delete only.</summary>
    SafeOnly,
    /// <summary>Score ≥ 60 — Aucun + Faible (corbeille OK).</summary>
    SafeAndLow,
    /// <summary>Score ≥ 20 — everything except Critique.</summary>
    NonCritical,
    /// <summary>Score &lt; 20 — Critique only (audit mode).</summary>
    CriticalOnly,
}

/// <summary>Phase 10 — sort direction for the Rémanents tab.</summary>
public enum OrphanSortDirection
{
    /// <summary>Largest / highest / most-risky first.</summary>
    Descending,
    /// <summary>Smallest / lowest / least-risky first.</summary>
    Ascending,
}
