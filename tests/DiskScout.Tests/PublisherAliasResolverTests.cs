using System.IO;
using System.Reflection;
using System.Text;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// PublisherAliasResolver coverage — exact alias path, token alias expansion,
/// FuzzyMatcher fallback, threshold gate, real-world BCF Manager case-study,
/// case-insensitivity, cancellation, idempotent load, and resilience to bad /
/// missing JSON.
/// </summary>
public class PublisherAliasResolverTests
{
    private readonly ILogger _logger;
    private readonly Assembly _appAssembly;

    public PublisherAliasResolverTests()
    {
        _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
        _appAssembly = typeof(PublisherAliasResolver).Assembly;
    }

    private PublisherAliasResolver BuildResolver() => new(_logger, _appAssembly);

    // -------------------------------------------------------------------------
    // Test 1: LoadAsync deserializes the embedded aliases.json into >= 15
    //         canonical entries (matching the curated catalog from Plan 10-03).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_LoadsEmbeddedAliasCatalog()
    {
        var resolver = BuildResolver();

        await resolver.LoadAsync();

        resolver.CanonicalCount.Should().BeGreaterOrEqualTo(15);
    }

    // -------------------------------------------------------------------------
    // Test 2: Exact whole-string alias match returns score 1.0 with the
    //         canonical surfaced. "RVT" → "Revit".
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Resolve_ExactAliasMatch_ReturnsScore1()
    {
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync("RVT", publisher: null, displayName: null);

        result.Should().NotBeNull();
        result!.Value.Score.Should().Be(1.0);
        result.Value.MatchedCanonical.Should().Be("Revit");
    }

    // -------------------------------------------------------------------------
    // Test 3: Token alias expansion — folder "RVT 2025" with display name
    //         "Autodesk Revit 2025" and publisher "Autodesk, Inc." should
    //         resolve via the Revit alias path (RVT token → Revit canonical)
    //         AND/OR the FuzzyMatcher fallback. Either way: a non-null match
    //         with score >= threshold.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Resolve_AliasExpandsAndMatches_RegistryDisplayName()
    {
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(
            folderName: "RVT 2025",
            publisher: "Autodesk, Inc.",
            displayName: "Autodesk Revit 2025");

        result.Should().NotBeNull();
        result!.Value.Score.Should().BeGreaterOrEqualTo(0.7);
        // Token expansion should raise "Revit" into the candidate set, OR the
        // direct fuzzy fallback should match via shared "autodesk"+"revit" tokens.
        var matched = result.Value.MatchedCanonical;
        matched.Should().Match(m =>
            m == "Revit" || m == "Autodesk Revit 2025" || m == "Autodesk, Inc.");
    }

