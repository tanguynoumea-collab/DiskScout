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

public sealed partial class DuplicatesViewModel : ObservableObject
{
    private const long DefaultMinSize = 1 * 1024 * 1024; // 1 MB floor

    private readonly IFileDeletionService _deletion;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour chercher les doublons (nom + taille identiques).";

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

    public ObservableCollection<DuplicateRow> Rows { get; } = new();
    public ICollectionView View { get; }

    private IReadOnlyList<FileSystemNode>? _lastNodes;

    public DuplicatesViewModel(IFileDeletionService deletion, ILogger logger)
    {
        _deletion = deletion;
        _logger = logger;

        View = CollectionViewSource.GetDefaultView(Rows);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DuplicateRow.GroupLabel)));
        View.SortDescriptions.Add(new SortDescription(nameof(DuplicateRow.GroupWastedBytes), ListSortDirection.Descending));
    }

    partial void OnMinMbFilterChanged(double value)
    {
        if (_lastNodes is not null) Load(_lastNodes);
    }

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        _lastNodes = nodes;
        Rows.Clear();
        IsHashVerified = false;
        HashStatus = "Groupage par nom + taille. Clique « Vérifier par hash » pour éliminer les faux positifs.";

        long minBytes = (long)(Math.Max(0, MinMbFilter) * 1024 * 1024);
        if (minBytes <= 0) minBytes = DefaultMinSize;

        var groups = nodes
            .Where(n => n.Kind == FileSystemNodeKind.File && n.SizeBytes >= minBytes)
            .GroupBy(n => (n.Name.ToLowerInvariant(), n.SizeBytes))
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.First().SizeBytes * (g.Count() - 1));

        long wasted = 0;
        int groupCount = 0;

        foreach (var g in groups.Take(500))
        {
            var list = g.ToList();
            var sample = list[0];
            var groupWasted = sample.SizeBytes * (list.Count - 1);
            wasted += groupWasted;
            groupCount++;

            var label = $"{sample.Name} — {FormatBytes(sample.SizeBytes)} × {list.Count} copies  →  {FormatBytes(groupWasted)} gaspillés";

            foreach (var node in list)
            {
                Rows.Add(new DuplicateRow(node, label, groupWasted));
            }
        }

        GroupCount = groupCount;
        WastedBytes = wasted;
        HasResults = Rows.Count > 0;
        View.Refresh();
    }

    [RelayCommand]
    private async Task VerifyWithHashAsync()
    {
        if (IsHashRunning || Rows.Count == 0) return;
        IsHashRunning = true;
        HashStatus = "Vérification par hash en cours (xxHash3)…";

        var beforeGroups = GroupCount;
        var beforeWasted = WastedBytes;

        var buckets = Rows.GroupBy(r => r.GroupLabel).ToList();

        var verifiedRows = new List<DuplicateRow>();
        long verifiedWasted = 0;
        int verifiedGroups = 0;

        await Task.Run(() =>
        {
            foreach (var bucket in buckets)
            {
                var rows = bucket.ToList();
                if (rows.Count < 2) continue;

                // Partial hash first (fast, 64 KB)
                var byPartial = rows
                    .GroupBy(r => Helpers.FileHasher.ComputePartialHash(r.FullPath))
                    .Where(g => g.Key != 0 && g.Count() > 1);

                foreach (var partial in byPartial)
                {
                    // Full hash on the partial-hash collision group
                    var byFull = partial
                        .GroupBy(r => Helpers.FileHasher.ComputeFullHash(r.FullPath))
                        .Where(g => g.Key != 0 && g.Count() > 1);

                    foreach (var full in byFull)
                    {
                        var list = full.ToList();
                        var wasted = list[0].SizeBytes * (list.Count - 1);
                        verifiedWasted += wasted;
                        verifiedGroups++;

                        var newLabel = list[0].GroupLabel + "  ✓";
                        foreach (var r in list)
                        {
                            verifiedRows.Add(new DuplicateRow(
                                new FileSystemNode(0, null, r.Name, r.FullPath,
                                    FileSystemNodeKind.File, r.SizeBytes, 1, 0,
                                    r.LastModifiedUtc, false, 0),
                                newLabel, wasted));
                        }
                    }
                }
            }
        });

        Rows.Clear();
        foreach (var r in verifiedRows) Rows.Add(r);
        GroupCount = verifiedGroups;
        WastedBytes = verifiedWasted;
        HasResults = Rows.Count > 0;
        IsHashVerified = true;
        IsHashRunning = false;

        var removedGroups = Math.Max(0, beforeGroups - verifiedGroups);
        HashStatus = $"Vérifié par hash : {verifiedGroups} vrai{(verifiedGroups > 1 ? "s" : "")} doublon{(verifiedGroups > 1 ? "s" : "")} "
                   + $"({FormatBytes(verifiedWasted)} gaspillés). "
                   + $"{removedGroups} faux positif{(removedGroups > 1 ? "s" : "")} éliminé{(removedGroups > 1 ? "s" : "")} "
                   + $"(économie {FormatBytes(Math.Max(0, beforeWasted - verifiedWasted))} retirée des faux doublons).";
        View.Refresh();
    }

    [RelayCommand]
    private void CopyPath(DuplicateRow? row)
    {
        if (row is null) return;
        try { Clipboard.SetText(row.FullPath); } catch { }
    }

    [RelayCommand]
    private async Task DeleteAsync(DuplicateRow? row)
    {
        if (row is null) return;
        var summary = $"Supprimer ce doublon :{Environment.NewLine}{row.FullPath}{Environment.NewLine}Taille : {FormatBytes(row.SizeBytes)}";
        var (confirmed, permanent) = DeletePrompt.Ask(summary);
        if (!confirmed) return;

        var result = await _deletion.DeleteAsync(new[] { row.FullPath }, sendToRecycleBin: !permanent);
        DeletePrompt.ShowResult(result);

        if (result.SuccessCount > 0) Rows.Remove(row);
    }

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

public sealed class DuplicateRow
{
    public DuplicateRow(FileSystemNode node, string groupLabel, long groupWastedBytes)
    {
        FullPath = node.FullPath;
        Name = node.Name;
        SizeBytes = node.SizeBytes;
        LastModifiedUtc = node.LastModifiedUtc;
        GroupLabel = groupLabel;
        GroupWastedBytes = groupWastedBytes;
    }

    public string FullPath { get; }
    public string Name { get; }
    public long SizeBytes { get; }
    public DateTime LastModifiedUtc { get; }
    public string GroupLabel { get; }
    public long GroupWastedBytes { get; }

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
