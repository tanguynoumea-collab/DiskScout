using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;

namespace DiskScout.ViewModels;

public sealed partial class OrphansViewModel : ObservableObject
{
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

    public OrphansViewModel()
    {
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
            Rows.Add(new OrphanRow(c));
            total += c.SizeBytes;
        }
        Count = Rows.Count;
        TotalBytes = total;
        HasResults = Rows.Count > 0;
        View.Refresh();
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
