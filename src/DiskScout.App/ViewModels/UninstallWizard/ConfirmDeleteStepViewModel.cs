using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;

namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// Step 5 — checkable tree of residue findings + final irreversible-confirmation modal.
///
/// Safety contract (CONTEXT.md D-03 + plan must_haves):
/// - Tree is grouped by <see cref="ResidueCategory"/>; every leaf starts UNCHECKED;
/// - <see cref="ConfirmAsync"/> shows a final modal with explicit IRRÉVERSIBLE wording before deleting;
/// - Deletion goes through <see cref="IFileDeletionService.DeleteAsync"/> with
///   <see cref="DeleteMode.Permanent"/> — NEVER quarantine, NEVER recycle bin (per D-03);
/// - Every path is re-asserted against <see cref="ResiduePathSafety.IsSafeToPropose"/> at BuildTree
///   AND at ConfirmAsync (defense-in-depth — scanner already filtered, but we verify again here).
/// </summary>
public sealed partial class ConfirmDeleteStepViewModel : ObservableObject
{
    private readonly UninstallWizardViewModel _wizard;
    private readonly IFileDeletionService _deletion;
    private readonly Serilog.ILogger _logger;

    public ObservableCollection<ResidueTreeNode> Roots { get; } = new();

    [ObservableProperty]
    private long _totalSelectedBytes;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private DeletionResult? _deletionOutcome;

    /// <summary>Pretty-printed total bytes selected for the bottom banner.</summary>
    public string TotalSelectedDisplay => ByteFormat.Fmt(TotalSelectedBytes);

    public ConfirmDeleteStepViewModel(
        UninstallWizardViewModel wizard,
        IFileDeletionService deletion,
        Serilog.ILogger logger)
    {
        _wizard = wizard;
        _deletion = deletion;
        _logger = logger;
        BuildTree();
    }

