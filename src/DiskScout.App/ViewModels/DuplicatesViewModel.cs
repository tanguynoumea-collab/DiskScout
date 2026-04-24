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

public sealed partial class DuplicatesViewModel : ObservableObject
{
    private const long DefaultMinSize = 1 * 1024 * 1024;

    private readonly IFileDeletionService _deletion;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour chercher les doublons.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _groupCount;

    [ObservableProperty]
    private long _wastedBytes;

    [ObservableProperty]
    private double _minMbFilter = 1.0;

    [ObservableProperty]
    private bool _isHashVerified;

    [ObservableProperty]
    private string _hashStatus = "Groupage par nom + taille. Clique « Vérifier par hash » pour éliminer les faux positifs.";

    [ObservableProperty]
    private bool _isHashRunning;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private long _selectedBytes;

    public ObservableCollection<DuplicateGroup> Groups { get; } = new();

    private IReadOnlyList<FileSystemNode>? _lastNodes;

    public DuplicatesViewModel(IFileDeletionService deletion, ILogger logger)
    {
        _deletion = deletion;
        _logger = logger;
    }

    partial void OnMinMbFilterChanged(double value)
    {
        if (_lastNodes is not null) Load(_lastNodes);
    }

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        _lastNodes = nodes;

        foreach (var g in Groups)
        {
            foreach (var r in g.Rows) r.PropertyChanged -= OnRowPropertyChanged;
            g.Rows.CollectionChanged -= OnGroupRowsChanged;
        }
        Groups.Clear();

        IsHashVerified = false;
        HashStatus = "Groupage par nom + taille. Clique « Vérifier par hash » pour éliminer les faux positifs.";

        long minBytes = (long)(Math.Max(0, MinMbFilter) * 1024 * 1024);
        if (minBytes <= 0) minBytes = DefaultMinSize;

        var byNameSize = nodes
            .Where(n => n.Kind == FileSystemNodeKind.File && n.SizeBytes >= minBytes)
            .GroupBy(n => (n.Name.ToLowerInvariant(), n.SizeBytes))
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.First().SizeBytes * (g.Count() - 1));

        long wasted = 0;
        int groupCount = 0;

        foreach (var g in byNameSize.Take(500))
        {
            var list = g.ToList();
            var sample = list[0];
            var groupWasted = sample.SizeBytes * (list.Count - 1);
            wasted += groupWasted;
            groupCount++;

            var rows = list.Select(n => new DuplicateRow(n, groupWasted)).ToList();
            var group = new DuplicateGroup(sample.Name, sample.SizeBytes, rows, groupWasted, hashVerified: false);
            foreach (var r in rows) r.PropertyChanged += OnRowPropertyChanged;
            group.Rows.CollectionChanged += OnGroupRowsChanged;
            Groups.Add(group);
        }

        GroupCount = groupCount;
        WastedBytes = wasted;
        SelectedCount = 0;
        SelectedBytes = 0;
        HasResults = Groups.Count > 0;
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DuplicateRow.IsSelected)) return;
        RecomputeSelection();
    }

    private void OnGroupRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DuplicateRow r in e.OldItems) r.PropertyChanged -= OnRowPropertyChanged;
        RecomputeSelection();
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

    [RelayCommand]
    private void ToggleGroupSelection(DuplicateGroup? group)
    {
        if (group is null) return;
        var v = !group.AreAllSelected;
        foreach (var r in group.Rows) r.IsSelected = v;
    }

    [RelayCommand] private void SelectAll()
    {
        foreach (var g in Groups) foreach (var r in g.Rows) r.IsSelected = true;
    }

    [RelayCommand] private void ClearSelection()
    {
        foreach (var g in Groups) foreach (var r in g.Rows) r.IsSelected = false;
    }

    [RelayCommand] private void GenerateAiAuditPrompt()
    {
        var items = Groups.SelectMany(g => g.Rows).Where(r => r.IsSelected)
            .Select(r =>
            {
                var group = Groups.FirstOrDefault(g => g.Rows.Contains(r));
                var groupSize = group?.Rows.Count ?? 0;
                var wasted = group?.WastedBytes ?? 0;
                var verified = group?.HashVerified == true ? " (hash xxHash3 vérifié ✓)" : " (groupage nom+taille seulement, non vérifié par hash)";
                var reason = $"Groupe de {groupSize} copies, {DeletePrompt.FormatBytes(wasted)} gaspillés si on garde 1 seule copie{verified}";
                return new AuditItem(r.FullPath, r.SizeBytes, reason);
            })
            .ToList();
        AuditPromptBuilder.BuildAndCopy(TabContexts.Duplicates, items);
    }

    private int GroupSizeFor(DuplicateRow r) => Groups.FirstOrDefault(g => g.Rows.Contains(r))?.Rows.Count ?? 0;
    private long GroupWastedFor(DuplicateRow r) => Groups.FirstOrDefault(g => g.Rows.Contains(r))?.WastedBytes ?? 0;

    [RelayCommand]
    private async Task PurgeSelectedAsync()
    {
        var selected = Groups.SelectMany(g => g.Rows).Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var totalBytes = selected.Sum(r => r.SizeBytes);
        var summary =
            $"{selected.Count} doublon(s) sélectionné(s) — {DeletePrompt.FormatBytes(totalBytes)} à libérer." +
            Environment.NewLine + Environment.NewLine +
            "Premiers éléments :" + Environment.NewLine +
            string.Join(Environment.NewLine, selected.Take(6).Select(r => $"  • {r.FullPath}")) +
            (selected.Count > 6 ? Environment.NewLine + $"  … et {selected.Count - 6} autre(s)" : string.Empty);

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

    private void RemoveRow(DuplicateRow row)
    {
        foreach (var g in Groups)
        {
            if (g.Rows.Remove(row))
            {
                row.PropertyChanged -= OnRowPropertyChanged;
                g.RecomputeWasted();
                break;
            }
        }
        for (int i = Groups.Count - 1; i >= 0; i--)
        {
            if (Groups[i].Rows.Count < 2)
            {
                Groups[i].Rows.CollectionChanged -= OnGroupRowsChanged;
                foreach (var r in Groups[i].Rows) r.PropertyChanged -= OnRowPropertyChanged;
                Groups.RemoveAt(i);
            }
        }
        GroupCount = Groups.Count;
        WastedBytes = Groups.Sum(g => g.WastedBytes);
        HasResults = Groups.Count > 0;
        RecomputeSelection();
    }

    [RelayCommand]
    private async Task VerifyWithHashAsync()
    {
        if (IsHashRunning || Groups.Count == 0) return;
        IsHashRunning = true;
        HashStatus = "Vérification par hash en cours (xxHash3)…";

        var beforeGroups = GroupCount;
        var beforeWasted = WastedBytes;

        // Copy state for background work
        var snapshot = Groups.Select(g => new { Group = g, Rows = g.Rows.ToList() }).ToList();

        var verifiedGroups = new List<(string Label, long SizeBytes, List<DuplicateRow> Rows, long Wasted)>();

        await Task.Run(() =>
        {
            foreach (var s in snapshot)
            {
                if (s.Rows.Count < 2) continue;
                var byPartial = s.Rows
                    .GroupBy(r => Helpers.FileHasher.ComputePartialHash(r.FullPath))
                    .Where(g => g.Key != 0 && g.Count() > 1);
                foreach (var partial in byPartial)
                {
                    var byFull = partial
                        .GroupBy(r => Helpers.FileHasher.ComputeFullHash(r.FullPath))
                        .Where(g => g.Key != 0 && g.Count() > 1);
                    foreach (var full in byFull)
                    {
                        var list = full.ToList();
                        var wasted = list[0].SizeBytes * (list.Count - 1);
                        verifiedGroups.Add((list[0].Name, list[0].SizeBytes, list, wasted));
                    }
                }
            }
        });

        // Rebuild Groups on UI thread
        foreach (var g in Groups)
        {
            foreach (var r in g.Rows) r.PropertyChanged -= OnRowPropertyChanged;
            g.Rows.CollectionChanged -= OnGroupRowsChanged;
        }
        Groups.Clear();

        long verifiedWasted = 0;
        foreach (var vg in verifiedGroups.OrderByDescending(x => x.Wasted))
        {
            var group = new DuplicateGroup(vg.Label, vg.SizeBytes, vg.Rows, vg.Wasted, hashVerified: true);
            foreach (var r in vg.Rows) r.PropertyChanged += OnRowPropertyChanged;
            group.Rows.CollectionChanged += OnGroupRowsChanged;
            Groups.Add(group);
            verifiedWasted += vg.Wasted;
        }

        GroupCount = Groups.Count;
        WastedBytes = verifiedWasted;
        HasResults = Groups.Count > 0;
        IsHashVerified = true;
        IsHashRunning = false;
        RecomputeSelection();

        var removedGroups = Math.Max(0, beforeGroups - Groups.Count);
        HashStatus = $"Vérifié ✓ : {Groups.Count} vrais doublons ({DeletePrompt.FormatBytes(verifiedWasted)} gaspillés). " +
                     $"{removedGroups} faux positifs éliminés.";
    }
}

