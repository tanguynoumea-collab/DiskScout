using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;

namespace DiskScout.ViewModels;

public sealed partial class TreeViewModel : ObservableObject
{
    private const int MaxChildrenPerNode = 500;

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour explorer ton disque.";

    [ObservableProperty]
    private bool _hasResults;

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = new();

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        Roots.Clear();
        if (nodes.Count == 0)
        {
            HasResults = false;
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

        var rootNodes = nodes.Where(n => n.ParentId is null).OrderByDescending(n => n.SizeBytes).ToList();
        foreach (var root in rootNodes)
        {
            var rootSize = root.SizeBytes > 0 ? root.SizeBytes : 1;
            var vm = new TreeNodeViewModel(root, childrenByParent, rootSize);
            Roots.Add(vm);
        }
        HasResults = Roots.Count > 0;
    }
}

public sealed partial class TreeNodeViewModel : ObservableObject
{
    private const int MaxChildrenPerNode = 500;
    private static readonly TreeNodeViewModel LoadingPlaceholder = new();

    private readonly Dictionary<long, List<FileSystemNode>>? _index;
    private readonly long _rootSize;
    private bool _childrenLoaded;
    private bool _hasChildren;

    public FileSystemNode Node { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    private TreeNodeViewModel()
    {
        Node = new FileSystemNode(0, null, "Chargement...", "", FileSystemNodeKind.Directory, 0, 0, 0, DateTime.MinValue, false, 0);
        _index = null;
        _rootSize = 1;
    }

    public TreeNodeViewModel(FileSystemNode node, Dictionary<long, List<FileSystemNode>> index, long rootSize)
    {
        Node = node;
        _index = index;
        _rootSize = rootSize > 0 ? rootSize : 1;

        _hasChildren = index.ContainsKey(node.Id);
        if (_hasChildren)
        {
            Children.Add(LoadingPlaceholder);
        }
    }

    public string DisplayName => Node.Name;

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
                if (count >= MaxChildrenPerNode) break;
                Children.Add(new TreeNodeViewModel(child, _index, _rootSize));
                count++;
            }
        }
        _childrenLoaded = true;
    }
}
