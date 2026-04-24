using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskScout.Models;

namespace DiskScout.Views.Controls;

/// <summary>
/// Squarified treemap — Bruls, Huijsmans &amp; van Wijk algorithm.
/// Renders a children layout for a given FileSystemNode with size-proportional rectangles
/// and color by depth / age / type. Click drills down, right-click raises NavigateUp.
/// </summary>
public class TreeMapControl : Canvas
{
    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(
            nameof(Nodes),
            typeof(IReadOnlyList<FileSystemNode>),
            typeof(TreeMapControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty ChildrenIndexProperty =
        DependencyProperty.Register(
            nameof(ChildrenIndex),
            typeof(IReadOnlyDictionary<long, List<FileSystemNode>>),
            typeof(TreeMapControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty CurrentRootIdProperty =
        DependencyProperty.Register(
            nameof(CurrentRootId),
            typeof(long?),
            typeof(TreeMapControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty ColorModeProperty =
        DependencyProperty.Register(
            nameof(ColorMode),
            typeof(TreeMapColorMode),
            typeof(TreeMapControl),
            new PropertyMetadata(TreeMapColorMode.Depth, OnDataChanged));

    public IReadOnlyList<FileSystemNode>? Nodes
    {
        get => (IReadOnlyList<FileSystemNode>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public IReadOnlyDictionary<long, List<FileSystemNode>>? ChildrenIndex
    {
        get => (IReadOnlyDictionary<long, List<FileSystemNode>>?)GetValue(ChildrenIndexProperty);
        set => SetValue(ChildrenIndexProperty, value);
    }

    public long? CurrentRootId
    {
        get => (long?)GetValue(CurrentRootIdProperty);
        set => SetValue(CurrentRootIdProperty, value);
    }

    public TreeMapColorMode ColorMode
    {
        get => (TreeMapColorMode)GetValue(ColorModeProperty);
        set => SetValue(ColorModeProperty, value);
    }

    public event EventHandler<FileSystemNode>? NodeClicked;
    public event EventHandler<FileSystemNode>? NodeRightClicked;

    private const int MaxCellsPerLevel = 400;
    private bool _redrawing;
    private Size _lastRedrawSize = Size.Empty;

    public TreeMapControl()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        SizeChanged += OnSizeChanged;
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (TreeMapControl)d;
        c._lastRedrawSize = Size.Empty; // invalidate cache so Redraw recomputes
        c.Dispatcher.BeginInvoke(new Action(c.Redraw), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var newSize = new Size(Math.Round(ActualWidth), Math.Round(ActualHeight));
        if (newSize.Width < 1 || newSize.Height < 1) return;
        if (newSize == _lastRedrawSize) return;
        Dispatcher.BeginInvoke(new Action(Redraw), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Redraw()
    {
        if (_redrawing) return;
        _redrawing = true;
        try
        {
            var w = ActualWidth;
            var h = ActualHeight;
            if (w < 1 || h < 1) return;

            _lastRedrawSize = new Size(Math.Round(w), Math.Round(h));

            Children.Clear();

            if (Nodes is null || Nodes.Count == 0 || ChildrenIndex is null) return;

            var rootId = CurrentRootId;
            IReadOnlyList<FileSystemNode> items;
            if (rootId is null)
            {
                items = Nodes.Where(n => n.ParentId is null && n.SizeBytes > 0).ToList();
            }
            else if (ChildrenIndex.TryGetValue(rootId.Value, out var kids))
            {
                items = kids.Where(n => n.SizeBytes > 0).ToList();
            }
            else return;

            if (items.Count == 0) return;

            // Cap total cells to keep rendering bounded — even at 100k files per folder
            var filtered = items
                .OrderByDescending(n => n.SizeBytes)
                .Take(MaxCellsPerLevel)
                .ToList();

            var rect = new Rect(0, 0, w, h);
            var total = filtered.Sum(n => n.SizeBytes);
            if (total <= 0) return;
            Squarify(filtered, 0, filtered.Count, total, rect);
        }
        finally
        {
            _redrawing = false;
        }
    }

    /// <summary>Squarified treemap layout. Recursive on best-aspect-ratio rows.</summary>
    private void Squarify(IReadOnlyList<FileSystemNode> items, int start, int end, long total, Rect rect)
    {
        if (start >= end || rect.Width < 1 || rect.Height < 1) return;

        // Scale children sizes to pixel area
        var area = rect.Width * rect.Height;
        if (area <= 0 || total <= 0) return;

        var scale = area / total;

        int rowStart = start;
        while (rowStart < end)
        {
            var rowEnd = rowStart + 1;
            double bestRatio = double.PositiveInfinity;

            // Grow the row while aspect ratio improves
            while (rowEnd <= end)
            {
                var rowItems = items.Skip(rowStart).Take(rowEnd - rowStart).ToList();
                var ratio = WorstAspectRatio(rowItems, scale, Math.Min(rect.Width, rect.Height));
                if (ratio <= bestRatio)
                {
                    bestRatio = ratio;
                    rowEnd++;
                    if (rowEnd > end) break;
                }
                else
                {
                    rowEnd--;
                    break;
                }
            }
            if (rowEnd > end) rowEnd = end;
            if (rowEnd == rowStart) rowEnd = rowStart + 1;

            // Place row
            var row = items.Skip(rowStart).Take(rowEnd - rowStart).ToList();
            var rowArea = row.Sum(n => n.SizeBytes) * scale;
            var horizontal = rect.Width >= rect.Height;
            var rowThickness = horizontal ? rowArea / rect.Height : rowArea / rect.Width;
            var rowRect = horizontal
                ? new Rect(rect.X, rect.Y, rowThickness, rect.Height)
                : new Rect(rect.X, rect.Y, rect.Width, rowThickness);

            PlaceRow(row, rowRect, scale, horizontal);

            // Advance
            rect = horizontal
                ? new Rect(rect.X + rowThickness, rect.Y, Math.Max(0, rect.Width - rowThickness), rect.Height)
                : new Rect(rect.X, rect.Y + rowThickness, rect.Width, Math.Max(0, rect.Height - rowThickness));

            rowStart = rowEnd;
        }
    }

    private double WorstAspectRatio(List<FileSystemNode> row, double scale, double shortSide)
    {
        if (row.Count == 0 || shortSide <= 0) return double.PositiveInfinity;
        var sum = row.Sum(n => n.SizeBytes) * scale;
        if (sum <= 0) return double.PositiveInfinity;
        var minS = row.Min(n => n.SizeBytes) * scale;
        var maxS = row.Max(n => n.SizeBytes) * scale;
        var s2 = shortSide * shortSide;
        var sum2 = sum * sum;
        return Math.Max(s2 * maxS / sum2, sum2 / (s2 * minS));
    }

    private void PlaceRow(List<FileSystemNode> row, Rect rowRect, double scale, bool horizontal)
    {
        double offset = 0;
        foreach (var n in row)
        {
            var size = n.SizeBytes * scale;
            if (size <= 0) continue;

            Rect r;
            if (horizontal)
            {
                var h = size / rowRect.Width;
                r = new Rect(rowRect.X, rowRect.Y + offset, rowRect.Width, h);
                offset += h;
            }
            else
            {
                var w = size / rowRect.Height;
                r = new Rect(rowRect.X + offset, rowRect.Y, w, rowRect.Height);
                offset += w;
            }

            AddCell(n, r);
        }
    }

    private void AddCell(FileSystemNode node, Rect r)
    {
        if (r.Width < 2 || r.Height < 2) return;

        var border = new Border
        {
            Width = Math.Max(0, r.Width - 1),
            Height = Math.Max(0, r.Height - 1),
            Background = BrushFor(node),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 26, 27, 30)),
            BorderThickness = new Thickness(1),
            ToolTip = $"{node.Name}\n{node.FullPath}\n{FormatBytes(node.SizeBytes)}",
            Cursor = Cursors.Hand,
            Tag = node,
        };

        Canvas.SetLeft(border, r.X);
        Canvas.SetTop(border, r.Y);

        border.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            NodeClicked?.Invoke(this, node);
        };
        border.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            NodeRightClicked?.Invoke(this, node);
        };

        // Label if the cell is big enough
        if (r.Width >= 60 && r.Height >= 28)
        {
            var label = new TextBlock
            {
                Text = node.Name,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(230, 240, 240, 240)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Padding = new Thickness(4, 2, 4, 0),
                IsHitTestVisible = false,
                Width = Math.Max(0, r.Width - 2),
            };
            var lblCanvas = new Canvas { IsHitTestVisible = false };
            Canvas.SetLeft(lblCanvas, r.X);
            Canvas.SetTop(lblCanvas, r.Y);
            lblCanvas.Children.Add(label);

            Children.Add(border);
            Children.Add(lblCanvas);
        }
        else
        {
            Children.Add(border);
        }
    }

    private Brush BrushFor(FileSystemNode node)
    {
        return ColorMode switch
        {
            TreeMapColorMode.Age    => AgeBrush(node),
            TreeMapColorMode.Type   => TypeBrush(node),
            _                       => DepthBrush(node),
        };
    }

    private static Brush DepthBrush(FileSystemNode node)
    {
        // Palette cycling by depth
        Color[] palette =
        {
            Color.FromRgb(0x4C, 0x9A, 0xFF), // blue
            Color.FromRgb(0x27, 0xAE, 0x60), // green
            Color.FromRgb(0xE6, 0x7E, 0x22), // orange
            Color.FromRgb(0x9B, 0x59, 0xB6), // purple
            Color.FromRgb(0x1A, 0xBC, 0x9C), // teal
            Color.FromRgb(0xE7, 0x4C, 0x3C), // red
            Color.FromRgb(0xF1, 0xC4, 0x0F), // yellow
        };
        return new SolidColorBrush(palette[Math.Abs(node.Depth) % palette.Length]);
    }

    private static Brush AgeBrush(FileSystemNode node)
    {
        if (node.LastModifiedUtc == DateTime.MinValue)
            return new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
        var days = (DateTime.UtcNow - node.LastModifiedUtc).TotalDays;
        var c = days switch
        {
            < 30   => Color.FromRgb(0x27, 0xAE, 0x60),
            < 180  => Color.FromRgb(0xF1, 0xC4, 0x0F),
            < 365  => Color.FromRgb(0xE6, 0x7E, 0x22),
            < 1095 => Color.FromRgb(0xE7, 0x4C, 0x3C),
            _      => Color.FromRgb(0x8B, 0x00, 0x00),
        };
        return new SolidColorBrush(c);
    }

    private static Brush TypeBrush(FileSystemNode node)
    {
        if (node.Kind == FileSystemNodeKind.Directory || node.Kind == FileSystemNodeKind.Volume)
            return new SolidColorBrush(Color.FromRgb(0x34, 0x5A, 0x8F));
        var cat = Helpers.DocumentTypeAnalyzer.Classify(node.Name);
        return new SolidColorBrush(cat switch
        {
            DocumentCategory.Pdf      => Color.FromRgb(0xE7, 0x4C, 0x3C),
            DocumentCategory.Xlsx     => Color.FromRgb(0x27, 0xAE, 0x60),
            DocumentCategory.Rvt      => Color.FromRgb(0x34, 0x98, 0xDB),
            DocumentCategory.Txt      => Color.FromRgb(0xF1, 0xC4, 0x0F),
            DocumentCategory.Dll      => Color.FromRgb(0x9B, 0x59, 0xB6),
            DocumentCategory.Sys      => Color.FromRgb(0xE6, 0x7E, 0x22),
            DocumentCategory.Exe      => Color.FromRgb(0x1A, 0xBC, 0x9C),
            DocumentCategory.Images   => Color.FromRgb(0xE9, 0x1E, 0x63),
            DocumentCategory.Videos   => Color.FromRgb(0x00, 0xBC, 0xD4),
            DocumentCategory.Archives => Color.FromRgb(0x79, 0x55, 0x48),
            _                         => Color.FromRgb(0x7F, 0x8C, 0x8D),
        });
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

public enum TreeMapColorMode
{
    Depth,
    Age,
    Type,
}
