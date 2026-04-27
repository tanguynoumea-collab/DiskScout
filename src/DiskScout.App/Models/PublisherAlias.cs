namespace DiskScout.Models;

/// <summary>
/// One entry of the curated alias catalog. Maps a canonical publisher or product
/// name to a list of common short forms / brand variations / SKU suffixes that
/// real-world filesystem folders or registry DisplayName values can take.
/// </summary>
/// <param name="Canonical">
/// The canonical name as it should appear after resolution (e.g., "Revit",
/// "Adobe Inc.", "Microsoft Corporation"). This is what the resolver returns
/// in <c>MatchedCanonical</c> when an alias hit fires.
/// </param>
/// <param name="Aliases">
/// The list of alias short forms / variations that should resolve to
/// <see cref="Canonical"/> (e.g., for "Revit": <c>["RVT", "Autodesk Revit",
/// "Revit Architecture", "Revit Structure", "Revit MEP", "Revit LT"]</c>).
/// Matched case-insensitively.
/// </param>
/// <param name="Type">
/// Discriminator: <c>"publisher"</c> for a publisher canonical (Adobe Inc.,
/// Microsoft Corporation, …) or <c>"product"</c> for a product canonical (Revit,
/// Visual Studio, …). Allows downstream consumers to weight publisher vs product
/// matches differently. May be null for legacy entries.
/// </param>
public sealed record PublisherAlias(string Canonical, string[] Aliases, string? Type);
