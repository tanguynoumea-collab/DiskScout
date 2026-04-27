using System.Diagnostics;
using System.Text.Json;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Production <see cref="IAppxEnumerator"/> that shells out to PowerShell to invoke
/// <c>Get-AppxPackage -AllUsers | Select-Object PackageFullName,PackageFamilyName,Publisher,InstallLocation | ConvertTo-Json -Depth 2</c>
/// and parses the JSON output. Process timeout: 15 seconds (Get-AppxPackage is slower
/// than pnputil — typical 1-3s on a clean machine, up to 10s on heavily-customized
/// corporate images).
/// </summary>
/// <remarks>
/// <para>
/// On parse failure or process failure, logs a Warning and yields nothing — never
/// throws. The single-package edge case (PowerShell emits a JSON object instead of
/// an array when only one package is returned) is handled in <see cref="ParseJson"/>.
/// </para>
/// <para>
/// Approach chosen: shell-out to PowerShell rather than P/Invoke against the
/// <c>Windows.Management.Deployment.PackageManager</c> WinRT API because (a) the
/// WinRT projection adds significant binary weight to the single-file publish,
/// (b) PowerShell is available on every Windows install since 7, and (c) the
/// surface is mockable behind <see cref="IAppxEnumerator"/> — correctness +
/// testability beat raw perf for this background indexing pass (results are
/// cached for 5 minutes by the snapshot provider).
/// </para>
/// </remarks>
public sealed class AppxEnumerator : IAppxEnumerator
{
    private const int TimeoutMs = 15_000;

    private const string PowerShellCommand =
        "Get-AppxPackage -AllUsers | Select-Object PackageFullName,PackageFamilyName,Publisher,InstallLocation | ConvertTo-Json -Depth 2";

    private readonly ILogger _logger;

    public AppxEnumerator(ILogger logger) => _logger = logger;

    public IEnumerable<(string PackageFullName, string? PackageFamilyName, string? Publisher, string? InstallLocation)> EnumerateAppxPackages()
    {
        string output;
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"{PowerShellCommand}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) yield break;

            output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(TimeoutMs))
            {
                try { p.Kill(); } catch { /* best-effort */ }
                _logger.Warning("Get-AppxPackage timed out after {TimeoutMs}ms", TimeoutMs);
                yield break;
            }
            if (p.ExitCode != 0)
            {
                _logger.Warning("Get-AppxPackage exited with code {ExitCode}", p.ExitCode);
                yield break;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "powershell.exe Get-AppxPackage failed to launch");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(output)) yield break;

        foreach (var entry in ParseJson(output))
            yield return entry;
    }

    private IEnumerable<(string, string?, string?, string?)> ParseJson(string json)
    {
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to parse Get-AppxPackage JSON output");
            yield break;
        }

        using (doc)
        {
            var root = doc.RootElement;
            // Single-package case: PowerShell emits an object directly. Multi-package
            // case: PowerShell emits an array of objects. Handle both.
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryReadPackage(root, out var entry))
                    yield return entry;
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (TryReadPackage(item, out var entry))
                        yield return entry;
                }
            }
        }
    }

    private static bool TryReadPackage(
        JsonElement obj,
        out (string PackageFullName, string? PackageFamilyName, string? Publisher, string? InstallLocation) result)
    {
        string? full = ReadString(obj, "PackageFullName");
        if (string.IsNullOrWhiteSpace(full))
        {
            result = default;
            return false;
        }
        string? family = ReadString(obj, "PackageFamilyName");
        string? publisher = ReadString(obj, "Publisher");
        string? location = ReadString(obj, "InstallLocation");
        result = (full!, family, publisher, location);
        return true;
    }

    private static string? ReadString(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}
