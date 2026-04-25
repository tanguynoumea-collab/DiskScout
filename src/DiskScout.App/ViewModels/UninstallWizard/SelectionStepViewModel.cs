using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Models;

namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// Step 1 — confirm program selection + silent toggle. The selected program comes from the
/// caller (right-click on Programs DataGrid), so this step is mostly informational.
/// </summary>
public sealed partial class SelectionStepViewModel : ObservableObject
{
    private readonly UninstallWizardViewModel _wizard;

    public InstalledProgram Target => _wizard.Target;

    /// <summary>Two-way bound to the wizard's PreferSilent so toggling here flows to the run step.</summary>
    public bool PreferSilent
    {
        get => _wizard.PreferSilent;
        set
        {
            if (_wizard.PreferSilent != value)
            {
                _wizard.PreferSilent = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>True when the wizard has at least one matched publisher rule (informational badge).</summary>
    public bool HasMatchedRules => _wizard.MatchedRules.Count > 0;

    /// <summary>Comma-separated list of matched rule ids — for the "Règles éditeur" badge text.</summary>
    public string MatchedRulesSummary =>
        _wizard.MatchedRules.Count == 0
            ? "Aucune règle éditeur connue"
            : string.Join(", ", _wizard.MatchedRules.Select(m => m.Rule.Id));

    public SelectionStepViewModel(UninstallWizardViewModel wizard)
    {
        _wizard = wizard;
    }

    [RelayCommand]
    private void Next() => _wizard.GoToPreviewCommand.Execute(null);

    [RelayCommand]
    private void Cancel() => _wizard.CancelCommand.Execute(null);
}
