using System.IO;

namespace DiskScout.Helpers;

public static class AppPaths
{
    public const string AppFolderName = "DiskScout";
    public const string LogFileName = "diskscout.log";
    public const string ScansFolderName = "scans";
    public const string InstallTracesFolderName = "install-traces";
    public const string PublisherRulesFolderName = "publisher-rules";
    public const string PathRulesFolderName = "path-rules";
    public const string AuditsFolderName = "audits";

    public static string AppDataFolder
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(root, AppFolderName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string LogFilePath => Path.Combine(AppDataFolder, LogFileName);

    public static string ScansFolder
    {
        get
        {
            var path = Path.Combine(AppDataFolder, ScansFolderName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string InstallTracesFolder
    {
        get
        {
            var path = Path.Combine(AppDataFolder, InstallTracesFolderName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string PublisherRulesFolder
    {
        get
        {
            var path = Path.Combine(AppDataFolder, PublisherRulesFolderName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string PathRulesFolder
    {
        get
        {
            var path = Path.Combine(AppDataFolder, PathRulesFolderName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string AuditFolder
    {
        get
        {
            var path = Path.Combine(AppDataFolder, AuditsFolderName);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
