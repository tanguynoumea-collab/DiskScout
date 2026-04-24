using System.Collections.ObjectModel;
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

public sealed partial class OldFilesViewModel : ObservableObject
{
    private readonly IFileDeletionService _deletion;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir les fichiers anciens (> N jours).";

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

    public ObservableCollection<OldFileRow> Rows { get; } = new();
    public ICollectionView View { get; }

    private IReadOnlyList<FileSystemNode>? _lastNodes;

    public OldFilesViewModel(IFileDeletionService deletion, ILogger logger)
    {
        _deletion = deletion;
        _logger = logger;

        View = CollectionViewSource.GetDefaultView(Rows);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OldFileRow.Extension)));
        View.SortDescriptions.Add(new SortDescription(nameof(OldFileRow.Extension), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(OldFileRow.SizeBytes), ListSortDirection.Descending));
    }

    partial void OnMinAgeDaysChanged(int value) { if (_lastNodes is not null) Load(_lastNodes); }
    partial void OnMinMbFilterChanged(double value) { if (_lastNodes is not null) Load(_lastNodes); }

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        _lastNodes = nodes;
        Rows.Clear();

        long minBytes = (long)(Math.Max(0, MinMbFilter) * 1024 * 1024);
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(0, MinAgeDays));
        long total = 0;

        var matching = nodes
            .Where(n => n.Kind == FileSystemNodeKind.File
                     && n.SizeBytes >= minBytes
                     && n.LastModifiedUtc != DateTime.MinValue
                     && n.LastModifiedUtc < cutoff)
            .OrderByDescending(n => n.SizeBytes)
            .Take(1000)
            .ToList();

        foreach (var n in matching)
        {
            Rows.Add(new OldFileRow(n));
            total += n.SizeBytes;
        }

        Count = Rows.Count;
        TotalBytes = total;
        HasResults = Rows.Count > 0;
        View.Refresh();
    }

    [RelayCommand]
    private void CopyPath(OldFileRow? row)
    {
        if (row is null) return;
        try { Clipboard.SetText(row.FullPath); } catch { }
    }

    [RelayCommand]
    private async Task DeleteAsync(OldFileRow? row)
    {
        if (row is null) return;
        var summary = $"Supprimer ce fichier ancien :{Environment.NewLine}{row.FullPath}{Environment.NewLine}Taille : {row.SizeDisplay}";
        var (confirmed, permanent) = DeletePrompt.Ask(summary);
        if (!confirmed) return;
        var result = await _deletion.DeleteAsync(new[] { row.FullPath }, sendToRecycleBin: !permanent);
        DeletePrompt.ShowResult(result);
        if (result.SuccessCount > 0) Rows.Remove(row);
    }
}

public sealed class OldFileRow
{
    public OldFileRow(FileSystemNode node)
    {
        FullPath = node.FullPath;
        Name = node.Name;
        SizeBytes = node.SizeBytes;
        LastModifiedUtc = node.LastModifiedUtc;
        var dot = Name.LastIndexOf('.');
        Extension = dot > 0 && dot < Name.Length - 1
            ? Name[(dot + 1)..].ToLowerInvariant()
            : "(sans ext)";
        AgeDays = (int)Math.Max(0, (DateTime.UtcNow - LastModifiedUtc).TotalDays);
    }
    public string FullPath { get; }
    public string Name { get; }
    public long SizeBytes { get; }
    public DateTime LastModifiedUtc { get; }
    public string Extension { get; }
    public int AgeDays { get; }

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
