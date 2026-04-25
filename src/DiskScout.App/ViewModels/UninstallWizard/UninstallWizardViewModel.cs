using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Models;
using DiskScout.Services;

namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// Top-level state machine of the Uninstall Wizard. Holds:
/// - the target program;
/// - the active <see cref="WizardStep"/> + the corresponding step view-model;
/// - shared per-wizard state (Trace, MatchedRules, AllResidueFindings, UninstallOutcome, DeletionOutcome);
/// - a wizard-scoped <see cref="CancellationTokenSource"/> reused by RunUninstall + ResidueScan.
///
/// Dependencies are injected by the caller (App.OpenUninstallWizard helper) — this view-model
/// itself is constructed once per launch and discarded when the window closes.
/// </summary>
public sealed partial class UninstallWizardViewModel : ObservableObject
{
    private readonly Serilog.ILogger _logger;
    private readonly IInstallTracker _tracker;
    private readonly IInstallTraceStore _traceStore;
    private readonly INativeUninstallerDriver _driver;
    private readonly IResidueScanner _residueScanner;
    private readonly IPublisherRuleEngine _ruleEngine;
    private readonly IFileDeletionService _deletion;

    /// <summary>Wizard-scoped CTS used by RunUninstall + ResidueScan steps. Cancel() tears it down.</summary>
    private CancellationTokenSource? _wizardCts;

    [ObservableProperty]
    private WizardStep _currentStep = WizardStep.Selection;

    [ObservableProperty]
    private ObservableObject? _currentStepViewModel;

    [ObservableProperty]
    private string _windowTitle = "Désinstallation assistée";

    /// <summary>Should the driver request a silent uninstall? Toggled by Step 1 (Selection).</summary>
    [ObservableProperty]
    private bool _preferSilent = true;

    /// <summary>The program being uninstalled. Immutable for the lifetime of this wizard.</summary>
    public InstalledProgram Target { get; }

    /// <summary>Optional install trace (Plan 09-01) — if a previous install was tracked, scanned for HighConfidence findings.</summary>
    public InstallTrace? Trace { get; set; }

    /// <summary>Publisher-rule matches (Plan 09-04) — populated when Selection step initialises.</summary>
    public IReadOnlyList<PublisherRuleMatch> MatchedRules { get; set; } = Array.Empty<PublisherRuleMatch>();

    /// <summary>All residue findings discovered during Step 4 (ResidueScan). Consumed by Step 5 (ConfirmDelete) to build the tree.</summary>
    public List<ResidueFinding> AllResidueFindings { get; } = new();

    /// <summary>Outcome of Step 3 (RunUninstall) — populated by RunUninstallStepViewModel.</summary>
    public UninstallOutcome? UninstallOutcome { get; set; }

    /// <summary>Outcome of Step 5 deletion (Plan 06 will use it for the final report).</summary>
    public DeletionResult? DeletionOutcome { get; set; }

    /// <summary>Token exposed for steps that want to honour the wizard-level cancel.</summary>
    public CancellationToken WizardCancellationToken => _wizardCts?.Token ?? CancellationToken.None;

    /// <summary>Raised when the user cancels or the wizard finishes — Window listens to close itself.</summary>
    public event EventHandler? CloseRequested;

    public UninstallWizardViewModel(
        Serilog.ILogger logger,
        InstalledProgram target,
        IInstallTracker tracker,
        IInstallTraceStore traceStore,
        INativeUninstallerDriver driver,
        IResidueScanner residueScanner,
        IPublisherRuleEngine ruleEngine,
        IFileDeletionService deletion)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _traceStore = traceStore ?? throw new ArgumentNullException(nameof(traceStore));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _residueScanner = residueScanner ?? throw new ArgumentNullException(nameof(residueScanner));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _deletion = deletion ?? throw new ArgumentNullException(nameof(deletion));

        // Initial step: Selection. The view-model is constructed here so tests can assert
        // the initial CurrentStepViewModel type without spinning up a window.
        CurrentStepViewModel = new SelectionStepViewModel(this);

        // Resolve publisher-rule matches eagerly so Selection / Preview can show the badge.
        try
        {
            MatchedRules = _ruleEngine.Match(Target.Publisher, Target.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "PublisherRuleEngine.Match failed for {DisplayName}", Target.DisplayName);
            MatchedRules = Array.Empty<PublisherRuleMatch>();
        }
    }

    [RelayCommand]
    private void GoToPreview()
    {
        CurrentStep = WizardStep.Preview;
        CurrentStepViewModel = new PreviewStepViewModel(this, _ruleEngine);
    }

    [RelayCommand]
    private void GoToRunUninstall()
    {
        CurrentStep = WizardStep.RunUninstall;
        DisposeAndRecreateCts();
        CurrentStepViewModel = new RunUninstallStepViewModel(this, _driver, _wizardCts!);
    }

    [RelayCommand]
    private void GoToResidueScan()
    {
        CurrentStep = WizardStep.ResidueScan;
        DisposeAndRecreateCts();
        CurrentStepViewModel = new ResidueScanStepViewModel(this, _residueScanner, _ruleEngine, _wizardCts!);
    }

    [RelayCommand]
    private void GoToConfirmDelete()
    {
        CurrentStep = WizardStep.ConfirmDelete;
        CurrentStepViewModel = new ConfirmDeleteStepViewModel(this, _deletion, _logger);
    }

    [RelayCommand]
    private void GoBackToSelection()
    {
        CurrentStep = WizardStep.Selection;
        CurrentStepViewModel = new SelectionStepViewModel(this);
    }

    [RelayCommand]
    private void GoBackToPreview()
    {
        CurrentStep = WizardStep.Preview;
        CurrentStepViewModel = new PreviewStepViewModel(this, _ruleEngine);
    }

    /// <summary>
    /// Cancels any in-flight wizard operation (RunUninstall / ResidueScan) and asks the window to close.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        try { _wizardCts?.Cancel(); }
        catch (Exception ex) { _logger.Warning(ex, "CTS cancel threw"); }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes without cancelling — used by terminal Done state.</summary>
    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeAndRecreateCts()
    {
        try { _wizardCts?.Cancel(); } catch { }
        _wizardCts?.Dispose();
        _wizardCts = new CancellationTokenSource();
    }
}
