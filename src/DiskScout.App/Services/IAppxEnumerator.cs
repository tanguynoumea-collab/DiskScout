namespace DiskScout.Services;

/// <summary>
/// Abstraction over the Appx (UWP / MSIX) package enumerator. Lets tests inject
/// deterministic Appx fixtures without launching PowerShell. Used by the AppData
/// orphan-detection pipeline (Phase 10) to rule out folders that belong to a still-
/// installed UWP package.
/// </summary>
/// <remarks>
/// New in Plan 10-02. Production implementation is <see cref="AppxEnumerator"/> which
/// shells out to
/// <c>powershell.exe -NoProfile -Command "Get-AppxPackage -AllUsers | ConvertTo-Json"</c>
/// with a 15s timeout. The single-package edge case (PowerShell emits a JSON object
/// instead of an array when only one package matches) is handled by the parser.
/// </remarks>
public interface IAppxEnumerator
{
    /// <summary>
    /// Tuple stream: (PackageFullName, PackageFamilyName, Publisher, InstallLocation).
    /// <c>InstallLocation</c> is the absolute path under
    /// <c>%ProgramFiles%\WindowsApps\</c> (system) or
    /// <c>%LocalAppData%\Packages\</c> (per-user runtime data).
    /// </summary>
    IEnumerable<(string PackageFullName, string? PackageFamilyName, string? Publisher, string? InstallLocation)> EnumerateAppxPackages();
}
