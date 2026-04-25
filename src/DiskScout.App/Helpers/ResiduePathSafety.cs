namespace DiskScout.Helpers;

/// <summary>
/// Non-bypassable critical-path whitelist for the residue scanner.
/// Every residue finding emitted by <see cref="DiskScout.Services.IResidueScanner"/> MUST first
/// pass through <see cref="IsSafeToPropose"/>; service-name findings additionally pass through
/// <see cref="IsSafeServiceName"/>. The lists are explicit, grep-stable constants — they are
/// the product's last line of defence against accidentally proposing a system-critical path.
/// </summary>
/// <remarks>
/// CONTEXT.md "Risks to Address in Plan" mandates: a false positive on residue scan = potential
/// user-data loss. The whitelist below is intentionally aggressive (it OVER-rejects rather than
/// UNDER-rejects). If a future legitimate vendor path is wrongly excluded, prefer adding a
/// publisher rule (Plan 09-04) over weakening this guard.
/// </remarks>
public static class ResiduePathSafety
{
    /// <summary>
    /// Filesystem critical-path denylist — substring match, case-insensitive.
    /// </summary>
    public static readonly string[] CriticalFilesystemSubstrings =
    {
        @"\Windows\System32",
        @"\Windows\SysWOW64",
        @"\Windows\WinSxS",
        @"\Windows\drivers",
        @"\Windows\Boot",
        @"\Windows\Fonts",
        @"\Windows\Microsoft.NET",
        @"\Windows\servicing",
        @"\Windows\assembly",
        @"\Program Files\Common Files",
        @"\Program Files (x86)\Common Files",
        @"\Program Files\Windows Defender",
        @"\Program Files (x86)\Windows Defender",
        @"\Program Files\Windows Security",
        @"\Program Files\Windows NT",
        @"\Program Files (x86)\Windows NT",
        @"\Program Files\Microsoft\Edge",
        @"\Program Files (x86)\Microsoft\Edge",
        @"\Program Files\WindowsApps",
    };

    /// <summary>
    /// Registry critical-key denylist — prefix match, case-insensitive.
    /// </summary>
    public static readonly string[] CriticalRegistryPrefixes =
    {
        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip",
        @"HKLM\SYSTEM\CurrentControlSet\Services\Dhcp",
        @"HKLM\SYSTEM\CurrentControlSet\Services\Dnscache",
        @"HKLM\SYSTEM\CurrentControlSet\Services\WinDefend",
        @"HKLM\SYSTEM\CurrentControlSet\Services\MsSecCore",
        @"HKLM\SYSTEM\CurrentControlSet\Services\WdNisSvc",
        @"HKLM\SYSTEM\CurrentControlSet\Services\Sense",
        @"HKLM\SYSTEM\CurrentControlSet\Services\TrustedInstaller",
        @"HKLM\SYSTEM\CurrentControlSet\Services\EventLog",
        @"HKLM\SYSTEM\CurrentControlSet\Services\RpcSs",
        @"HKLM\SYSTEM\CurrentControlSet\Services\LSM",
        @"HKLM\SYSTEM\CurrentControlSet\Services\MpsSvc",
        @"HKLM\SOFTWARE\Microsoft\Windows Defender",
        @"HKLM\SOFTWARE\Microsoft\Windows Security",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Class",
        @"HKLM\SYSTEM\Setup",
    };

    /// <summary>
    /// Critical Windows service names that must NEVER be proposed for deletion,
    /// regardless of whether the publisher heuristic matches.
    /// </summary>
    public static readonly string[] CriticalServiceNamePatterns =
    {
        "WinDefend",
        "MsSecCore",
        "WdNisSvc",
        "Sense",
        "WdFilter",
        "MpsSvc",
        "BFE",
        "EventLog",
        "RpcSs",
        "DcomLaunch",
        "TrustedInstaller",
        "Dhcp",
        "Dnscache",
        "LSM",
        "lsass",
    };

    /// <summary>
    /// True if <paramref name="path"/> may be proposed to the user as residue.
    /// False on null/whitespace, on any filesystem path containing a critical substring,
    /// or on any registry path starting with a critical prefix.
    /// </summary>
    public static bool IsSafeToPropose(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        foreach (var substring in CriticalFilesystemSubstrings)
        {
            if (path.Contains(substring, StringComparison.OrdinalIgnoreCase)) return false;
        }

        foreach (var prefix in CriticalRegistryPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    /// <summary>
    /// True if a Windows service named <paramref name="serviceName"/> may be proposed for residue removal.
    /// False on null/whitespace, or on any case-insensitive equality match against
    /// <see cref="CriticalServiceNamePatterns"/>.
    /// </summary>
    public static bool IsSafeServiceName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return false;

        foreach (var pattern in CriticalServiceNamePatterns)
        {
            if (string.Equals(serviceName, pattern, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