    /// <summary>
    /// Build the category-grouped tree from <c>_wizard.AllResidueFindings</c>.
    /// Defense-in-depth: every leaf path is re-asserted against the whitelist before being added.
    /// </summary>
    private void BuildTree()
    {
        Roots.Clear();

        foreach (var grp in _wizard.AllResidueFindings.GroupBy(f => f.Category))
        {
            var category = new ResidueTreeNode
            {
                Label = $"{CategoryLabel(grp.Key)} ({grp.Count()})",
                Category = grp.Key,
                IsChecked = false,
            };

            foreach (var f in grp.OrderByDescending(x => x.SizeBytes))
            {
                // Defense-in-depth: scanner filters via ResiduePathSafety, but re-assert here.
                if (!ResiduePathSafety.IsSafeToPropose(f.Path))
                {
                    _logger.Warning("BuildTree: dropping unsafe path {Path} (caught by defense-in-depth)", f.Path);
                    continue;
                }

                var leaf = new ResidueTreeNode
                {
                    Label = f.Path,
                    Path = f.Path,
                    SizeBytes = f.SizeBytes,
                    Category = f.Category,
                    Trust = f.Trust,
                    Source = f.Source,
                    Reason = f.Reason,
                    IsChecked = false,
                };

                leaf.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ResidueTreeNode.IsChecked)) RecomputeTotals();
                };
                category.Children.Add(leaf);
            }

            if (category.Children.Count > 0)
            {
                category.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ResidueTreeNode.IsChecked)) RecomputeTotals();
                };
                Roots.Add(category);
            }
        }
    }

    private static string CategoryLabel(ResidueCategory cat) => cat switch
    {
        ResidueCategory.Filesystem => "Fichiers / dossiers",
        ResidueCategory.Registry => "Registre",
        ResidueCategory.Shortcut => "Raccourcis",
        ResidueCategory.MsiPatch => "Patches MSI",
        ResidueCategory.Service => "Services Windows",
        ResidueCategory.ScheduledTask => "Tâches planifiées",
        ResidueCategory.ShellExtension => "Extensions Explorateur",
        _ => cat.ToString(),
    };

    private void RecomputeTotals()
    {
        long bytes = 0;
        int count = 0;
        foreach (var root in Roots)
        {
            foreach (var leaf in root.EnumerateLeavesChecked())
            {
                bytes += leaf.SizeBytes;
                count++;
            }
        }
        TotalSelectedBytes = bytes;
        SelectedCount = count;
        OnPropertyChanged(nameof(TotalSelectedDisplay));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// All selected leaf paths (Path != null and IsChecked == true). Filtered through
    /// <see cref="ResiduePathSafety.IsSafeToPropose"/> as a final defense-in-depth pass.
    /// </summary>
    public IReadOnlyList<string> SelectedPaths
    {
        get
        {
            var list = new List<string>();
            foreach (var root in Roots)
            {
                foreach (var leaf in root.EnumerateLeavesChecked())
                {
                    if (leaf.Path is null) continue;
                    if (!ResiduePathSafety.IsSafeToPropose(leaf.Path)) continue;
                    list.Add(leaf.Path);
                }
            }
            return list;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        // 1. Re-derive the safe selection (defense-in-depth — scanner + BuildTree already filtered).
        var paths = new List<string>();
        foreach (var root in Roots)
        {
            foreach (var leaf in root.EnumerateLeavesChecked())
            {
                if (leaf.Path is null) continue;
                if (!ResiduePathSafety.IsSafeToPropose(leaf.Path))
                {
                    _logger.Warning("ConfirmAsync: dropping unsafe path {Path} (defense-in-depth)", leaf.Path);
                    continue;
                }
                paths.Add(leaf.Path);
            }
        }

        if (paths.Count == 0)
        {
            return;
        }

        // 2. Show the final irreversible-confirmation modal. Default = "Non".
        var sizeStr = ByteFormat.Fmt(TotalSelectedBytes);
        var msg =
            $"Supprimer DÉFINITIVEMENT {paths.Count} élément(s) ({sizeStr}) ?" + Environment.NewLine + Environment.NewLine +
            "Cette action est IRRÉVERSIBLE — pas de quarantaine, pas de corbeille." + Environment.NewLine + Environment.NewLine +
            "Confirmer ?";

        var res = System.Windows.MessageBox.Show(
            msg,
            "DiskScout — confirmer la suppression définitive",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Stop,
            System.Windows.MessageBoxResult.No);

        if (res != System.Windows.MessageBoxResult.Yes)
        {
            _logger.Information("ConfirmDelete: user cancelled the irreversible confirmation modal ({Count} items, {Size})", paths.Count, sizeStr);
            return;
        }

        // 3. Perform the deletion via IFileDeletionService with DeleteMode.Permanent.
        IsDeleting = true;
        ConfirmCommand.NotifyCanExecuteChanged();

        try
        {
            DeletionOutcome = await _deletion.DeleteAsync(paths, DeleteMode.Permanent, default).ConfigureAwait(true);
            _wizard.DeletionOutcome = DeletionOutcome;
            _wizard.CurrentStep = WizardStep.Done;
            _logger.Information(
                "Wizard deletion complete: {Success} succeeded, {Fail} failed, {Bytes} freed permanently",
                DeletionOutcome.SuccessCount, DeletionOutcome.FailureCount, DeletionOutcome.TotalBytesFreed);

            DeletePrompt.ShowResult(DeletionOutcome, DeleteMode.Permanent);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Wizard deletion failed");
            System.Windows.MessageBox.Show(
                $"Échec de la suppression : {ex.Message}",
                "DiskScout — erreur",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsDeleting = false;
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanConfirm() => SelectedCount > 0 && !IsDeleting;

    [RelayCommand]
    private void Cancel() => _wizard.CancelCommand.Execute(null);

    [RelayCommand]
    private void Close() => _wizard.CloseCommand.Execute(null);
}