    // -------------------------------------------------------------------------
    // Test 4: No alias declared — falls back to FuzzyMatcher and returns a
    //         positive hit when the publisher/displayName overlaps tokens.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Resolve_NoAlias_FallsBackToFuzzy()
    {
        var resolver = BuildResolver();

        // Folder "Microsoft Visual Studio Code" not literally in the alias
        // table; no alias token leads to a canonical that fuzzy-matches the
        // folder; but the direct fuzzy fallback against the registry
        // displayName matches via shared tokens "microsoft"+"visual"+"studio".
        var result = await resolver.ResolveAsync(
            folderName: "Microsoft Visual Studio Code",
            publisher: "Microsoft Corporation",
            displayName: "Microsoft Visual Studio Code");

        result.Should().NotBeNull();
        result!.Value.Score.Should().BeGreaterOrEqualTo(0.7);
        result.Value.MatchedCanonical.Should().NotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------
    // Test 5: Below-threshold input — random GUID-like folder with no
    //         publisher / displayName overlap returns null.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Resolve_NoMatchBelowThreshold_ReturnsNull()
    {
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(
            folderName: "X9zk-uniqueblob-abc",
            publisher: "Acme Widgets",
            displayName: "Acme Productivity Suite");

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Test 6: Real-world Plan-10 acceptance case — folder "BcfManager" should
    //         resolve to "BCF Manager" via the exact alias entry, and against
    //         a registry displayName "BCF Managers 6.5" the same canonical
    //         should still surface (alias precedence over fuzzy fallback when
    //         alias score is higher).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Resolve_BcfManager_MatchesBCFManagers6_5()
    {
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(
            folderName: "BcfManager",
            publisher: "KUBUS",
            displayName: "BCF Managers 6.5");

        result.Should().NotBeNull();
        result!.Value.Score.Should().BeGreaterOrEqualTo(0.7);
        result.Value.MatchedCanonical.Should().Be("BCF Manager");
        result.Value.Score.Should().Be(1.0); // exact alias hit
    }

    // -------------------------------------------------------------------------
    // Test 7: Case-insensitive alias lookup — "rvt" / "Rvt" / "RVT" all match.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("RVT")]
    [InlineData("rvt")]
    [InlineData("Rvt")]
    [InlineData("rVt")]
    public async Task Resolve_CaseInsensitive(string folder)
    {
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(folder, publisher: null, displayName: null);

        result.Should().NotBeNull();
        result!.Value.MatchedCanonical.Should().Be("Revit");
    }

    // -------------------------------------------------------------------------
    // Test 8: Cancellation honored — a pre-cancelled token throws
    //         OperationCanceledException without falling through to fuzzy.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Resolve_Cancellation_Honored()
    {
        var resolver = BuildResolver();
        await resolver.LoadAsync(); // pre-load so cancellation lands on the resolve path

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => resolver.ResolveAsync(
            "BcfManager",
            publisher: null,
            displayName: null,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -------------------------------------------------------------------------
    // Test 9: LoadAsync is idempotent — second call is a no-op (CanonicalCount
    //         unchanged, no exception).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_Idempotent()
    {
        var resolver = BuildResolver();

        await resolver.LoadAsync();
        var firstCount = resolver.CanonicalCount;

        await resolver.LoadAsync();
        var secondCount = resolver.CanonicalCount;

        firstCount.Should().Be(secondCount);
        firstCount.Should().BeGreaterThan(0);
    }

    // -------------------------------------------------------------------------
    // Test 10: ResolveAsync auto-loads on first call without an explicit
    //          LoadAsync — exact-alias match still returns 1.0.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Resolve_AutoLoadsOnFirstCall()
    {
        var resolver = BuildResolver();
        // No LoadAsync call here — Resolve must auto-load.

        var result = await resolver.ResolveAsync("BcfManager", publisher: null, displayName: null);

        result.Should().NotBeNull();
        result!.Value.MatchedCanonical.Should().Be("BCF Manager");
    }

    // -------------------------------------------------------------------------
    // Test 11: LoadAsync against an assembly with NO aliases.json embedded
    //          resource — resolver still works, but only via the FuzzyMatcher
    //          fallback (alias dict empty). Folder + publisher/displayName
    //          with strong token overlap still resolves.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_AliasesJsonMissing_ReturnsEmpty_FuzzyStillWorks()
    {
        // The xUnit assembly reliably exists at runtime and has zero
        // DiskScout.Resources.PathRules.aliases.json embedded resource.
        var bareAssembly = typeof(FactAttribute).Assembly;
        var resolver = new PublisherAliasResolver(_logger, bareAssembly);

        await resolver.LoadAsync();
        resolver.CanonicalCount.Should().Be(0);

        // FuzzyMatcher fallback — exact match on the publisher/displayName triple.
        var result = await resolver.ResolveAsync(
            folderName: "Adobe Photoshop",
            publisher: "Adobe Inc.",
            displayName: "Adobe Photoshop");

        result.Should().NotBeNull();
        result!.Value.Score.Should().Be(1.0); // exact match in FuzzyMatcher.ComputeMatch
    }

    // -------------------------------------------------------------------------
    // Test 12: LoadAsync with a deliberately malformed aliases.json embedded
    //          resource — the JsonException is caught + logged, alias dict
    //          stays empty, FuzzyMatcher fallback remains functional. Tested
    //          via a stub Assembly subclass that returns garbage bytes for
    //          the aliases.json resource name.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_BadJson_LogsAndContinues()
    {
        var stubAssembly = new BadJsonStubAssembly();
        var resolver = new PublisherAliasResolver(_logger, stubAssembly);

        Func<Task> act = () => resolver.LoadAsync();
        await act.Should().NotThrowAsync();

        resolver.CanonicalCount.Should().Be(0);

        // Fuzzy fallback still operative.
        var result = await resolver.ResolveAsync(
            folderName: "JetBrains Rider",
            publisher: "JetBrains s.r.o.",
            displayName: "JetBrains Rider");
        result.Should().NotBeNull();
        result!.Value.Score.Should().Be(1.0);
    }

    // -------------------------------------------------------------------------
    // Test 13: Empty / whitespace folder name returns null without throwing.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Resolve_EmptyFolder_ReturnsNull(string? folder)
    {
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(folder!, publisher: "Adobe Inc.", displayName: "Adobe Acrobat");

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Test 14: Aliases.json shape contract — entry "BCF Manager" exists with
    //          its three documented aliases. Locks the curated catalog
    //          contents against accidental edits that would break the
    //          downstream pipeline (10-04 / 10-05 / 10-06 corpus tests).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_BcfManagerEntry_HasThreeDocumentedAliases()
    {
        var resolver = BuildResolver();
        await resolver.LoadAsync();

        // "BCF Managers" (the displayName variant) must resolve to the
        // canonical "BCF Manager" via the alias table — score 1.0.
        var result = await resolver.ResolveAsync("BCF Managers", null, null);
        result.Should().NotBeNull();
        result!.Value.MatchedCanonical.Should().Be("BCF Manager");
        result.Value.Score.Should().Be(1.0);

        var kubus = await resolver.ResolveAsync("KUBUS BCF", null, null);
        kubus.Should().NotBeNull();
        kubus!.Value.MatchedCanonical.Should().Be("BCF Manager");
    }

    // -------------------------------------------------------------------------
    // Test 15: Each canonical resolves to itself via the exact-alias path
    //          (canonical-as-alias-of-self contract). Pin a few key entries.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("Autodesk")]
    [InlineData("Microsoft")]
    [InlineData("NVIDIA")]
    [InlineData("Revit")]
    [InlineData("Visual Studio")]
    [InlineData("Navisworks")]
    [InlineData("Dynamo")]
    public async Task Resolve_CanonicalIsAliasOfSelf(string canonical)
    {
        var resolver = BuildResolver();

        var result = await resolver.ResolveAsync(canonical, null, null);

        result.Should().NotBeNull();
        result!.Value.Score.Should().Be(1.0);
        result.Value.MatchedCanonical.Should().Be(canonical);
    }

    // -------------------------------------------------------------------------
    // Test 16: Threshold floor — folder name with only a single weak token
    //          overlap (FuzzyMatcher requires 2+ token intersection AND
    //          ratio >= threshold) returns null.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Resolve_SingleTokenOverlap_BelowThreshold_ReturnsNull()
    {
        var resolver = BuildResolver();

        // No alias hit; FuzzyMatcher requires >=2 token intersection — single
        // shared token "ZScalerXYZ" vs "Zscaler Inc." is not enough.
        var result = await resolver.ResolveAsync(
            folderName: "ZScalerXYZ",
            publisher: "Some Corp",
            displayName: "Some Office Suite");

        result.Should().BeNull();
    }

    // =========================================================================
    // Test stub: an Assembly that pretends to expose the aliases.json
    // embedded resource but returns garbage bytes. Used by Test 12.
    // =========================================================================
    private sealed class BadJsonStubAssembly : Assembly
    {
        private const string Name = "DiskScout.Resources.PathRules.aliases.json";
        private static readonly byte[] GarbageBytes = Encoding.UTF8.GetBytes("{ this is not [valid json");

        public override string[] GetManifestResourceNames() => new[] { Name };

        public override Stream? GetManifestResourceStream(string name)
        {
            if (string.Equals(name, Name, StringComparison.Ordinal))
                return new MemoryStream(GarbageBytes, writable: false);
            return null;
        }
    }
}
