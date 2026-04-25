using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;

namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// Step 4 — deep residue scan post-uninstall + merge in publisher-rule-derived candidates.
/// Findings are pushed both to <see cref="Findings"/> (for the view) and to
/// <see cref="UninstallWizardViewModel.AllResidueFindings"/> (consumed by Step 5).
/// </summary>
public sealed partial class ResidueScanStepViewModel : ObservableObject
{
    private readonly UninstallWizardViewModel _wizard;
    private readonly IResidueScanner _residueScanner;
    private readonly IPublisherRuleEngine _ruleEngine;
    private readonly CancellationTokenSource _cts;

    public ObservableCollection<ResidueFinding> Findings { get; } = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private string _currentArea = "Prêt à scanner.";

    [ObservableProperty]
    private int _progressPercent;

    public ResidueScanStepViewModel(
        UninstallWizardViewModel wizard,
        IResidueScanner residueScanner,
        IPublisherRuleEngine ruleEngine,
        CancellationTokenSource cts)
    {
        _wizard = wizard;
        _residueScanner = residueScanner;
        _ruleEngine = ruleEngine;
        _cts = cts;
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsScanning = true;
        HasScanned = false;
        Findings.Clear();
        _wizard.AllResidueFindings.Clear();
        ScanCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();

        try
        {
            var target = new ResidueScanTarget(
                DisplayName: _wizard.Target.DisplayName,
                Publisher: _wizard.Target.Publisher,
                InstallLocation: _wizard.Target.InstallLocation,
                RegistryKeyName: _wizard.Target.RegistryKey);

            var progress = new Progress<string>(area => CurrentArea = area);

            CurrentArea = "Scan en cours...";
            var scanned = await _residueScanner.ScanAsync(target, _wizard.Trace, progress, _cts.Token).ConfigureAwait(true);
            foreach (var f in scanned)
            {
                // Defense-in-depth: scanner already filters via ResiduePathSafety, but re-assert here too.
                if (!ResiduePathSafety.IsSafeToPropose(f.Path)) continue;
                Findings.Add(f);
            }

            // Merge publisher-rule-derived candidates with re-asserted whitelist.
            CurrentArea = "Fusion des règles éditeur...";
            foreach (var match in _wizard.MatchedRules)
            {
                foreach (var template in match.Rule.FilesystemPaths)
                {
                    if (string.IsNullOrWhiteSpace(template)) continue;
                    var expanded = _ruleEngine.ExpandTokens(template, _wizard.Target.Publisher, _wizard.Target.DisplayName);
                    if (string.IsNullOrWhiteSpace(expanded)) continue;
                    if (!ResiduePathSafety.IsSafeToPropose(expanded)) continue;

                    var exists = Directory.Exists(expanded) || File.Exists(expanded);
                    var trust = exists ? ResidueTrustLevel.HighConfidence : ResidueTrustLevel.LowConfidence;
                    var size = exists && Directory.Exists(expanded) ? MeasureFolderBytes(expanded)
                              : exists && File.Exists(expanded) ? TryGetFileSize(expanded)
                              : 0L;

                    Findings.Add(new ResidueFinding(
                        Category: ResidueCategory.Filesystem,
                        Path: expanded,
                        SizeBytes: size,
                        Reason: $"Règle éditeur '{match.Rule.Id}' (existe sur disque ? {(exists ? "oui" : "non")}).",
                        Trust: trust,
                        Source: ResidueSource.PublisherRule));
                }

                foreach (var template in match.Rule.RegistryPaths)
                {
                    if (string.IsNullOrWhiteSpace(template)) continue;
                    var expanded = _ruleEngine.ExpandTokens(template, _wizard.Target.Publisher, _wizard.Target.DisplayName);
                    if (string.IsNullOrWhiteSpace(expanded)) continue;
                    if (!ResiduePathSafety.IsSafeToPropose(expanded)) continue;

                    Findings.Add(new ResidueFinding(
                        Category: ResidueCategory.Registry,
                        Path: expanded,
                        SizeBytes: 0,
                        Reason: $"Règle éditeur '{match.Rule.Id}' (clé registre).",
                        Trust: ResidueTrustLevel.MediumConfidence,
                        Source: ResidueSource.PublisherRule));
                }
            }

            _wizard.AllResidueFindings.AddRange(Findings);
            ProgressPercent = 100;
            CurrentArea = $"Scan terminé — {Findings.Count} résidu(s) candidat(s).";
            HasScanned = true;
        }
        catch (OperationCanceledException)
        {
            CurrentArea = "Scan annulé.";
        }
        catch (Exception ex)
        {
            CurrentArea = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            ScanCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScan() => !IsScanning && !HasScanned;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => _wizard.GoToConfirmDeleteCommand.Execute(null);

    private bool CanGoNext() => HasScanned && !IsScanning;

    [RelayCommand]
    private void Cancel() => _wizard.CancelCommand.Execute(null);

    private static long MeasureFolderBytes(string path)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { /* per-file best-effort */ }
            }
        }
        catch { /* per-folder best-effort */ }
        return total;
    }

    private static long TryGetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