public sealed partial class DuplicateGroup : ObservableObject
{
    public string Label { get; }
    public long SizeBytes { get; }
    public bool HashVerified { get; }

    [ObservableProperty]
    private long _wastedBytes;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _areAllSelected;

    public ObservableCollection<DuplicateRow> Rows { get; }

    public string Header => $"{Label} — {FormatBytes(SizeBytes)} × {Rows.Count} copies" + (HashVerified ? "  ✓" : string.Empty);
    public string CountLabel => $"{Rows.Count} copie{(Rows.Count > 1 ? "s" : "")}";
    public string WastedDisplay => FormatBytes(WastedBytes);
    public string SelectedLabel => SelectedCount == 0 ? string.Empty : $"  ({SelectedCount} sélectionné{(SelectedCount > 1 ? "s" : "")})";

    public DuplicateGroup(string label, long sizeBytes, IEnumerable<DuplicateRow> rows, long wastedBytes, bool hashVerified)
    {
        Label = label;
        SizeBytes = sizeBytes;
        Rows = new ObservableCollection<DuplicateRow>(rows);
        WastedBytes = wastedBytes;
        HashVerified = hashVerified;
    }

    public void RecomputeWasted()
    {
        WastedBytes = Rows.Count < 2 ? 0 : SizeBytes * (Rows.Count - 1);
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(WastedDisplay));
    }

    public void UpdateAllSelectedFlag()
        => AreAllSelected = Rows.Count > 0 && Rows.All(r => r.IsSelected);

    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(SelectedLabel));

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

public sealed partial class DuplicateRow : ObservableObject
{
    public DuplicateRow(FileSystemNode node, long groupWastedBytes)
    {
        FullPath = node.FullPath;
        Name = node.Name;
        SizeBytes = node.SizeBytes;
        LastModifiedUtc = node.LastModifiedUtc;
        GroupWastedBytes = groupWastedBytes;
    }

    public string FullPath { get; }
    public string Name { get; }
    public long SizeBytes { get; }
    public DateTime LastModifiedUtc { get; }
    public long GroupWastedBytes { get; }

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

    public string LastModifiedDisplay =>
        LastModifiedUtc == DateTime.MinValue ? "—" : LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd");
}
