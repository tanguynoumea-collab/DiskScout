using DiskScout.Models;

namespace DiskScout.Services;

public sealed class PathKeyedDeltaComparator : IDeltaComparator
{
    public DeltaResult Compare(ScanResult before, ScanResult after)
    {
        var beforeIndex = before.Nodes.ToDictionary(n => n.FullPath, n => n, StringComparer.OrdinalIgnoreCase);
        var afterIndex = after.Nodes.ToDictionary(n => n.FullPath, n => n, StringComparer.OrdinalIgnoreCase);

        var allPaths = new HashSet<string>(beforeIndex.Keys, StringComparer.OrdinalIgnoreCase);
        allPaths.UnionWith(afterIndex.Keys);

        var entries = new List<DeltaEntry>();
        foreach (var path in allPaths)
        {
            var hasBefore = beforeIndex.TryGetValue(path, out var b);
            var hasAfter = afterIndex.TryGetValue(path, out var a);

            if (hasBefore && hasAfter)
            {
                var delta = a!.SizeBytes - b!.SizeBytes;
                if (delta == 0) continue;
                var change = delta > 0 ? DeltaChange.Grew : DeltaChange.Shrank;
                var percent = b.SizeBytes == 0 ? (double?)null : delta / (double)b.SizeBytes;
                entries.Add(new DeltaEntry(path, change, b.SizeBytes, a.SizeBytes, delta, percent));
            }
            else if (hasAfter)
            {
                entries.Add(new DeltaEntry(path, DeltaChange.Added, null, a!.SizeBytes, a.SizeBytes, null));
            }
            else if (hasBefore)
            {
                entries.Add(new DeltaEntry(path, DeltaChange.Removed, b!.SizeBytes, null, -b.SizeBytes, null));
            }
        }

        return new DeltaResult(
            BeforeScanId: before.ScanId,
            AfterScanId: after.ScanId,
            BeforeCompletedUtc: before.CompletedUtc,
            AfterCompletedUtc: after.CompletedUtc,
            Entries: entries);
    }
}
