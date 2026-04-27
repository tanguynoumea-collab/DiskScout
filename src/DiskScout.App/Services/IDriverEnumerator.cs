namespace DiskScout.Services;

/// <summary>
/// Abstraction over the Windows driver enumerator (third-party drivers in the Windows
/// driver store). Lets tests inject deterministic driver fixtures without shelling out
/// to <c>pnputil.exe</c>. Used by the AppData orphan-detection pipeline (Phase 10) to
/// rule out folders whose parent vendor publishes an installed driver.
/// </summary>
/// <remarks>
/// New in Plan 10-02. Production implementation is <see cref="DriverEnumerator"/> which
/// shells out to <c>pnputil.exe /enum-drivers /format:json</c> with a 10s timeout and
/// falls back to plain-text parsing on older Windows builds (&lt; 10 v1903).
/// </remarks>
public interface IDriverEnumerator
{
    /// <summary>
    /// Tuple stream: (PublishedName, OriginalFileName, Provider, ClassName).
    /// <c>PublishedName</c> is the OEMxx.inf filename Windows assigns when staging a driver;
    /// <c>OriginalFileName</c> is the .inf filename as shipped by the vendor;
    /// <c>Provider</c> is the company name; <c>ClassName</c> is the driver class
    /// (e.g. "Display", "Printer", "USB").
    /// </summary>
    IEnumerable<(string PublishedName, string? OriginalFileName, string? Provider, string? ClassName)> EnumerateDrivers();
}
