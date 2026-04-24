using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskScout.Models;

namespace DiskScout.ViewModels;

public sealed partial class TreeViewModel : ObservableObject
{
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

        var childrenByParent = nodes
            .Where(n => n.ParentId.HasValue)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.SizeBytes).ToList());

        var roots = nodes.Where(n => n.ParentId is null).OrderByDescending(n => n.SizeBytes).ToList();
        foreach (var root in roots)
        {
            var vm = new TreeNodeViewModel(root, childrenByParent, root.SizeBytes > 0 ? root.SizeBytes : 1);
            Roots.Add(vm);
        }
        HasResults = Roots.Count > 0;
    }
}

public sealed partial class TreeNodeViewModel : ObservableObject
{
    private readonly Dictionary<long, List<FileSystemNode>> _index;
    private readonly long _rootSize;
    private bool _childrenLoaded;

    public FileSystemNode Node { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    public TreeNodeViewModel(FileSystemNode node, Dictionary<long, List<FileSystemNode>> index, long rootSize)
    {
        Node = node;
        _index = index;
        _rootSize = rootSize > 0 ? rootSize : 1;

        if (_index.TryGetValue(node.Id, out _))
        {
            Children.Add(null!); // placeholder for lazy expand
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
        if (!value || _childrenLoaded) return;
        Children.Clear();
        if (_index.TryGetValue(Node.Id, out var kids))
        {
            foreach (var child in kids.Take(500))
            {
                Children.Add(new TreeNodeViewModel(child, _index, _rootSize));
            }
        }
        _childrenLoaded = true;
    }
}
