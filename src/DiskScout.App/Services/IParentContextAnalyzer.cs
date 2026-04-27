namespace DiskScout.Services;

/// <summary>
/// Walks up generic-leaf parent directories to find the first "significant" ancestor.
/// Used by the orphan pipeline's stage [2] to normalize candidate paths whose leaf
/// is a non-discriminating folder name (Logs, Cache, en-us, Updates, ...) up to
/// the meaningful vendor directory above.
/// </summary>
/// <remarks>
/// The "generic leaves" set (case-insensitive, ASCII forms only) is:
/// <c>Logs, Log, Cache, Caches, Settings, Setting, Config, Updates, Update,
/// Download, Downloads, Installer, Installers, Components, sym, Symbols,
/// jsonoutput, output, storage, scripting, uscripting, policy, tzdata, Common,
/// Images, LangResources, Resources, en-us, fr-fr, de-de, es-es, it-it, ja-jp,
/// ko-kr, pt-br, ru-ru, zh-cn, zh-tw</c>.
/// </remarks>
public interface IParentContextAnalyzer
{
    /// <summary>
    /// Returns the path of the first ancestor whose leaf name is NOT in the
    /// "generic leaves" set, or the original path if none of its ancestors
    /// are generic. Walks iteratively from leaf to root; stops at the drive
    /// root or empty path.
    /// </summary>
    string GetSignificantParent(string fullPath);
}
