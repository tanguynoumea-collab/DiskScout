using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;

namespace DiskScout.ViewModels;

public sealed partial class ProgramsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir les programmes installés.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _count;

    public ObservableCollection<InstalledProgramRow> Rows { get; } = new();

    public ICollectionView View { get; }

    public ProgramsViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Rows);
        View.SortDescriptions.Add(new SortDescription(nameof(InstalledProgramRow.SizeBytes), ListSortDirection.Descending));
        View.Filter = FilterPredicate;
    }

    partial void OnSearchTextChanged(string value) => View.Refresh();

    public void Load(IEnumerable<InstalledProgram> programs)
    {
        Rows.Clear();
        foreach (var p in programs)
        {
            Rows.Add(new InstalledProgramRow(p));
        }
        Count = Rows.Count;
        HasResults = Rows.Count > 0;
        View.Refresh();
    }

    private bool FilterPredicate(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        if (obj is not InstalledProgramRow r) return false;
        return (r.DisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
            || (r.Publisher?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}

public sealed class InstalledProgramRow
{
    public InstalledProgramRow(InstalledProgram p)
    {
        DisplayName = p.DisplayName;
        Publisher = p.Publisher;
        Version = p.Version;
        InstallDate = p.InstallDate?.ToString("yyyy-MM-dd");
        InstallLocation = p.InstallLocation;
        SizeBytes = p.ComputedSizeBytes > 0 ? p.ComputedSizeBytes : p.RegistryEstimatedSizeBytes;
        Hive = p.Hive.ToString();
    }

    public string DisplayName { get; }
    public string? Publisher { get; }
    public string? Version { get; }
    public string? InstallDate { get; }
    public string? InstallLocation { get; }
    public long SizeBytes { get; }
    public string SizeDisplay => FormatBytes(SizeBytes);
    public string Hive { get; }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int unit = 0;
        while (v >= 1024 && unit < units.Length - 1) { v /= 1024; unit++; }
        return $"{v:F1} {units[unit]}";
    }
}
