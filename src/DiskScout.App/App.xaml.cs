using System.IO;
using System.Linq;
using System.Windows;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;
using DiskScout.ViewModels;
using DiskScout.ViewModels.UninstallWizard;
using DiskScout.Views.UninstallWizard;
using Serilog;
using Serilog.Core;

namespace DiskScout;

public partial class App : Application
{
    private Logger? _logger;

    // Services constructed once at startup; cached for OpenUninstallWizard re-entry.
    private IInstallTracker? _installTracker;
    private IInstallTraceStore? _installTraceStore;
    private INativeUninstallerDriver? _uninstallDriver;
    private IResidueScanner? _residueScanner;
    private IPublisherRuleEngine? _ruleEngine;
    private IFileDeletionService? _fileDeletion;
    private IUninstallReportService? _uninstallReport;

    private static App? Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Instance = this;

        _logger = LogService.CreateLogger();
        Log.Logger = _logger;

        _logger.Information("DiskScout starting (version {Version}).", typeof(App).Assembly.GetName().Version);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _logger?.Fatal(args.ExceptionObject as Exception, "Unhandled AppDomain exception. Terminating={Terminating}", args.IsTerminating);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.Error(args.Exception, "Unhandled Dispatcher exception.");
            MessageBox.Show(
                args.Exception.Message + "\n\nVoir diskscout.log pour le détail.",
                "DiskScout — erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.Error(args.Exception, "Unobserved task exception.");
            args.SetObserved();
        };

        // Composition root — manual DI.
        IDriveService driveService = new DriveService();
        IFileSystemScanner fileSystemScanner = new NativeFileSystemScanner(_logger);
        IInstalledProgramsScanner installedProgramsScanner = new RegistryInstalledProgramsScanner(_logger);
        IOrphanDetectorService orphanDetectorService = new OrphanDetectorService(_logger);
        IPersistenceService persistenceService = new JsonPersistenceService(_logger);
        IQuarantineService quarantineService = new QuarantineService(_logger);
        IFileDeletionService fileDeletionService = new FileDeletionService(_logger, quarantineService);
        IPdfReportService pdfReport = new PdfReportService(_logger);

        // Phase 9 services — cached for the Uninstall Wizard launcher.
        _installTraceStore = new JsonInstallTraceStore(_logger);
        _installTracker = new InstallTracker(_logger, _installTraceStore);
        _uninstallDriver = new NativeUninstallerDriver(_logger);
        _residueScanner = new ResidueScanner(_logger);
        _ruleEngine = new PublisherRuleEngine(_logger);
        // Plan 06: load rules synchronously so Match() is ready before the wizard can be opened
        // and before MainViewModel.OnScanCompleted's annotation flow runs. JSON files are tiny
        // (7 embedded + any user files) — measured below 50 ms in practice.
        _ruleEngine.LoadAsync().GetAwaiter().GetResult();
        _fileDeletion = fileDeletionService; // alias for the wizard
        _uninstallReport = new UninstallReportService(_logger);

        // Housekeeping at startup: purge expired quarantine + old scan JSONs.
        _ = quarantineService.PurgeAsync(TimeSpan.FromDays(30));
        _ = PurgeOldScansAsync(10);

        var mainViewModel = new MainViewModel(
            _logger,
            driveService,
            fileSystemScanner,
            installedProgramsScanner,
            orphanDetectorService,
            persistenceService,
            fileDeletionService,
            quarantineService,
            _installTraceStore,
            _ruleEngine,
            pdfReport);

        var mainWindow = new MainWindow(mainViewModel);
        mainWindow.Show();

        _logger.Information("DiskScout shell shown.");
    }

    /// <summary>
    /// Opens the Uninstall Wizard window modally for the given program.
    /// Called from <see cref="DiskScout.Views.Tabs.ProgramsTabView"/> in response to
    /// <c>ProgramsViewModel.UninstallRequested</c>.
    /// </summary>
    public static void OpenUninstallWizard(InstalledProgram program)
    {
        var app = Instance ?? throw new InvalidOperationException("App not initialized");
        if (app._installTracker is null
            || app._installTraceStore is null
            || app._uninstallDriver is null
            || app._residueScanner is null
            || app._ruleEngine is null
            || app._fileDeletion is null
            || app._uninstallReport is null)
        {
            throw new InvalidOperationException("Phase 9 services not yet wired.");
        }

        var vm = new UninstallWizardViewModel(
            app._logger!,
            program,
            app._installTracker,
            app._installTraceStore,
            app._uninstallDriver,
            app._residueScanner,
            app._ruleEngine,
            app._fileDeletion,
            app._uninstallReport);

        var window = new UninstallWizardWindow
        {
            DataContext = vm,
            Owner = Current.MainWindow,
        };
        vm.CloseRequested += (_, _) =>
        {
            try { window.Close(); } catch { /* best-effort */ }
        };
        window.ShowDialog();
    }

    private Task PurgeOldScansAsync(int keepMostRecent) => Task.Run(() =>
    {
        try
        {
            var dir = AppPaths.ScansFolder;
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir)
                .EnumerateFiles("scan_*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            if (files.Count <= keepMostRecent) return;
            foreach (var f in files.Skip(keepMostRecent))
            {
                try { f.Delete(); _logger?.Information("Purged old scan {File}", f.Name); }
                catch (Exception ex) { _logger?.Warning(ex, "Failed to purge scan {File}", f.Name); }
            }
        }
        catch (Exception ex) { _logger?.Warning(ex, "Scan purge failed"); }
    });

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Information("DiskScout exiting (code {ExitCode}).", e.ApplicationExitCode);
        _logger?.Dispose();
        base.OnExit(e);
    }
}
