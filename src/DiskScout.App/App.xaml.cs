using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        // Phase 10 — Orphan Detection Precision Refactor.
        // Build the AppData pipeline foundations: PathRule engine + parent
        // analyzer, machine snapshot (services + drivers + appx + sched tasks),
        // publisher alias resolver, then the four matchers + scorer + classifier.
        // All wired into IAppDataOrphanPipeline; OrphanDetectorService owns it.
        IPathRuleEngine pathRuleEngine = new PathRuleEngine(_logger);
        // Eager-load like the Phase 9 PublisherRuleEngine — JSON is tiny (5 embedded
        // catalogs, ~100 rules total) and Match() must be ready before scans run.
        pathRuleEngine.LoadAsync().GetAwaiter().GetResult();
        IParentContextAnalyzer parentAnalyzer = new ParentContextAnalyzer();

        IServiceEnumerator serviceEnumerator =
            ResidueScanner.CreateDefaultServiceEnumerator(_logger);
        IScheduledTaskEnumerator scheduledTaskEnumerator =
            ResidueScanner.CreateDefaultScheduledTaskEnumerator(_logger);
        IDriverEnumerator driverEnumerator = new DriverEnumerator(_logger);
        IAppxEnumerator appxEnumerator = new AppxEnumerator(_logger);
        IMachineSnapshotProvider snapshotProvider = new MachineSnapshotProvider(
            _logger, serviceEnumerator, driverEnumerator, appxEnumerator, scheduledTaskEnumerator);

        IPublisherAliasResolver aliasResolver = new PublisherAliasResolver(_logger);
        // Resolver auto-loads on first ResolveAsync — no startup wait.

        IConfidenceScorer confidenceScorer = new ConfidenceScorer();
        IRiskLevelClassifier riskClassifier = new RiskLevelClassifier();
        IServiceMatcher serviceMatcher = new ServiceMatcher(_logger);
        IDriverMatcher driverMatcher = new DriverMatcher(_logger);
        IAppxMatcher appxMatcher = new AppxMatcher(_logger);
        IRegistryMatcher registryMatcher = new RegistryMatcher(_logger);

        IAppDataOrphanPipeline appDataPipeline = new AppDataOrphanPipeline(
            _logger,
            pathRuleEngine,
            parentAnalyzer,
            snapshotProvider,
            aliasResolver,
            confidenceScorer,
            riskClassifier,
            serviceMatcher,
            driverMatcher,
            appxMatcher,
            registryMatcher);

        IOrphanDetectorService orphanDetectorService = new OrphanDetectorService(_logger, appDataPipeline);
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

        // Phase-10-05: --audit headless CLI mode. Runs the orphan detector
        // pipeline once, writes a CSV under %LocalAppData%\DiskScout\audits,
        // and exits without showing the WPF shell. Used for repeatable offline
        // precision review on any machine.
        var args = e.Args ?? Array.Empty<string>();
        if (args.Length > 0 && args[0].Equals("--audit", StringComparison.OrdinalIgnoreCase))
        {
            // Attach to the parent console (PowerShell / cmd) so Console.WriteLine
            // surfaces the CSV path. If launched from Explorer (no parent console),
            // AttachConsole returns false — we fall back to a sibling audit_path.txt
            // next to the CSV.
            bool hasConsole = AttachConsole(-1);

            _logger.Information("DiskScout running in --audit mode (no UI). HasConsole={HasConsole}", hasConsole);
            try
            {
                var auditWriter = new AuditCsvWriter(_logger);
                var auditPath = RunHeadlessAuditAsync(
                    driveService,
                    fileSystemScanner,
                    installedProgramsScanner,
                    orphanDetectorService,
                    auditWriter)
                    .GetAwaiter().GetResult();

                _logger.Information("Audit complete: {Path}", auditPath);
                if (hasConsole)
                {
                    Console.WriteLine(auditPath);
                }
                else
                {
                    try
                    {
                        var sidecar = Path.Combine(Path.GetDirectoryName(auditPath)!, "audit_path.txt");
                        File.WriteAllText(sidecar, auditPath);
                    }
                    catch (Exception sex)
                    {
                        _logger.Warning(sex, "Audit-mode: failed to write sidecar audit_path.txt");
                    }
                }
                Shutdown(0);
                return;
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Audit-mode failure");
                if (hasConsole) Console.Error.WriteLine($"Audit failed: {ex.Message}");
                Shutdown(1);
                return;
            }
        }

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
    /// Phase-10-05: headless audit flow. Picks the first ready fixed drive,
    /// runs the same scan + orphan-detect pipeline as the UI flow, then writes
    /// the AppData candidates' diagnostics to a CSV via <see cref="IAuditCsvWriter"/>.
    /// Returns the full path to the CSV. The composition is identical to the UI
    /// path — this keeps a single execution flow under test.
    /// </summary>
    private async Task<string> RunHeadlessAuditAsync(
        IDriveService driveService,
        IFileSystemScanner fileSystemScanner,
        IInstalledProgramsScanner installedProgramsScanner,
        IOrphanDetectorService orphanDetectorService,
        IAuditCsvWriter auditWriter)
    {
        var drives = driveService.GetFixedDrives()
            .Where(d => d.IsReady)
            .Select(d => d.RootPath)
            .ToList();

        if (drives.Count == 0)
        {
            _logger?.Warning("Audit-mode: no ready fixed drives found");
            return await auditWriter.WriteAsync(Array.Empty<AppDataOrphanCandidate>());
        }

        // Limit to the first ready drive — the audit purpose is precision
        // measurement on a representative sample, not exhaustive cross-disk
        // enumeration. Keeps the run time bounded (target ≤ 30 s per audit).
        var firstDrive = drives.Take(1).ToList();

        var noopProgress = new Progress<ScanProgress>();
        var nodes = await fileSystemScanner.ScanAsync(firstDrive, noopProgress, CancellationToken.None)
            .ConfigureAwait(false);
        var programs = await installedProgramsScanner.ScanAsync(nodes, CancellationToken.None)
            .ConfigureAwait(false);
        var orphans = await orphanDetectorService.DetectAsync(nodes, programs, CancellationToken.None)
            .ConfigureAwait(false);

        // Project to the AppData diagnostics — only the AppData branch sets
        // the Diagnostics field (Plan 10-04). Other categories produce
        // OrphanCandidate with Diagnostics=null and are skipped here.
        var candidates = orphans
            .Where(o => o.Category == OrphanCategory.AppDataOrphan && o.Diagnostics is not null)
            .Select(o => o.Diagnostics!)
            .ToList();

        return await auditWriter.WriteAsync(candidates).ConfigureAwait(false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

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
