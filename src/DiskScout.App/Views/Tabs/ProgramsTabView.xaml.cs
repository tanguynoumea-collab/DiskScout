using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskScout.Models;
using DiskScout.ViewModels;

namespace DiskScout.Views.Tabs;

/// <summary>
/// Code-behind: only subscribes to <see cref="ProgramsViewModel.UninstallRequested"/> to open
/// the Uninstall Wizard window. This is acceptable code-behind (per CLAUDE.md "événements UI purs")
/// because window-opening is a UI ceremony that doesn't fit MVVM cleanly.
/// </summary>
public partial class ProgramsTabView : UserControl
{
    private ProgramsViewModel? _hookedVm;

    public ProgramsTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => UnhookCurrent();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UnhookCurrent();
        if (e.NewValue is ProgramsViewModel vm)
        {
            vm.UninstallRequested += OnUninstallRequested;
            _hookedVm = vm;
        }
    }

    private void UnhookCurrent()
    {
        if (_hookedVm is not null)
        {
            _hookedVm.UninstallRequested -= OnUninstallRequested;
            _hookedVm = null;
        }
    }

    private void OnUninstallRequested(object? sender, InstalledProgram program)
    {
        DiskScout.App.OpenUninstallWizard(program);
    }

    private void OnDataGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        var row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
        if (row is not null) row.IsSelected = true;
    }
}
