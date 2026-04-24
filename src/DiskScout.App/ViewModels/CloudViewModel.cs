using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;

namespace DiskScout.ViewModels;

public sealed partial class CloudViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucune racine OneDrive / SharePoint détectée. Lance un scan pour voir.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _totalPhysicalBytes;

    [ObservableProperty]
    private long _totalLogicalBytes;

    public ObservableCollection<CloudSyncRootRow> Rows { get; } = new();

    public void Load(IReadOnlyList<CloudSyncRoot> roots)
    {
        Rows.Clear();
        long physical = 0, logical = 0;
        foreach (var r in roots)
        {
            Rows.Add(new CloudSyncRootRow(r));
            physical += r.PhysicalBytes;
            logical += r.LogicalBytes;
        }
        Count = Rows.Count;
        TotalPhysicalBytes = physical;
        TotalLogicalBytes = logical;
        HasResults = Rows.Count > 0;
    }
}

public sealed class CloudSyncRootRow
{
    public CloudSyncRootRow(CloudSyncRoot r)
    {
        DisplayName = r.DisplayName;
        RootPath = r.RootPath;
        Provider = r.Provider.ToString();
        PhysicalBytes = r.PhysicalBytes;
        LogicalBytes = r.LogicalBytes;
        HydratedFileCount = r.HydratedFileCount;
        PlaceholderFileCount = r.PlaceholderFileCount;
        TotalFileCount = r.TotalFileCount;
        HydrationPercent = r.TotalFileCount == 0 ? 0 : 100.0 * r.HydratedFileCount / r.TotalFileCount;
        SavingsBytes = Math.Max(0, r.LogicalBytes - r.PhysicalBytes);
    }

    public string DisplayName { get; }
    public string RootPath { get; }
    public string Provider { get; }
    public long PhysicalBytes { get; }
    public long LogicalBytes { get; }
    public int HydratedFileCount { get; }
    public int PlaceholderFileCount { get; }
    public int TotalFileCount { get; }
    public double HydrationPercent { get; }
    public long SavingsBytes { get; }

    public string PhysicalDisplay => Fmt(PhysicalBytes);
    public string LogicalDisplay => Fmt(LogicalBytes);
    public string SavingsDisplay => Fmt(SavingsBytes);
    public string HydrationDisplay => $"{HydrationPercent:F1}% ({HydratedFileCount:n0} / {TotalFileCount:n0})";

    private static string Fmt(long bytes)
    {
        if (bytes <= 0) return "0 o";
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {u[i]}";
    }
}
