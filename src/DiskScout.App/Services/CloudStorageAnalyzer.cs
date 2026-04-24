using System.IO;
using System.Text.RegularExpressions;
using DiskScout.Models;
using Microsoft.Win32;
using Serilog;

namespace DiskScout.Services;

public sealed partial class CloudStorageAnalyzer : ICloudStorageAnalyzer
{
    private readonly ILogger _logger;

    public CloudStorageAnalyzer(ILogger logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<CloudSyncRoot>> AnalyzeAsync(
        IReadOnlyList<FileSystemNode> nodes,
        CancellationToken cancellationToken)
    {
        var roots = DiscoverRoots();
        var results = new List<CloudSyncRoot>();

        foreach (var (path, provider, displayName) in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (physical, logical, hydrated, placeholders, totalFiles) = AggregateUnderPath(nodes, path);
            if (totalFiles == 0 && physical == 0 && logical == 0) continue;

            results.Add(new CloudSyncRoot(
                Provider: provider,
                DisplayName: displayName,
                RootPath: path,
                PhysicalBytes: physical,
                LogicalBytes: logical,
                HydratedFileCount: hydrated,
                PlaceholderFileCount: placeholders,
                TotalFileCount: totalFiles));
        }

        _logger.Information("Cloud storage analysis: {Count} sync roots detected", results.Count);
        return Task.FromResult<IReadOnlyList<CloudSyncRoot>>(results);
    }

    private static (long physical, long logical, int hydrated, int placeholders, int total) AggregateUnderPath(
        IReadOnlyList<FileSystemNode> nodes, string path)
    {
        var trimmed = path.TrimEnd('\\') + "\\";
        long physical = 0, logical = 0;
        int hydrated = 0, placeholders = 0, total = 0;

        foreach (var n in nodes)
        {
            if (n.Kind != FileSystemNodeKind.File) continue;
            if (!n.FullPath.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)) continue;

            total++;
            physical += n.SizeBytes;
            logical += n.LogicalSizeBytes > 0 ? n.LogicalSizeBytes : n.SizeBytes;

            if (n.IsCloudPlaceholder) placeholders++;
            else hydrated++;
        }

        return (physical, logical, hydrated, placeholders, total);
    }

    private IEnumerable<(string Path, CloudProvider Provider, string DisplayName)> DiscoverRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, provider, display) in DiscoverFromRegistry())
        {
            if (seen.Add(path)) yield return (path, provider, display);
        }

        foreach (var (path, provider, display) in DiscoverFromUserProfile())
        {
            if (seen.Add(path)) yield return (path, provider, display);
        }
    }

    private IEnumerable<(string Path, CloudProvider Provider, string DisplayName)> DiscoverFromRegistry()
    {
        // OneDrive per-user accounts: HKCU\Software\Microsoft\OneDrive\Accounts\<key>\UserFolder
        string[] accountsKeys =
        {
            @"SOFTWARE\Microsoft\OneDrive\Accounts",
        };

        foreach (var accountsPath in accountsKeys)
        {
            RegistryKey? accountsRoot = null;
            try
            {
                accountsRoot = Registry.CurrentUser.OpenSubKey(accountsPath);
                if (accountsRoot is null) continue;

                foreach (var subKeyName in accountsRoot.GetSubKeyNames())
                {
                    using var account = accountsRoot.OpenSubKey(subKeyName);
                    if (account is null) continue;

                    var userFolder = account.GetValue("UserFolder") as string;
                    if (string.IsNullOrWhiteSpace(userFolder) || !Directory.Exists(userFolder)) continue;

                    var displayName = (account.GetValue("UserEmail") as string)
                        ?? (account.GetValue("DisplayName") as string)
                        ?? subKeyName;

                    var provider = subKeyName switch
                    {
                        "Personal" => CloudProvider.OneDrivePersonal,
                        var s when s.StartsWith("Business", StringComparison.OrdinalIgnoreCase) => CloudProvider.OneDriveBusiness,
                        _ => CloudProvider.OneDriveBusiness,
                    };

                    yield return (userFolder, provider, $"{provider} — {displayName}");

                    // Scan siblings at user profile level that belong to the same tenant (SharePoint libraries)
                    var parent = Path.GetDirectoryName(userFolder);
                    if (parent is not null && Directory.Exists(parent))
                    {
                        foreach (var sibling in EnumerateSharePointSiblings(parent, displayName))
                        {
                            if (!string.Equals(sibling.Path, userFolder, StringComparison.OrdinalIgnoreCase))
                            {
                                yield return sibling;
                            }
                        }
                    }
                }
            }
            finally
            {
                accountsRoot?.Dispose();
            }
        }
    }

    private IEnumerable<(string Path, CloudProvider Provider, string DisplayName)> DiscoverFromUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile) || !Directory.Exists(userProfile)) yield break;

        foreach (var dir in SafeEnumerateDirs(userProfile))
        {
            var name = Path.GetFileName(dir);
            if (name is null) continue;

            if (name.Equals("OneDrive", StringComparison.OrdinalIgnoreCase))
            {
                yield return (dir, CloudProvider.OneDrivePersonal, "OneDrive — Personnel");
            }
            else if (name.StartsWith("OneDrive - ", StringComparison.OrdinalIgnoreCase))
            {
                var tenant = name.Substring("OneDrive - ".Length);
                yield return (dir, CloudProvider.OneDriveBusiness, $"OneDrive — {tenant}");
            }
            else if (SharePointRegex().IsMatch(name))
            {
                yield return (dir, CloudProvider.SharePoint, $"SharePoint — {name}");
            }
        }
    }

    private IEnumerable<(string Path, CloudProvider Provider, string DisplayName)> EnumerateSharePointSiblings(string parent, string tenantHint)
    {
        foreach (var dir in SafeEnumerateDirs(parent))
        {
            var name = Path.GetFileName(dir);
            if (name is null) continue;

            if (SharePointRegex().IsMatch(name))
            {
                yield return (dir, CloudProvider.SharePoint, $"SharePoint — {name}");
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirs(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    [GeneratedRegex(@".+( - |—).+", RegexOptions.IgnoreCase)]
    private static partial Regex SharePointRegex();
}
