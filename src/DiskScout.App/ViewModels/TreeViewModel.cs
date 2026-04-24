using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;

namespace DiskScout.ViewModels;

public sealed partial class TreeViewModel : ObservableObject
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;

    private Dictionary<long, List<FileSystemNode>> _childrenByParent = new();
    private IReadOnlyList<FileSystemNode> _rootNodes = Array.Empty<FileSystemNode>();

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour explorer ton disque.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private double _minSizeGb;

    [ObservableProperty]
    private int _visibleRootCount;

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = new();

    public long MinSizeBytes => (long)Math.Max(0, MinSizeGb * BytesPerGb);

    partial void OnMinSizeGbChanged(double value) => RebuildRoots();

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        Roots.Clear();
        if (nodes.Count == 0)
        {
            _childrenByParent = new();
            _rootNodes = Array.Empty<FileSystemNode>();
            HasResults = false;
            VisibleRootCount = 0;
            return;
        }

        var childrenByParent = new Dictionary<long, List<FileSystemNode>>(capacity: nodes.Count / 4);
        foreach (var n in nodes)
        {
            if (!n.ParentId.HasValue) continue;
            if (!childrenByParent.TryGetValue(n.ParentId.Value, out var list))
            {
                list = new List<FileSystemNode>();
                childrenByParent[n.ParentId.Value] = list;
            }
            list.Add(n);
        }
        foreach (var kv in childrenByParent)
        {
            kv.Value.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        }

        _childrenByParent = childrenByParent;
        _rootNodes = nodes.Where(n => n.ParentId is null).OrderByDescending(n => n.SizeBytes).ToList();

        RebuildRoots();
    }

    private void RebuildRoots()
    {
        Roots.Clear();
        if (_rootNodes.Count == 0)
        {
            HasResults = false;
            VisibleRootCount = 0;
            return;
        }

        var threshold = MinSizeBytes;
        foreach (var root in _rootNodes)
        {
            // Volume root always shown; children get filtered instead
            var rootSize = root.SizeBytes > 0 ? root.SizeBytes : 1;
            var vm = new TreeNodeViewModel(root, _childrenByParent, rootSize, threshold);
            Roots.Add(vm);
        }
        HasResults = Roots.Count > 0;
        VisibleRootCount = Roots.Count;
    }
}

public sealed partial class TreeNodeViewModel : ObservableObject
{
    private const int MaxChildrenPerNode = 500;
    private static readonly TreeNodeViewModel LoadingPlaceholder = new();

    private readonly Dictionary<long, List<FileSystemNode>>? _index;
    private readonly long _rootSize;
    private readonly long _minSizeBytes;
    private bool _childrenLoaded;

    public FileSystemNode Node { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    private TreeNodeViewModel()
    {
        Node = new FileSystemNode(0, null, "Chargement...", "", FileSystemNodeKind.Directory, 0, 0, 0, DateTime.MinValue, false, 0);
        _index = null;
        _rootSize = 1;
        _minSizeBytes = 0;
    }

    public TreeNodeViewModel(FileSystemNode node, Dictionary<long, List<FileSystemNode>> index, long rootSize, long minSizeBytes)
    {
        Node = node;
        _index = index;
        _rootSize = rootSize > 0 ? rootSize : 1;
        _minSizeBytes = minSizeBytes;

        if (index.TryGetValue(node.Id, out var kids) && HasVisibleChild(kids, minSizeBytes))
        {
            Children.Add(LoadingPlaceholder);
        }
    }

    private static bool HasVisibleChild(List<FileSystemNode> kids, long minSizeBytes)
    {
        if (minSizeBytes <= 0) return kids.Count > 0;
        foreach (var k in kids)
        {
            if (k.SizeBytes >= minSizeBytes) return true;
        }
        return false;
    }

    public string DisplayName
    {
        get
        {
            var suffix = IsCloudPath(Node.FullPath) ? "  [OneDrive/SharePoint]"
                       : Node.IsReparsePoint ? "  [Reparse]"
                       : string.Empty;
            return Node.Name + suffix;
        }
    }

    private static bool IsCloudPath(string path) =>
        path.Contains("OneDrive", StringComparison.OrdinalIgnoreCase)
        || path.Contains("SharePoint", StringComparison.OrdinalIgnoreCase);

    public string SizeDisplay
    {
        get
        {
            if (Node.SizeBytes <= 0) return "—";
            string[] u = { "o", "Ko", "Mo", "Go", "To" };
            double v = Node.SizeBytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:F1} {u[i]}";
        }
    }

    public double SharePercent => Math.Min(100.0, Math.Max(0.0, 100.0 * Node.SizeBytes / _rootSize));

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || _childrenLoaded || _index is null) return;
        Children.Clear();
        if (_index.TryGetValue(Node.Id, out var kids))
        {
            int count = 0;
            foreach (var child in kids)
            {
                if (_minSizeBytes > 0 && child.SizeBytes < _minSizeBytes) continue;
                if (count >= MaxChildrenPerNode) break;
                Children.Add(new TreeNodeViewModel(child, _index, _rootSize, _minSizeBytes));
                count++;
            }
        }
        _childrenLoaded = true;
    }
}
