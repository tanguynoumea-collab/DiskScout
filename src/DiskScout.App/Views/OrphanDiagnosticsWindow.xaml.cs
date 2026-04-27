using System.Windows;

namespace DiskScout.Views;

/// <summary>
/// "Pourquoi cette détection ?" modal — surfaces the full diagnostics trace
/// of an AppData orphan candidate (Plan 10-06). Code-behind is intentionally
/// minimal: only InitializeComponent + the close-button click handler, per
/// CLAUDE.md MVVM strict rules ("événements UI purs uniquement").
/// </summary>
public partial class OrphanDiagnosticsWindow : Window
{
    public OrphanDiagnosticsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
