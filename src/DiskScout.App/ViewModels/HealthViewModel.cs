using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;
using DiskScout.Services;

namespace DiskScout.ViewModels;

public sealed partial class HealthViewModel : ObservableObject
{
    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir le bilan de santé du disque.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _healthScore;

    [ObservableProperty]
    private string _healthGrade = "—";

    [ObservableProperty]
    private string _healthLabel = "";

    [ObservableProperty]
    private SolidColorBrush _gradeBrush = new(Color.FromRgb(0x7F, 0x8C, 0x8D));

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string _driveCountDisplay = "";

    [ObservableProperty]
    private string _totalScannedDisplay = "";

    [ObservableProperty]
    private long _remnantsBytes;

    [ObservableProperty]
    private long _cleanupBytes;

    [ObservableProperty]
    private long _duplicatesBytes;

    [ObservableProperty]
    private long _oldFilesBytes;

    [ObservableProperty]
    private long _potentialRecoveryBytes;

    public ObservableCollection<DriveHealthRow> Drives { get; } = new();
    public ObservableCollection<HealthMetricCard> Metrics { get; } = new();
    public ObservableCollection<ExtensionSlice> TopExtensions { get; } = new();
    public ObservableCollection<FolderSlice> TopFolders { get; } = new();

    public void Load(
        ScanResult result,
        IReadOnlyList<DriveInfoSnapshot> driveSnapshots,
        long remnantsBytes,
        long cleanupBytes,
        long duplicatesBytes,
        long oldFilesBytes)
    {
        Drives.Clear();
        Metrics.Clear();
        TopExtensions.Clear();
        TopFolders.Clear();

        var scannedDrives = new HashSet<string>(
            result.ScannedDrives.Select(d => d.TrimEnd('\\').ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);

        long totalBytes = 0;
        int penalty = 0;
        int countEvaluated = 0;

        foreach (var d in driveSnapshots.Where(s => scannedDrives.Contains(s.RootPath.TrimEnd('\\').ToUpperInvariant()) || true))
        {
            if (!scannedDrives.Contains(d.RootPath.TrimEnd('\\').ToUpperInvariant())) continue;

            var usedPct = d.TotalSizeBytes == 0 ? 0.0 : 100.0 * (d.TotalSizeBytes - d.FreeSpaceBytes) / d.TotalSizeBytes;
            var freePct = 100.0 - usedPct;
            totalBytes += (d.TotalSizeBytes - d.FreeSpaceBytes);
            countEvaluated++;

            var color = freePct switch
            {
                < 5  => Color.FromRgb(0xE7, 0x4C, 0x3C), // red
                < 10 => Color.FromRgb(0xE6, 0x7E, 0x22), // orange
                < 20 => Color.FromRgb(0xF1, 0xC4, 0x0F), // yellow
                _    => Color.FromRgb(0x27, 0xAE, 0x60), // green
            };

            var status = freePct switch
            {
                < 5  => "Critique",
                < 10 => "Faible",
                < 20 => "Attention",
                _    => "OK",
            };

            if (freePct < 5) penalty += 30;
            else if (freePct < 10) penalty += 20;
            else if (freePct < 20) penalty += 10;

            Drives.Add(new DriveHealthRow(
                d.RootPath, d.Label, d.Format,
                d.TotalSizeBytes, d.FreeSpaceBytes,
                usedPct, status, new SolidColorBrush(color)));
        }

        // Metric-level penalties on absolute bytes
        if (remnantsBytes    > 5L  * 1024 * 1024 * 1024) penalty += 10;
        if (cleanupBytes     > 10L * 1024 * 1024 * 1024) penalty += 10;
        if (duplicatesBytes  > 5L  * 1024 * 1024 * 1024) penalty += 10;
        if (oldFilesBytes    > 50L * 1024 * 1024 * 1024) penalty += 5;

        var score = Math.Max(0, 100 - penalty);
        HealthScore = score;

        (HealthGrade, HealthLabel, var gradeColor) = score switch
        {
            >= 90 => ("A+", "Excellent",   Color.FromRgb(0x27, 0xAE, 0x60)),
            >= 80 => ("A",  "Bonne santé", Color.FromRgb(0x2E, 0xCC, 0x71)),
            >= 70 => ("B",  "Correct",     Color.FromRgb(0xF1, 0xC4, 0x0F)),
            >= 60 => ("C",  "À surveiller",Color.FromRgb(0xE6, 0x7E, 0x22)),
            _     => ("D",  "Critique",    Color.FromRgb(0xE7, 0x4C, 0x3C)),
        };
        GradeBrush = new SolidColorBrush(gradeColor);

        RemnantsBytes = remnantsBytes;
        CleanupBytes = cleanupBytes;
        DuplicatesBytes = duplicatesBytes;
        OldFilesBytes = oldFilesBytes;
        PotentialRecoveryBytes = remnantsBytes + cleanupBytes + duplicatesBytes;

        DriveCountDisplay = $"{countEvaluated} disque{(countEvaluated > 1 ? "s" : "")} analysé{(countEvaluated > 1 ? "s" : "")}";
        TotalScannedDisplay = FormatBytes(totalBytes);

        // Build metric cards
        Metrics.Add(BuildCard("Rémanents",      remnantsBytes,   5L  * 1024L * 1024L * 1024L));
        Metrics.Add(BuildCard("Nettoyage",      cleanupBytes,    10L * 1024L * 1024L * 1024L));
        Metrics.Add(BuildCard("Doublons",       duplicatesBytes, 5L  * 1024L * 1024L * 1024L));
        Metrics.Add(BuildCard("Vieux fichiers", oldFilesBytes,   50L * 1024L * 1024L * 1024L));

        Summary = BuildSummaryText(score, penalty, countEvaluated);

        // Top extensions (by bytes)
        var bytesByExt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in result.Nodes)
        {
            if (n.Kind != FileSystemNodeKind.File || n.SizeBytes <= 0) continue;
            var dot = n.Name.LastIndexOf('.');
            var ext = dot > 0 && dot < n.Name.Length - 1
                ? n.Name[(dot + 1)..].ToLowerInvariant()
                : "(sans ext)";
            if (ext.Length > 10) ext = "(long)";
            bytesByExt[ext] = bytesByExt.TryGetValue(ext, out var b) ? b + n.SizeBytes : n.SizeBytes;
        }
        var totalFileBytes = bytesByExt.Values.Sum();
        foreach (var (ext, bytes) in bytesByExt.OrderByDescending(kv => kv.Value).Take(8))
        {
            var pct = totalFileBytes == 0 ? 0 : 100.0 * bytes / totalFileBytes;
            TopExtensions.Add(new ExtensionSlice(ext, bytes, pct));
        }

        // Top root folders by size (depth == 1 directories)
        var rootFolders = result.Nodes
            .Where(n => n.Kind == FileSystemNodeKind.Directory && n.Depth == 1)
            .OrderByDescending(n => n.SizeBytes)
            .Take(6)
            .ToList();
        var maxRoot = rootFolders.FirstOrDefault()?.SizeBytes ?? 1;
        foreach (var r in rootFolders)
        {
            var share = maxRoot == 0 ? 0 : 100.0 * r.SizeBytes / maxRoot;
            TopFolders.Add(new FolderSlice(r.Name, r.FullPath, r.SizeBytes, share));
        }

        HasResults = Drives.Count > 0;
    }

