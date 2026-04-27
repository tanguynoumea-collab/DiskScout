namespace DiskScout.Models;

/// <summary>
/// Immutable aggregate of the four machine-state surfaces relevant to the AppData
/// orphan-detection pipeline (Phase 10): installed Windows services, third-party
/// drivers in the Windows driver store, installed Appx (UWP/MSIX) packages, and
/// scheduled tasks. Built once per ~5-min TTL window by
/// <see cref="DiskScout.Services.MachineSnapshotProvider"/> and consumed by the
/// MultiSourceMatcher pipeline step (Plan 10-04).
/// </summary>
/// <remarks>
/// <para>
/// In addition to the four entry lists, the snapshot exposes three pre-built
/// <see cref="IReadOnlySet{T}"/> indexes for O(1) substring/prefix lookups during
/// the matcher hot loop:
/// </para>
/// <list type="bullet">
/// <item><see cref="ServiceBinaryPathPrefixes"/> — directory part of every service's
///   <c>BinaryPath</c> (case-insensitive)</item>
/// <item><see cref="DriverProviderTokens"/> — whitespace-separated lower-case tokens
///   of every driver's <c>Provider</c> field</item>
/// <item><see cref="AppxInstallLocationPrefixes"/> — every Appx package's
///   <c>InstallLocation</c> (case-insensitive)</item>
/// </list>
/// <para>
/// Once constructed, the snapshot is fully immutable — the provider returns the same
/// reference for every <c>GetAsync</c> within the TTL window.
/// </para>
/// </remarks>
public sealed record MachineSnapshot(
    DateTime CapturedUtc,
    IReadOnlyList<ServiceEntry> Services,
    IReadOnlyList<DriverEntry> Drivers,
    IReadOnlyList<AppxEntry> AppxPackages,
    IReadOnlyList<ScheduledTaskEntry> ScheduledTasks)
{
    /// <summary>
    /// Set of every service binary's parent directory (case-insensitive). Lets the
    /// MultiSourceMatcher rule out an AppData candidate whose path lives under any
    /// running-service binary directory in a single hash lookup.
    /// </summary>
    public IReadOnlySet<string> ServiceBinaryPathPrefixes { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set of lower-case tokens extracted from every driver's <c>Provider</c> field
    /// (split on whitespace). Lets the matcher detect "Provider" overlap with the
    /// vendor of an AppData candidate folder.
    /// </summary>
    public IReadOnlySet<string> DriverProviderTokens { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set of every Appx package's <c>InstallLocation</c> (case-insensitive). Lets
    /// the matcher rule out a candidate whose path is rooted at an installed Appx
    /// package's directory in a single hash lookup.
    /// </summary>
    public IReadOnlySet<string> AppxInstallLocationPrefixes { get; init; }
        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One Windows service. Tuple-equivalent to
/// <see cref="DiskScout.Services.IServiceEnumerator"/>'s yield shape.</summary>
public sealed record ServiceEntry(string Name, string DisplayName, string? BinaryPath);

/// <summary>One third-party driver in the Windows driver store. Tuple-equivalent to
/// <see cref="DiskScout.Services.IDriverEnumerator"/>'s yield shape.</summary>
public sealed record DriverEntry(string PublishedName, string? OriginalFileName, string? Provider, string? ClassName);

/// <summary>One installed Appx (UWP/MSIX) package. Tuple-equivalent to
/// <see cref="DiskScout.Services.IAppxEnumerator"/>'s yield shape.</summary>
public sealed record AppxEntry(string PackageFullName, string? PackageFamilyName, string? Publisher, string? InstallLocation);

/// <summary>One scheduled task. Tuple-equivalent to
/// <see cref="DiskScout.Services.IScheduledTaskEnumerator"/>'s yield shape.</summary>
public sealed record ScheduledTaskEntry(string TaskPath, string? Author, string? ActionPath);
