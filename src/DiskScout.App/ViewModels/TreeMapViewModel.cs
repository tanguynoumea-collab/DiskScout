using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;
using DiskScout.Views.Controls;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class TreeMapViewModel : ObservableObject
{
    private readonly IFileDeletionService _deletion;
    private readonly ILogger _logger;

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour voir la carte proportionnelle du disque.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private IReadOnlyList<FileSystemNode>? _nodes;

    [ObservableProperty]
    private IReadOnlyDictionary<long, List<FileSystemNode>>? _childrenIndex;

    [ObservableProperty]
    private long? _currentRootId;

    [ObservableProperty]
    private string _currentPath = "Racine";

    [ObservableProperty]
    private TreeMapColorMode _colorMode = TreeMapColorMode.Depth;

    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();

    public TreeMapViewModel(IFileDeletionService deletion, ILogger logger)
    {
        _deletion = deletion;
        _logger = logger;
    }

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            Nodes = null;
            ChildrenIndex = null;
            HasResults = false;
            Breadcrumbs.Clear();
            return;
        }

        var index = new Dictionary<long, List<FileSystemNode>>(nodes.Count / 4);
        foreach (var n in nodes)
        {
            if (!n.ParentId.HasValue) continue;
            if (!index.TryGetValue(n.ParentId.Value, out var list))
            {
                list = new List<FileSystemNode>();
                index[n.ParentId.Value] = list;
            }
            list.Add(n);
        }
        foreach (var kv in index)
        {
            kv.Value.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        }

        Nodes = nodes;
        ChildrenIndex = index;
        CurrentRootId = null;
        CurrentPath = "Racine";
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new BreadcrumbItem(null, "Racine"));
        HasResults = true;
    }

    public void DrillDown(FileSystemNode node)
    {
        if (ChildrenIndex is null || !ChildrenIndex.TryGetValue(node.Id, out var kids) || kids.Count == 0)
        {
            // Leaf: ignore
            return;
        }
        CurrentRootId = node.Id;
        CurrentPath = node.FullPath;
        Breadcrumbs.Add(new BreadcrumbItem(node.Id, node.Name));
    }

    [RelayCommand]
    private void NavigateTo(BreadcrumbItem? target)
    {
        if (target is null) return;
        while (Breadcrumbs.Count > 0 && Breadcrumbs[^1].NodeId != target.NodeId)
        {
            Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        }
        CurrentRootId = target.NodeId;
        if (target.NodeId is null)
        {
            CurrentPath = "Racine";
        }
        else if (Nodes is not null)
        {
            var node = Nodes.FirstOrDefault(n => n.Id == target.NodeId);
            CurrentPath = node?.FullPath ?? target.Label;
        }
    }

    [RelayCommand]
    private void GoUp()
    {
        if (Breadcrumbs.Count <= 1) return;
        Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        NavigateTo(Breadcrumbs[^1]);
    }

    [RelayCommand]
    private void SetColorMode(string? mode)
    {
        ColorMode = mode switch
        {
            "Age"  => TreeMapColorMode.Age,
            "Type" => TreeMapColorMode.Type,
            _      => TreeMapColorMode.Depth,
        };
    }

    public async Task DeleteNodeAsync(FileSystemNode node)
    {
        if (node.Kind == FileSystemNodeKind.Volume) return;

        var summary = $"Supprimer :{Environment.NewLine}{node.FullPath}{Environment.NewLine}Taille : {FormatBytes(node.SizeBytes)}";
        var mode = Helpers.DeletePrompt.Ask(summary);
        if (mode == DeleteMode.Cancelled) return;
        var result = await _deletion.DeleteAsync(new[] { node.FullPath }, mode);
        Helpers.DeletePrompt.ShowResult(result, mode);
    }

    public void CopyNodePath(FileSystemNode node)
    {
        try { Clipboard.SetText(node.FullPath); } catch { /* clipboard busy */ }
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

public sealed record BreadcrumbItem(long? NodeId, string Label);