    private static HealthMetricCard BuildCard(string label, long bytes, long redThreshold)
    {
        var ratio = redThreshold == 0 ? 0 : (double)bytes / redThreshold;
        var color = ratio switch
        {
            < 0.5 => Color.FromRgb(0x27, 0xAE, 0x60),
            < 0.8 => Color.FromRgb(0xF1, 0xC4, 0x0F),
            < 1.0 => Color.FromRgb(0xE6, 0x7E, 0x22),
            _     => Color.FromRgb(0xE7, 0x4C, 0x3C),
        };
        return new HealthMetricCard(label, FormatBytes(bytes), bytes, new SolidColorBrush(color));
    }

    private static string BuildSummaryText(int score, int penalty, int drives)
    {
        if (score >= 90) return "Disque en excellent état. Peu d'actions nécessaires.";
        if (score >= 80) return "Disque en bonne santé. Quelques optimisations possibles.";
        if (score >= 70) return "État correct. Consulte Nettoyage et Doublons pour libérer de l'espace.";
        if (score >= 60) return "À surveiller. Plusieurs sources de gaspillage détectées.";
        return "État critique. Libère de l'espace en priorité (Rémanents, Nettoyage, Doublons).";
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

public sealed record DriveHealthRow(
    string RootPath, string Label, string Format,
    long TotalSizeBytes, long FreeSpaceBytes,
    double UsedPercent, string Status,
    SolidColorBrush StatusBrush)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Label) ? RootPath : $"{RootPath.TrimEnd('\\')}  {Label}";

    public string UsedDisplay =>
        $"{Fmt(TotalSizeBytes - FreeSpaceBytes)} / {Fmt(TotalSizeBytes)}";

    public string FreeDisplay => $"{Fmt(FreeSpaceBytes)} libres";

    public double FreePercent => 100.0 - UsedPercent;

    public string UsedPercentDisplay => $"{UsedPercent:F1} %";

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

public sealed record HealthMetricCard(
    string Label,
    string ValueDisplay,
    long Bytes,
    SolidColorBrush Brush);

public sealed record ExtensionSlice(
    string Extension,
    long Bytes,
    double Percent)
{
    public string PercentDisplay => $"{Percent:F1} %";
    public string BytesDisplay
    {
        get
        {
            if (Bytes <= 0) return "0 o";
            string[] u = { "o", "Ko", "Mo", "Go", "To" };
            double v = Bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {u[i]}";
        }
    }
}

public sealed record FolderSlice(
    string Name,
    string FullPath,
    long Bytes,
    double SharePercent)
{
    public string SizeDisplay
    {
        get
        {
            if (Bytes <= 0) return "0 o";
            string[] u = { "o", "Ko", "Mo", "Go", "To" };
            double v = Bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {u[i]}";
        }
    }
}
