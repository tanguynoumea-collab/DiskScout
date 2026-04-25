using System.Windows;
using System.Windows.Controls;
using DiskScout.ViewModels.UninstallWizard;

namespace DiskScout.Views.UninstallWizard;

public partial class PreviewStepView : UserControl
{
    public PreviewStepView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Acceptable code-behind: invoke the view-model's Load() once the view is instantiated.
    /// This is a view-model lifecycle event (not business logic) so it stays in code-behind.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreviewStepViewModel vm)
        {
            vm.Load();
        }
    }
}
