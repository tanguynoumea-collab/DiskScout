namespace DiskScout.Models;

public enum RegistryHive
{
    LocalMachine64,
    LocalMachine32,
    CurrentUser64,
    CurrentUser32,
}

public sealed record InstalledProgram(
    string RegistryKey,
    RegistryHive Hive,
    string DisplayName,
    string? Publisher,
    string? Version,
    DateOnly? InstallDate,
    string? InstallLocation,
    string? UninstallString,
    long RegistryEstimatedSizeBytes,
    long ComputedSizeBytes);
