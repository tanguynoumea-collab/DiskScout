using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Serilog;

namespace DiskScout.Services;

/// <summary>
/// Production <see cref="IDriverEnumerator"/> that shells out to
/// <c>pnputil.exe /enum-drivers /format:json</c> (Windows 10 v1903+) and parses
/// the JSON output. Falls back to a plain-text line-prefix parser on older
/// Windows builds where <c>/format:json</c> is not supported.
/// </summary>
/// <remarks>
/// <para>
/// Process timeout: 10 seconds (same convention as
/// <c>ResidueScanner.SchTasksEnumerator</c>). On timeout, logs a Warning and
/// yields nothing — never throws.
/// </para>
/// <para>
/// Approach chosen: shell-out + JSON parse rather than P/Invoke against the
/// <c>SetupAPI</c> / <c>CfgMgr32</c> functions because (a) <c>pnputil</c> is
/// part of every Windows install since Vista, (b) JSON output (when supported)
/// gives us all 4 fields in one call, and (c) the surface is mockable behind
/// <see cref="IDriverEnumerator"/> — correctness + testability beat raw perf
/// for this background indexing pass (results are cached for 5 minutes by the
/// snapshot provider).
/// </para>
/// </remarks>
public sealed class DriverEnumerator : IDriverEnumerator
{
    private const int TimeoutMs = 10_000;

    private readonly ILogger _logger;

    public DriverEnumerator(ILogger logger) => _logger = logger;

    public IEnumerable<(string PublishedName, string? OriginalFileName, string? Provider, string? ClassName)> EnumerateDrivers()
    {
        // Try JSON-mode first (Windows 10 v1903+). If pnputil rejects the flag with a
        // non-zero exit, retry in plain-text mode.
        var (jsonOk, jsonOutput) = TryRunPnpUtil("/enum-drivers /format:json");
        if (jsonOk && !string.IsNullOrWhiteSpace(jsonOutput))
        {
            foreach (var entry in ParseJson(jsonOutput))
                yield return entry;
            yield break;
        }

        var (textOk, textOutput) = TryRunPnpUtil("/enum-drivers");
        if (!textOk || string.IsNullOrWhiteSpace(textOutput))
        {
            yield break;
        }

        foreach (var entry in ParseText(textOutput))
            yield return entry;
    }

    private (bool Ok, string Output) TryRunPnpUtil(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("pnputil.exe", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (false, string.Empty);

            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(TimeoutMs))
            {
                try { p.Kill(); } catch { /* best-effort */ }
                _logger.Warning("pnputil.exe {Args} timed out after {TimeoutMs}ms", args, TimeoutMs);
                return (false, string.Empty);
            }
            // pnputil returns 0 on success, non-zero on flag rejection.
            return (p.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "pnputil.exe {Args} failed to launch", args);
            return (false, string.Empty);
        }
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
            _logger.Warning(ex, "Failed to parse pnputil JSON output");
            yield break;
        }

        // Expected shape:
        // {
        //   "MicrosoftDriverPackages": [
        //     { "Published Name": "oem01.inf", "Original Name": "vendor.inf",
        //       "Provider Name": "Vendor Inc.", "Class Name": "Display", ... }
        //   ]
        // }
        // We tolerate both spaced ("Published Name") and camelCase ("publishedName") keys.
        using (doc)
        {
            JsonElement root = doc.RootElement;
            JsonElement packages = default;
            bool found = false;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        packages = prop.Value;
                        found = true;
                        break;
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                packages = root;
                found = true;
            }

            if (!found) yield break;

            foreach (var pkg in packages.EnumerateArray())
            {
                if (pkg.ValueKind != JsonValueKind.Object) continue;
                string? published = ReadString(pkg, "Published Name", "publishedName", "PublishedName");
                if (string.IsNullOrWhiteSpace(published)) continue;
                string? original = ReadString(pkg, "Original Name", "originalName", "OriginalName");
                string? provider = ReadString(pkg, "Provider Name", "providerName", "ProviderName");
                string? className = ReadString(pkg, "Class Name", "className", "ClassName");
                yield return (published!, original, provider, className);
            }
        }
    }

    private static string? ReadString(JsonElement obj, params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            if (obj.TryGetProperty(key, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
                if (prop.ValueKind == JsonValueKind.Null)
                    return null;
            }
        }
        return null;
    }

    private static IEnumerable<(string, string?, string?, string?)> ParseText(string text)
    {
        // pnputil legacy text format groups attributes per package, separated by blank lines:
        //
        // Published Name:     oem01.inf
        // Original Name:      vendor.inf
        // Provider Name:      Vendor Inc.
        // Class Name:         Display
        // Class GUID:         {4d36e968-...}
        // Driver Version:     07/12/2023 31.0.101.4255
        // Signer Name:        Microsoft Windows Hardware Compatibility Publisher
        //
        // Published Name:     oem02.inf
        // ...
        string? published = null, original = null, provider = null, className = null;

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!string.IsNullOrWhiteSpace(published))
                {
                    yield return (published!, original, provider, className);
                }
                published = original = provider = className = null;
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0 || colon >= line.Length - 1) continue;

            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            if (string.IsNullOrEmpty(value)) continue;

            if (key.Equals("Published Name", StringComparison.OrdinalIgnoreCase))
                published = value;
            else if (key.Equals("Original Name", StringComparison.OrdinalIgnoreCase))
                original = value;
            else if (key.Equals("Provider Name", StringComparison.OrdinalIgnoreCase))
                provider = value;
            else if (key.Equals("Class Name", StringComparison.OrdinalIgnoreCase))
                className = value;
        }

        // Trailing record (no terminating blank line).
        if (!string.IsNullOrWhiteSpace(published))
        {
            yield return (published!, original, provider, className);
        }
    }
}
