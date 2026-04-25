using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskScout.Models;
using DiskScout.Services;

namespace DiskScout.ViewModels.UninstallWizard;

/// <summary>
/// Step 3 — run the native uninstaller via <see cref="INativeUninstallerDriver"/> and stream stdout/stderr live.
/// </summary>
public sealed partial class RunUninstallStepViewModel : ObservableObject
{
    private readonly UninstallWizardViewModel _wizard;
    private readonly INativeUninstallerDriver _driver;
    private readonly CancellationTokenSource _cts;

    /// <summary>Live output lines (capped at 1000 to bound memory).</summary>
    public ObservableCollection<string> OutputLines { get; } = new();

    /// <summary>Human-readable status banner shown at the top of the view.</summary>
    [ObservableProperty]
    private string _status = "Prêt à lancer la désinstallation.";

    /// <summary>True while RunAsync is awaiting the driver. Disables the Run button.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>The terminal outcome — stays null until RunAsync completes.</summary>
    [ObservableProperty]
    private UninstallOutcome? _outcome;

    /// <summary>True when Outcome reports a runnable result (Success / NonZeroExit) and the user may proceed.</summary>
    public bool CanProceed =>
        Outcome is { Status: UninstallStatus.Success or UninstallStatus.NonZeroExit };

    public RunUninstallStepViewModel(
        UninstallWizardViewModel wizard,
        INativeUninstallerDriver driver,
        CancellationTokenSource cts)
    {
        _wizard = wizard;
        _driver = driver;
        _cts = cts;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsRunning = true;
        OutputLines.Clear();
        Outcome = null;
        OnPropertyChanged(nameof(CanProceed));
        NextCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();

        try
        {
            var preferSilent = _wizard.PreferSilent;
            var quiet = ReadQuietUninstallString(_wizard.Target);

            var cmd = _driver.ParseCommand(_wizard.Target, quiet, preferSilent);
            if (cmd is null && preferSilent)
            {
                Status = "Aucune variante silencieuse — désinstallation interactive requise.";
                cmd = _driver.ParseCommand(_wizard.Target, quiet, preferSilent: false);
            }

            if (cmd is null)
            {
                Outcome = new UninstallOutcome(
                    UninstallStatus.ParseFailure,
                    ExitCode: null,
                    Elapsed: TimeSpan.Zero,
                    CapturedOutputLineCount: 0,
                    ErrorMessage: "ParseCommand returned null (UninstallString missing or unparseable).");
                _wizard.UninstallOutcome = Outcome;
                Status = "Impossible de parser la commande de désinstallation.";
                return;
            }

            Status = $"Lancement : {cmd.ExecutablePath} {cmd.Arguments}".TrimEnd();

            var progress = new Progress<string>(line =>
            {
                OutputLines.Add(line);
                if (OutputLines.Count > 1000)
                {
                    OutputLines.RemoveAt(0);
                }
            });

            Outcome = await _driver.RunAsync(cmd, progress, _cts.Token).ConfigureAwait(true);
            _wizard.UninstallOutcome = Outcome;
            Status = $"Terminé : {Outcome.Status} (code={Outcome.ExitCode}) en {Outcome.Elapsed.TotalSeconds:F1}s.";
        }
        catch (OperationCanceledException)
        {
            Status = "Annulé.";
        }
        catch (Exception ex)
        {
            Status = $"Erreur : {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(nameof(CanProceed));
            NextCommand.NotifyCanExecuteChanged();
            RunCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRun() => !IsRunning && Outcome is null;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => _wizard.GoToResidueScanCommand.Execute(null);

    private bool CanGoNext() => CanProceed;

    [RelayCommand]
    private void Back() => _wizard.GoBackToPreviewCommand.Execute(null);

    [RelayCommand]
    private void Cancel() => _wizard.CancelCommand.Execute(null);

    /// <summary>
    /// Best-effort read of <c>QuietUninstallString</c> from the program's Uninstall registry key.
    /// Returns null on any error (the driver will fall back to <see cref="InstalledProgram.UninstallString"/>).
    /// </summary>
    private static string? ReadQuietUninstallString(InstalledProgram p)
    {
        try
        {
            var hive = p.Hive is RegistryHive.LocalMachine64 or RegistryHive.LocalMachine32
                ? Microsoft.Win32.RegistryHive.LocalMachine
                : Microsoft.Win32.RegistryHive.CurrentUser;
            var view = p.Hive is RegistryHive.LocalMachine64 or RegistryHive.CurrentUser64
                ? Microsoft.Win32.RegistryView.Registry64
                : Microsoft.Win32.RegistryView.Registry32;

            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + p.RegistryKey,
                writable: false);
            return key?.GetValue("QuietUninstallString") as string;
        }
        catch
        {
            return null;
        }
    }
}
