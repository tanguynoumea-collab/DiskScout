using System.IO;
using System.Linq;
using System.Windows;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;
using DiskScout.ViewModels;
using Serilog;
using Serilog.Core;

namespace DiskScout;

public partial class App : Application
{
    private Logger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            pdfReport);

        var mainWindow = new MainWindow(mainViewModel);
        mainWindow.Show();

        _logger.Information("DiskScout shell shown.");
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
