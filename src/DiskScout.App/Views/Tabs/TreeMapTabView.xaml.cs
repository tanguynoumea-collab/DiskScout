using System.Windows.Controls;
using DiskScout.ViewModels;

namespace DiskScout.Views.Tabs;

public partial class TreeMapTabView : UserControl
{
    public TreeMapTabView()
    {
        InitializeComponent();
        Map.NodeClicked += OnNodeClicked;
        Map.NodeRightClicked += OnNodeRightClicked;
    }

    private void OnNodeClicked(object? sender, DiskScout.Models.FileSystemNode node)
    {
        if (DataContext is TreeMapViewModel vm)
        {
            vm.DrillDown(node);
        }
    }

    private async void OnNodeRightClicked(object? sender, DiskScout.Models.FileSystemNode node)
    {
        if (DataContext is not TreeMapViewModel vm) return;

        // Minimal inline menu: Copier / Supprimer
        var menu = new System.Windows.Controls.ContextMenu();
        var copy = new System.Windows.Controls.MenuItem { Header = "Copier le chemin" };
        copy.Click += (_, _) => vm.CopyNodePath(node);
        var del = new System.Windows.Controls.MenuItem { Header = "Supprimer…" };
        del.Click += async (_, _) => await vm.DeleteNodeAsync(node);
        menu.Items.Add(copy);
        menu.Items.Add(del);
        menu.IsOpen = true;
        await System.Threading.Tasks.Task.CompletedTask;
    }
}
