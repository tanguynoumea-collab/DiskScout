using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace DiskScout.Helpers;

public static class LogService
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private const int RetainedFileCount = 5;

    public static Logger CreateLogger() =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: AppPaths.LogFilePath,
                rollingInterval: RollingInterval.Infinite,
                fileSizeLimitBytes: MaxFileSizeBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCount,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                shared: false)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .CreateLogger();
}
