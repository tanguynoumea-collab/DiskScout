using System.Windows;
using DiskScout.Helpers;
using DiskScout.ViewModels;

namespace DiskScout;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmDarkTitleBar.Apply(this);
    }
}
