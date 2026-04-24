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

    public ObservableCollection<OrphanRow> Rows { get; } = new();

    public ICollectionView View { get; }

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

        View = CollectionViewSource.GetDefaultView(Rows);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OrphanRow.CategoryLabel)));
        View.SortDescriptions.Add(new SortDescription(nameof(OrphanRow.CategoryLabel), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(OrphanRow.SizeBytes), ListSortDirection.Descending));
    }

    public void Load(IEnumerable<OrphanCandidate> candidates)
    {
        Rows.Clear();
        long total = 0;
        foreach (var c in candidates)
        {
            if (!_acceptedCategories.Contains(c.Category)) continue;
            Rows.Add(new OrphanRow(c));
            total += c.SizeBytes;
        }
        Count = Rows.Count;
        TotalBytes = total;
        HasResults = Rows.Count > 0;
        View.Refresh();
    }

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
}

public sealed class OrphanRow
{
    public OrphanRow(OrphanCandidate c)
    {
        FullPath = c.FullPath;
        SizeBytes = c.SizeBytes;
        Reason = c.Reason;
        Category = c.Category;
    }

    public string FullPath { get; }
    public long SizeBytes { get; }
    public string Reason { get; }
    public OrphanCategory Category { get; }

    public string CategoryLabel => Category switch
    {
        OrphanCategory.AppDataOrphan => "AppData orphelins",
        OrphanCategory.EmptyProgramFiles => "Program Files vides",
        OrphanCategory.StaleTemp => "Fichiers Temp anciens",
        OrphanCategory.OrphanInstallerPatch => "Patches MSI orphelins",
        OrphanCategory.SystemArtifact => "Artefacts système",
        OrphanCategory.DevCache => "Caches de développement",
        OrphanCategory.BrowserCache => "Caches de navigateurs",
        _ => "Autres",
    };

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
