using System.IO;

namespace DiskScout.Services;

/// <summary>
/// Walks up generic-leaf directories until reaching a "significant" ancestor.
/// Used by the orphan pipeline's stage [2] to normalize candidate paths whose
/// leaf is non-discriminating (Logs / Cache / en-us / Updates / ...) up to the
/// meaningful vendor folder above so downstream stages match against a stable
/// vendor root rather than its volatile log subfolder.
/// </summary>
public sealed class ParentContextAnalyzer : IParentContextAnalyzer
{
    /// <summary>
    /// Generic leaf names. Case-insensitive. Includes English locale variants
    /// (en-us, fr-fr, de-de, es-es, it-it, ja-jp, ko-kr, pt-br, ru-ru, zh-cn,
    /// zh-tw) as exact strings — no regex, no language code parsing.
    /// </summary>
    private static readonly HashSet<string> GenericLeaves = new(StringComparer.OrdinalIgnoreCase)
    {
        // Volatile subfolders.
        // Note: "Downloads", "Update", "Updates" intentionally NOT included —
        // empirically (Phase 10 corpus 365 audit), walking up from these
        // vendor sub-paths over-matches the parent vendor's services / drivers,
        // collapsing the score to Critique when the human verdict is Aucun
        // (vendor's Downloads folder happens to be empty residue).
        "Logs", "Log", "Cache", "Caches", "Settings", "Setting", "Config",
        "Installer", "Installers", "Components",
        "sym", "Symbols",
        "jsonoutput", "output", "storage",
        "scripting", "uscripting",
        "policy", "tzdata",
        "Common", "Images", "LangResources", "Resources",
        // Locale codes — exact string match (case-insensitive).
        "en-us", "fr-fr", "de-de", "es-es", "it-it",
        "ja-jp", "ko-kr", "pt-br", "ru-ru", "zh-cn", "zh-tw",
    };

    public string GetSignificantParent(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return fullPath ?? string.Empty;

        var current = fullPath.TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(current))
            return fullPath;

        while (true)
        {
            var leaf = Path.GetFileName(current);

            // Reached the drive root ("C:") or an empty leaf — stop.
            if (string.IsNullOrEmpty(leaf))
                return current;

            // Leaf is significant — return current path.
            if (!GenericLeaves.Contains(leaf))
                return current;

            // Leaf is generic — walk up one level.
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                return current;

            current = parent;
        }
    }
}
