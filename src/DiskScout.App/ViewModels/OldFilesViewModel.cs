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

public sealed partial class OldFilesViewModel : ObservableObject
{
    private readonly IFileDeletionService _deletion;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir les fichiers anciens.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private int _minAgeDays = 365;

    [ObservableProperty]
    private double _minMbFilter = 10.0;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private long _selectedBytes;

    public ObservableCollection<OldFilesGroup> Groups { get; } = new();

    private IReadOnlyList<FileSystemNode>? _lastNodes;

    public OldFilesViewModel(IFileDeletionService deletion, ILogger logger)
    {
        _deletion = deletion;
        _logger = logger;
    }

    partial void OnMinAgeDaysChanged(int value) { if (_lastNodes is not null) Load(_lastNodes); }
    partial void OnMinMbFilterChanged(double value) { if (_lastNodes is not null) Load(_lastNodes); }

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        _lastNodes = nodes;

        foreach (var g in Groups)
        {
            foreach (var r in g.Rows) r.PropertyChanged -= OnRowPropertyChanged;
            g.Rows.CollectionChanged -= OnGroupRowsChanged;
        }
        Groups.Clear();

        long minBytes = (long)(Math.Max(0, MinMbFilter) * 1024 * 1024);
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(0, MinAgeDays));

        var matching = nodes
            .Where(n => n.Kind == FileSystemNodeKind.File
                     && n.SizeBytes >= minBytes
                     && n.LastModifiedUtc != DateTime.MinValue
                     && n.LastModifiedUtc < cutoff)
            .OrderByDescending(n => n.SizeBytes)
            .Take(2000)
            .Select(n => new OldFileRow(n))
            .ToList();

        var byExt = matching
            .GroupBy(r => r.Extension)
            .OrderByDescending(g => g.Sum(r => r.SizeBytes));

        long total = 0;
        int count = 0;
        foreach (var ext in byExt)
        {
            var rows = ext.OrderByDescending(r => r.SizeBytes).ToList();
            var group = new OldFilesGroup(ext.Key, rows);
            foreach (var r in rows) { r.PropertyChanged += OnRowPropertyChanged; total += r.SizeBytes; count++; }
            group.Rows.CollectionChanged += OnGroupRowsChanged;
            Groups.Add(group);
        }

        Count = count;
        TotalBytes = total;
        SelectedCount = 0;
        SelectedBytes = 0;
        HasResults = Groups.Count > 0;
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OldFileRow.IsSelected)) return;
        RecomputeSelection();
    }

    private void OnGroupRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (OldFileRow r in e.OldItems) r.PropertyChanged -= OnRowPropertyChanged;
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
    private void ToggleGroupSelection(OldFilesGroup? group)
    {
        if (group is null) return;
        var v = !group.AreAllSelected;
        foreach (var r in group.Rows) r.IsSelected = v;
    }

    [RelayCommand] private void SelectAll()
        { foreach (var g in Groups) foreach (var r in g.Rows) r.IsSelected = true; }
    [RelayCommand] private void ClearSelection()
        { foreach (var g in Groups) foreach (var r in g.Rows) r.IsSelected = false; }

    [RelayCommand]
    private void GenerateAiAuditPrompt()
    {
        var items = Groups.SelectMany(g => g.Rows).Where(r => r.IsSelected)
            .Select(r =>
            {
                var ageLabel = r.AgeDays < 365 ? $"{r.AgeDays} jour(s)" : $"{r.AgeDays / 365.0:F1} an(s)";
                var reason = $"Non modifié depuis {ageLabel} (dernière modif {r.LastModifiedDisplay}, extension .{r.Extension})";
                return new AuditItem(r.FullPath, r.SizeBytes, reason);
            })
            .ToList();
        AuditPromptBuilder.BuildAndCopy(TabContexts.OldFiles, items);
    }

    [RelayCommand]
    private async Task PurgeSelectedAsync()
    {
        var selected = Groups.SelectMany(g => g.Rows).Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        var totalBytes = selected.Sum(r => r.SizeBytes);
        var summary =
            $"{selected.Count} fichier(s) ancien(s) sélectionné(s) — {DeletePrompt.FormatBytes(totalBytes)} à libérer." +
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

    private void RemoveRow(OldFileRow row)
    {
        foreach (var g in Groups)
        {
            if (g.Rows.Remove(row))
            {
                row.PropertyChanged -= OnRowPropertyChanged;
                g.RecomputeStats();
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
        Count = Groups.Sum(g => g.Rows.Count);
        TotalBytes = Groups.Sum(g => g.TotalBytes);
        HasResults = Groups.Count > 0;
        RecomputeSelection();
    }
}

public sealed partial class OldFilesGroup : ObservableObject
{
    public string Extension { get; }
    public ObservableCollection<OldFileRow> Rows { get; }

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

    public string TotalBytesDisplay => FormatBytes(TotalBytes);
    public string CountLabel => $"{Count} fichier{(Count > 1 ? "s" : "")}";
    public string SelectedLabel => SelectedCount == 0 ? string.Empty : $"  ({SelectedCount} sélectionné{(SelectedCount > 1 ? "s" : "")})";
    public string Header => $".{Extension}";

    public OldFilesGroup(string extension, IEnumerable<OldFileRow> rows)
    {
        Extension = extension;
        Rows = new ObservableCollection<OldFileRow>(rows);
        RecomputeStats();
    }

    public void RecomputeStats()
    {
        Count = Rows.Count;
        TotalBytes = Rows.Sum(r => r.SizeBytes);
        OnPropertyChanged(nameof(TotalBytesDisplay));
        OnPropertyChanged(nameof(CountLabel));
    }

    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(SelectedLabel));

    public void UpdateAllSelectedFlag()
        => AreAllSelected = Rows.Count > 0 && Rows.All(r => r.IsSelected);

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

public sealed partial class OldFileRow : ObservableObject
{
    public OldFileRow(FileSystemNode node)
    {
        FullPath = node.FullPath;
        Name = node.Name;
        SizeBytes = node.SizeBytes;
        LastModifiedUtc = node.LastModifiedUtc;
        var dot = Name.LastIndexOf('.');
        Extension = dot > 0 && dot < Name.Length - 1 ? Name[(dot + 1)..].ToLowerInvariant() : "(sans ext)";
        AgeDays = (int)Math.Max(0, (DateTime.UtcNow - LastModifiedUtc).TotalDays);
    }

    public string FullPath { get; }
    public string Name { get; }
    public long SizeBytes { get; }
    public DateTime LastModifiedUtc { get; }
    public string Extension { get; }
    public int AgeDays { get; }

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

    public string AgeDisplay =>
        AgeDays < 365 ? $"{AgeDays} j" : $"{AgeDays / 365.0:F1} ans";

    public string LastModifiedDisplay =>
        LastModifiedUtc == DateTime.MinValue ? "—" : LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd");
}
