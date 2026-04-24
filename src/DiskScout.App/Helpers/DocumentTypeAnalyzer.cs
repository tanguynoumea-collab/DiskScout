using DiskScout.Models;

namespace DiskScout.Helpers;

public static class DocumentTypeAnalyzer
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "gif", "bmp", "tif", "tiff", "webp",
        "heic", "heif", "svg", "ico", "raw", "cr2", "nef", "arw", "dng",
    };
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mkv", "mov", "avi", "webm", "flv", "wmv", "m4v",
        "mpg", "mpeg", "3gp", "m2ts", "ts",
    };
    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "rar", "tar", "xz", "gz", "bz2", "iso", "tgz", "cab",
    };

    public static DocumentCategory Classify(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return DocumentCategory.Other;

        var lastDot = fileName.LastIndexOf('.');
        if (lastDot < 0 || lastDot == fileName.Length - 1) return DocumentCategory.Other;

        var ext = fileName[(lastDot + 1)..];

        if (ext.Equals("pdf",  StringComparison.OrdinalIgnoreCase)) return DocumentCategory.Pdf;
        if (ext.Equals("xlsx", StringComparison.OrdinalIgnoreCase)) return DocumentCategory.Xlsx;
        if (ext.Equals("rvt",  StringComparison.OrdinalIgnoreCase)) return DocumentCategory.Rvt;
        if (ext.Equals("txt",  StringComparison.OrdinalIgnoreCase)) return DocumentCategory.Txt;
        if (ext.Equals("dll",  StringComparison.OrdinalIgnoreCase)) return DocumentCategory.Dll;
        if (ext.Equals("sys",  StringComparison.OrdinalIgnoreCase)) return DocumentCategory.Sys;
        if (ext.Equals("exe",  StringComparison.OrdinalIgnoreCase)) return DocumentCategory.Exe;

        if (ImageExts.Contains(ext))   return DocumentCategory.Images;
        if (VideoExts.Contains(ext))   return DocumentCategory.Videos;
        if (ArchiveExts.Contains(ext)) return DocumentCategory.Archives;

        return DocumentCategory.Other;
    }

    public static (Dictionary<long, DocumentTypeBreakdown> PerNode, DocumentTypeBreakdown Global) Analyze(
        IReadOnlyList<FileSystemNode> nodes,
        CancellationToken cancellationToken = default)
    {
        var fileBreakdownsByParent = new Dictionary<long, DocumentTypeBreakdown>();
        var global = DocumentTypeBreakdown.Empty;

        foreach (var n in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (n.Kind != FileSystemNodeKind.File || n.SizeBytes <= 0) continue;
            if (!n.ParentId.HasValue) continue;

            var category = Classify(n.Name);
            var current = fileBreakdownsByParent.TryGetValue(n.ParentId.Value, out var existing)
                ? existing
                : DocumentTypeBreakdown.Empty;
            fileBreakdownsByParent[n.ParentId.Value] = current.AddFile(category, n.SizeBytes);

            global = global.AddFile(category, n.SizeBytes);
        }

        var childrenByParent = BuildChildrenIndex(nodes);
        var result = new Dictionary<long, DocumentTypeBreakdown>(nodes.Count);

        foreach (var root in nodes.Where(n => n.ParentId is null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Aggregate(root.Id, childrenByParent, fileBreakdownsByParent, result);
        }

        return (result, global);
    }

    private static DocumentTypeBreakdown Aggregate(
        long nodeId,
        Dictionary<long, List<long>> childrenByParent,
        Dictionary<long, DocumentTypeBreakdown> filesOfDirectChildren,
        Dictionary<long, DocumentTypeBreakdown> result)
    {
        var total = filesOfDirectChildren.TryGetValue(nodeId, out var filesPart)
            ? filesPart
            : DocumentTypeBreakdown.Empty;

        if (childrenByParent.TryGetValue(nodeId, out var childIds))
        {
            foreach (var childId in childIds)
            {
                total = total.Add(Aggregate(childId, childrenByParent, filesOfDirectChildren, result));
            }
        }

        result[nodeId] = total;
        return total;
    }

    private static Dictionary<long, List<long>> BuildChildrenIndex(IReadOnlyList<FileSystemNode> nodes)
    {
        var dict = new Dictionary<long, List<long>>(nodes.Count / 4);
        foreach (var n in nodes)
        {
            if (n.Kind != FileSystemNodeKind.Directory && n.Kind != FileSystemNodeKind.Volume) continue;
            if (!n.ParentId.HasValue) continue;
            if (!dict.TryGetValue(n.ParentId.Value, out var list))
            {
                list = new List<long>();
                dict[n.ParentId.Value] = list;
            }
            list.Add(n.Id);
        }
        return dict;
    }
}
