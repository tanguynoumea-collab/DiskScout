using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class LargestFilesViewModel : ObservableObject
{
    private const int DefaultTopN = 200;

    private readonly IFileDeletionService _deletion;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir les plus gros fichiers.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _topN = DefaultTopN;

    public ObservableCollection<LargestFileRow> Rows { get; } = new();

    public ICollectionView View { get; }

    private IReadOnlyList<FileSystemNode>? _lastNodes;

    public LargestFilesViewModel(IFileDeletionService deletion, ILogger logger)
    {
        _deletion = deletion;
        _logger = logger;

        View = CollectionViewSource.GetDefaultView(Rows);
        View.SortDescriptions.Add(new SortDescription(nameof(LargestFileRow.SizeBytes), ListSortDirection.Descending));
        View.Filter = FilterPredicate;
    }

    partial void OnSearchTextChanged(string value) => View.Refresh();

    partial void OnTopNChanged(int value)
    {
        if (_lastNodes is not null) Load(_lastNodes);
    }

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        _lastNodes = nodes;
        Rows.Clear();

        long total = 0;
        var top = nodes
            .Where(n => n.Kind == FileSystemNodeKind.File && n.SizeBytes > 0)
            .OrderByDescending(n => n.SizeBytes)
            .Take(Math.Max(10, Math.Min(TopN, 2000)))
            .ToList();

        foreach (var n in top)
        {
            Rows.Add(new LargestFileRow(n));
            total += n.SizeBytes;
        }
        Count = Rows.Count;
        TotalBytes = total;
        HasResults = Rows.Count > 0;
        View.Refresh();
    }

    [RelayCommand]
    private void CopyPath(LargestFileRow? row)
    {
        if (row is null || string.IsNullOrEmpty(row.FullPath)) return;
        try { Clipboard.SetText(row.FullPath); } catch { /* clipboard busy */ }
    }

    [RelayCommand]
    private async Task DeleteAsync(LargestFileRow? row)
    {
        if (row is null) return;
        var summary = $"Supprimer :{Environment.NewLine}{row.FullPath}{Environment.NewLine}Taille : {DeletePrompt.FormatBytes(row.SizeBytes)}";
        var (confirmed, permanent) = DeletePrompt.Ask(summary);
        if (!confirmed) return;

        var result = await _deletion.DeleteAsync(new[] { row.FullPath }, sendToRecycleBin: !permanent);
        DeletePrompt.ShowResult(result);

        if (result.SuccessCount > 0)
        {
            Rows.Remove(row);
            Count = Rows.Count;
            TotalBytes -= result.TotalBytesFreed;
            HasResults = Rows.Count > 0;
            View.Refresh();
        }
    }

    private bool FilterPredicate(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        if (obj is not LargestFileRow r) return false;
        return (r.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (r.FullPath?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (r.Extension?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}

public sealed class LargestFileRow
{
    public LargestFileRow(FileSystemNode node)
    {
        Name = node.Name;
        FullPath = node.FullPath;
        SizeBytes = node.SizeBytes;
        LastModifiedUtc = node.LastModifiedUtc;
        var dot = node.Name.LastIndexOf('.');
        Extension = dot > 0 && dot < node.Name.Length - 1
            ? node.Name[(dot + 1)..].ToLowerInvariant()
            : "(sans ext)";
    }

    public string Name { get; }
    public string FullPath { get; }
    public long SizeBytes { get; }
    public DateTime LastModifiedUtc { get; }
    public string Extension { get; }

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
