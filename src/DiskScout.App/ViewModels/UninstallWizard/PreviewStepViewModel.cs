using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Services;

namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// Step 2 — preview known residues from install trace + publisher rules.
/// Two flat lists: trace events (left) and rule-derived hits (right). View hooks the Loaded event to call <see cref="Load"/>.
/// </summary>
public sealed partial class PreviewStepViewModel : ObservableObject
{
    private readonly UninstallWizardViewModel _wizard;
    private readonly IPublisherRuleEngine _ruleEngine;

    /// <summary>Pre-uninstall residue paths as recorded by the install tracker (Plan 09-01). Capped to 200 lines for UI.</summary>
    public ObservableCollection<string> TraceEvents { get; } = new();

    /// <summary>Rule-derived expected residue paths (Plan 09-04 expanded via ExpandTokens).</summary>
    public ObservableCollection<string> RuleHits { get; } = new();

    /// <summary>Convenience for the view: count of trace events surfaced.</summary>
    [ObservableProperty]
    private int _traceEventCount;

    /// <summary>Convenience for the view: count of rule-derived hits surfaced.</summary>
    [ObservableProperty]
    private int _ruleHitCount;

    public PreviewStepViewModel(UninstallWizardViewModel wizard, IPublisherRuleEngine ruleEngine)
    {
        _wizard = wizard;
        _ruleEngine = ruleEngine;
    }

    /// <summary>
    /// Populates <see cref="TraceEvents"/> + <see cref="RuleHits"/> from the wizard's resolved Trace and MatchedRules.
    /// Called by the view's Loaded event handler (acceptable code-behind for view-model lifecycle).
    /// </summary>
    public void Load()
    {
        TraceEvents.Clear();
        RuleHits.Clear();

        if (_wizard.Trace is { } trace)
        {
            foreach (var ev in trace.Events.Take(200))
            {
                TraceEvents.Add($"{ev.Kind}: {ev.Path}");
            }
        }

        foreach (var match in _wizard.MatchedRules)
        {
            foreach (var p in match.Rule.FilesystemPaths)
            {
                var expanded = _ruleEngine.ExpandTokens(p, _wizard.Target.Publisher, _wizard.Target.DisplayName);
                RuleHits.Add($"[{match.Rule.Id}] FS: {expanded}");
            }
            foreach (var p in match.Rule.RegistryPaths)
            {
                RuleHits.Add($"[{match.Rule.Id}] REG: {p}");
            }
        }

        TraceEventCount = TraceEvents.Count;
        RuleHitCount = RuleHits.Count;
    }

    [RelayCommand]
    private void Next() => _wizard.GoToRunUninstallCommand.Execute(null);

    [RelayCommand]
    private void Back() => _wizard.GoBackToSelectionCommand.Execute(null);

    [RelayCommand]
    private void Cancel() => _wizard.CancelCommand.Execute(null);
}
