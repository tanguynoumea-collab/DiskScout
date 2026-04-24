using System.IO;

namespace DiskScout.Services;

public sealed record DriveInfoSnapshot(
    string RootPath,
    string Label,
    string Format,
    long TotalSizeBytes,
    long FreeSpaceBytes,
    bool IsReady);

public interface IDriveService
{
    IReadOnlyList<DriveInfoSnapshot> GetFixedDrives();
}

public sealed class DriveService : IDriveService
{
    public IReadOnlyList<DriveInfoSnapshot> GetFixedDrives()
    {
        var result = new List<DriveInfoSnapshot>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            string label = string.Empty;
            string format = string.Empty;
            long total = 0;
            long free = 0;
            bool ready = drive.IsReady;
            try
            {
                if (ready)
                {
                    label = drive.VolumeLabel;
                    format = drive.DriveFormat;
                    total = drive.TotalSize;
                    free = drive.AvailableFreeSpace;
                }
            }
            catch
            {
                ready = false;
            }
            result.Add(new DriveInfoSnapshot(drive.RootDirectory.FullName, label, format, total, free, ready));
        }
        return result;
    }
}
