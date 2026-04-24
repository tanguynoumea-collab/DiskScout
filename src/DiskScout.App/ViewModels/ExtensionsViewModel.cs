using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;

namespace DiskScout.ViewModels;

public sealed partial class ExtensionsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir le classement des extensions.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private long _totalBytes;

    public ObservableCollection<ExtensionRow> Rows { get; } = new();

    public ICollectionView View { get; }

    public ExtensionsViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Rows);
        View.SortDescriptions.Add(new SortDescription(nameof(ExtensionRow.Bytes), ListSortDirection.Descending));
    }

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        Rows.Clear();
        var bytesByExt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var countsByExt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var n in nodes)
        {
            if (n.Kind != FileSystemNodeKind.File || n.SizeBytes <= 0) continue;

            var dot = n.Name.LastIndexOf('.');
            var ext = dot > 0 && dot < n.Name.Length - 1
                ? n.Name[(dot + 1)..].ToLowerInvariant()
                : "(sans ext)";
            if (ext.Length > 10) ext = "(long)";

            bytesByExt[ext] = bytesByExt.TryGetValue(ext, out var b) ? b + n.SizeBytes : n.SizeBytes;
            countsByExt[ext] = countsByExt.TryGetValue(ext, out var c) ? c + 1 : 1;
            total += n.SizeBytes;
        }

        foreach (var kv in bytesByExt.OrderByDescending(x => x.Value))
        {
            var pct = total == 0 ? 0 : 100.0 * kv.Value / total;
            Rows.Add(new ExtensionRow(kv.Key, countsByExt[kv.Key], kv.Value, pct));
        }

        Count = Rows.Count;
        TotalBytes = total;
        HasResults = Rows.Count > 0;
        View.Refresh();
    }
}

public sealed class ExtensionRow
{
    public ExtensionRow(string extension, long files, long bytes, double percent)
    {
        Extension = extension;
        Files = files;
        Bytes = bytes;
        Percent = percent;
    }
    public string Extension { get; }
    public long Files { get; }
    public long Bytes { get; }
    public double Percent { get; }
    public string BytesDisplay => Fmt(Bytes);
    public string PercentDisplay => $"{Percent:F2} %";
    public string FilesDisplay => Files.ToString("n0");

    private static string Fmt(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] u = { "o", "Ko", "Mo", "Go", "To" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F1} {u[i]}";
    }
}
