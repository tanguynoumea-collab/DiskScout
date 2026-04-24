namespace DiskScout.Models;

public enum CloudProvider
{
    Unknown,
    OneDrivePersonal,
    OneDriveBusiness,
    SharePoint,
}

public sealed record CloudSyncRoot(
    CloudProvider Provider,
    string DisplayName,
    string RootPath,
    long PhysicalBytes,
    long LogicalBytes,
    int HydratedFileCount,
    int PlaceholderFileCount,
    int TotalFileCount);
