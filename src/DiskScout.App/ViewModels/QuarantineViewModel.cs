using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Services;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class QuarantineViewModel : ObservableObject
{
    private readonly IQuarantineService _quarantine;
    private readonly ILogger _logger;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private string _emptyStateMessage = "La quarantaine est vide. Les fichiers supprimés via « Quarantaine » y atterriront pour 30 jours, restaurables à tout moment.";

    public ObservableCollection<QuarantineRow> Rows { get; } = new();
    public ICollectionView View { get; }

    public QuarantineViewModel(IQuarantineService quarantine, ILogger logger)
    {
        _quarantine = quarantine;
        _logger = logger;

        View = CollectionViewSource.GetDefaultView(Rows);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(QuarantineRow.SessionDisplay)));
        View.SortDescriptions.Add(new SortDescription(nameof(QuarantineRow.QuarantinedUtc), ListSortDirection.Descending));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var entries = await _quarantine.ListAsync();
        Rows.Clear();
        long total = 0;
        foreach (var e in entries.OrderByDescending(x => x.QuarantinedUtc))
        {
            Rows.Add(new QuarantineRow(e));
            total += e.BytesFreed;
        }
        Count = Rows.Count;
        TotalBytes = total;
        HasResults = Rows.Count > 0;
        View.Refresh();
    }

    [RelayCommand]
    private async Task RestoreAsync(QuarantineRow? row)
    {
        if (row is null) return;
        var result = await _quarantine.RestoreAsync(new[] { row.Entry });
        if (result.Count > 0)
        {
            Rows.Remove(row);
            Count = Rows.Count;
            TotalBytes -= row.BytesFreed;
            HasResults = Rows.Count > 0;
        }
        if (result.Failures.Count > 0)
        {
            var msg = string.Join("\n", result.Failures.Select(f => $"• {f.Path} — {f.Error}"));
            System.Windows.MessageBox.Show($"Échec restauration :\n{msg}",
                "DiskScout — quarantaine", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task PurgeExpiredAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "Purger définitivement toutes les entrées de quarantaine de plus de 30 jours ?\nCette action est IRRÉVERSIBLE.",
            "DiskScout — purger quarantaine",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.OK) return;

        var freed = await _quarantine.PurgeAsync(TimeSpan.FromDays(30));
        await RefreshAsync();
        System.Windows.MessageBox.Show(
            $"Purge : {Fmt(freed)} libérés définitivement.",
            "DiskScout", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

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

public sealed class QuarantineRow
{
    public QuarantineRow(QuarantineEntry entry)
    {
        Entry = entry;
        OriginalPath = entry.OriginalPath;
        BytesFreed = entry.BytesFreed;
        QuarantinedUtc = entry.QuarantinedUtc;
        AgeDays = entry.AgeDays;
        SessionId = entry.SessionId;
    }

    public QuarantineEntry Entry { get; }
    public string OriginalPath { get; }
    public long BytesFreed { get; }
    public DateTime QuarantinedUtc { get; }
    public int AgeDays { get; }
    public string SessionId { get; }

    public string SessionDisplay =>
        $"Session du {QuarantinedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ({AgeDays} j)";

    public string SizeDisplay
    {
        get
        {
            if (BytesFreed <= 0) return "—";
            string[] u = { "o", "Ko", "Mo", "Go", "To" };
            double v = BytesFreed;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {u[i]}";
        }
    }

    public string RetentionDisplay =>
        AgeDays < 30 ? $"Expire dans {30 - AgeDays} j" : "Expirée (à purger)";
}
