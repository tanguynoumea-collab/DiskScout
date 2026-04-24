using System.IO;

namespace DiskScout.Helpers;

public static class AppPaths
{
    public const string AppFolderName = "DiskScout";
    public const string LogFileName = "diskscout.log";
    public const string ScansFolderName = "scans";

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
}
