using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;
using Serilog;

namespace DiskScout.ViewModels;

public sealed partial class TreeViewModel : ObservableObject
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;

    private readonly IFileDeletionService _deletion;
    private readonly ILogger _logger;

    private Dictionary<long, List<FileSystemNode>> _childrenByParent = new();
    private IReadOnlyList<FileSystemNode> _rootNodes = Array.Empty<FileSystemNode>();
    private Dictionary<long, DocumentTypeBreakdown> _breakdownByNode = new();

    [ObservableProperty]
    private string _emptyStateMessage =
        "Aucun scan effectué. Lance un scan pour explorer ton disque.";

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private double _minSizeGb;

    [ObservableProperty]
    private int _visibleRootCount;

    [ObservableProperty]
    private bool _isDocumentAnalysisEnabled;

    [ObservableProperty]
    private DocumentTypeBreakdown _globalBreakdown = DocumentTypeBreakdown.Empty;

    public ObservableCollection<TreeNodeViewModel> Roots { get; } = new();

    public long MinSizeBytes => (long)Math.Max(0, MinSizeGb * BytesPerGb);

    public TreeViewModel(IFileDeletionService deletion, ILogger logger)
    {
        _deletion = deletion;
        _logger = logger;
    }

    partial void OnMinSizeGbChanged(double value) => RebuildRoots();

    public void Load(IReadOnlyList<FileSystemNode> nodes)
    {
        Roots.Clear();
        _breakdownByNode = new Dictionary<long, DocumentTypeBreakdown>();
        GlobalBreakdown = DocumentTypeBreakdown.Empty;
        IsDocumentAnalysisEnabled = false;

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

    [RelayCommand]
    private void AnalyzeDocumentTypes()
    {
        if (_rootNodes.Count == 0) return;
        var allNodes = _childrenByParent.Values.SelectMany(list => list).Concat(_rootNodes).ToList();
        var (perNode, global) = DocumentTypeAnalyzer.Analyze(allNodes);
        _breakdownByNode = perNode;
        GlobalBreakdown = global;
        IsDocumentAnalysisEnabled = true;
        RebuildRoots();
    }

    [RelayCommand]
    private void ClearDocumentAnalysis()
    {
        IsDocumentAnalysisEnabled = false;
        _breakdownByNode = new Dictionary<long, DocumentTypeBreakdown>();
        GlobalBreakdown = DocumentTypeBreakdown.Empty;
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
            var rootSize = root.SizeBytes > 0 ? root.SizeBytes : 1;
            var vm = new TreeNodeViewModel(root, _childrenByParent, rootSize, threshold,
                _breakdownByNode, IsDocumentAnalysisEnabled, _deletion, _logger);
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
    private readonly Dictionary<long, DocumentTypeBreakdown>? _breakdownByNode;
    private readonly bool _docAnalysisEnabled;
    private readonly IFileDeletionService? _deletion;
    private readonly ILogger? _logger;
    private bool _childrenLoaded;

    public FileSystemNode Node { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isDeleted;

    private TreeNodeViewModel()
    {
        Node = new FileSystemNode(0, null, "Chargement...", "", FileSystemNodeKind.Directory, 0, 0, 0, DateTime.MinValue, false, 0);
        _index = null;
        _rootSize = 1;
        _minSizeBytes = 0;
        _breakdownByNode = null;
        _docAnalysisEnabled = false;
        _deletion = null;
        _logger = null;
    }

    public TreeNodeViewModel(
        FileSystemNode node,
        Dictionary<long, List<FileSystemNode>> index,
        long rootSize,
        long minSizeBytes,
        Dictionary<long, DocumentTypeBreakdown> breakdownByNode,
        bool docAnalysisEnabled,
        IFileDeletionService? deletion,
        ILogger? logger)
    {
        Node = node;
        _index = index;
        _rootSize = rootSize > 0 ? rootSize : 1;
        _minSizeBytes = minSizeBytes;
        _breakdownByNode = breakdownByNode;
        _docAnalysisEnabled = docAnalysisEnabled;
        _deletion = deletion;
        _logger = logger;

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
            var deleted = IsDeleted ? "  [supprimé]" : string.Empty;
            return Node.Name + suffix + deleted;
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

    public bool ShowDocumentBreakdown =>
        _docAnalysisEnabled && _breakdownByNode is not null && _breakdownByNode.TryGetValue(Node.Id, out var b) && b.TotalBytes > 0;

    public DocumentTypeBreakdown Breakdown =>
        _breakdownByNode is not null && _breakdownByNode.TryGetValue(Node.Id, out var b)
            ? b
            : DocumentTypeBreakdown.Empty;

    public double PdfPercent      => Breakdown.PdfPercent;
    public double XlsxPercent     => Breakdown.XlsxPercent;
    public double RvtPercent      => Breakdown.RvtPercent;
    public double TxtPercent      => Breakdown.TxtPercent;
    public double DllPercent      => Breakdown.DllPercent;
    public double SysPercent      => Breakdown.SysPercent;
    public double ExePercent      => Breakdown.ExePercent;
    public double ImagesPercent   => Breakdown.ImagesPercent;
    public double VideosPercent   => Breakdown.VideosPercent;
    public double ArchivesPercent => Breakdown.ArchivesPercent;
    public double OtherPercent    => Breakdown.OtherPercent;

    [RelayCommand]
    private void CopyPath()
    {
        if (string.IsNullOrEmpty(Node.FullPath)) return;
        try { Clipboard.SetText(Node.FullPath); } catch { /* clipboard busy */ }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_deletion is null || string.IsNullOrEmpty(Node.FullPath) || IsDeleted) return;

        var kindLabel = Node.Kind switch
        {
            FileSystemNodeKind.Directory => "dossier",
            FileSystemNodeKind.Volume => "volume",
            FileSystemNodeKind.ReparsePoint => "point de reparse",
            _ => "fichier",
        };

        if (Node.Kind == FileSystemNodeKind.Volume)
        {
            MessageBox.Show(
                "Impossible de supprimer un volume racine.",
                "DiskScout",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var summary = $"Supprimer ce {kindLabel} :{Environment.NewLine}{Node.FullPath}{Environment.NewLine}Taille : {DeletePrompt.FormatBytes(Node.SizeBytes)}";
        var (confirmed, permanent) = DeletePrompt.Ask(summary);
        if (!confirmed) return;

        var result = await _deletion.DeleteAsync(new[] { Node.FullPath }, sendToRecycleBin: !permanent);
        DeletePrompt.ShowResult(result);

        if (result.SuccessCount > 0)
        {
            IsDeleted = true;
            OnPropertyChanged(nameof(DisplayName));
            _logger?.Information("Tree node flagged as deleted: {Path}", Node.FullPath);
        }
    }

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
                Children.Add(new TreeNodeViewModel(child, _index, _rootSize, _minSizeBytes,
                    _breakdownByNode ?? new Dictionary<long, DocumentTypeBreakdown>(),
                    _docAnalysisEnabled, _deletion, _logger));
                count++;
            }
        }
        _childrenLoaded = true;
    }
}
