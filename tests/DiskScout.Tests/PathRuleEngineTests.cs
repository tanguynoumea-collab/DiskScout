using System.IO;
using System.Reflection;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// Tests cover embedded resource enumeration (5 PathRules JSON files), LoadAsync
/// with no user folder, user-rule add + override semantics, malformed-JSON
/// resilience, prefix matching for OsCriticalDoNotPropose / PackageCache /
/// VendorShared categories, no-match path, and specificity ordering.
/// </summary>
public class PathRuleEngineTests : IDisposable
{
    private readonly string _tempUserRulesFolder;
    private readonly ILogger _logger;
    private readonly Assembly _appAssembly;

    public PathRuleEngineTests()
    {
        _tempUserRulesFolder = Path.Combine(
            Path.GetTempPath(),
            "DiskScoutPathRulesTests_" + Guid.NewGuid().ToString("N"));
        // Intentionally do NOT pre-create the folder — the no-user-folder test verifies that path.
        _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
        _appAssembly = typeof(PathRuleEngine).Assembly;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempUserRulesFolder, recursive: true); } catch { /* best-effort */ }
    }

    private PathRuleEngine BuildEngine() =>
        new PathRuleEngine(_logger, _tempUserRulesFolder, _appAssembly);

    // -------------------------------------------------------------------------
    // Test 1: Embedded resource enumeration finds the 5 PathRules JSONs (the
    //         sibling aliases.json — added by Plan 10-03 — shares the same
    //         Resources/PathRules folder via the .csproj glob but is structurally
    //         a PublisherAlias[] catalog, not a PathRule[] catalog. The
    //         PathRuleEngine's defensive JsonException catch + empty-Id skip
    //         absorbs it gracefully (verified by the LoadAsync_NoUserFolder…
    //         test below). Here we assert the 5 *PathRule* JSONs are present.
    // -------------------------------------------------------------------------
    [Fact]
    public void ManifestResources_ContainExactly5PathRules()
    {
        var pathRulesResources = _appAssembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith("DiskScout.Resources.PathRules.", StringComparison.Ordinal)
                && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !n.EndsWith("aliases.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        pathRulesResources.Should().HaveCount(5);
        pathRulesResources.Should().Contain(n => n.EndsWith("os-critical.json"));
        pathRulesResources.Should().Contain(n => n.EndsWith("package-cache.json"));
        pathRulesResources.Should().Contain(n => n.EndsWith("driver-data.json"));
        pathRulesResources.Should().Contain(n => n.EndsWith("corporate-agent.json"));
        pathRulesResources.Should().Contain(n => n.EndsWith("vendor-shared.json"));
    }

    // -------------------------------------------------------------------------
    // Test 2: AppPaths.PathRulesFolder + AppPaths.AuditFolder both auto-create.
    // -------------------------------------------------------------------------
    [Fact]
    public void AppPaths_PathRulesFolderAndAuditFolder_AutoCreate()
    {
        var pathRulesFolder = DiskScout.Helpers.AppPaths.PathRulesFolder;
        var auditFolder = DiskScout.Helpers.AppPaths.AuditFolder;

        pathRulesFolder.Should().EndWith(@"DiskScout\path-rules");
        auditFolder.Should().EndWith(@"DiskScout\audits");
        Directory.Exists(pathRulesFolder).Should().BeTrue();
        Directory.Exists(auditFolder).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Test 3: LoadAsync with no user folder loads only embedded rules.
    //         Each rule has Id, PathPattern, and a valid Category.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_NoUserFolder_LoadsEmbeddedRulesOnly()
    {
        var engine = BuildEngine();

        await engine.LoadAsync();

        engine.AllRules.Should().NotBeEmpty();
        // Sanity: at least one rule per category should be present.
        engine.AllRules.Should().Contain(r => r.Category == PathCategory.OsCriticalDoNotPropose);
        engine.AllRules.Should().Contain(r => r.Category == PathCategory.PackageCache);
        engine.AllRules.Should().Contain(r => r.Category == PathCategory.DriverData);
        engine.AllRules.Should().Contain(r => r.Category == PathCategory.CorporateAgent);
        engine.AllRules.Should().Contain(r => r.Category == PathCategory.VendorShared);
        engine.AllRules.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Id) && !string.IsNullOrWhiteSpace(r.PathPattern));
    }

    // -------------------------------------------------------------------------
    // Test 4: User rule with NEW Id is added on top of the embedded set.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_UserRuleWithNewId_IsMergedIntoAllRules()
    {
        Directory.CreateDirectory(_tempUserRulesFolder);
        var userRulePath = Path.Combine(_tempUserRulesFolder, "user-test.json");
        await File.WriteAllTextAsync(userRulePath, """
            {
              "id": "user-vendor-fooinc",
              "pathPattern": "%ProgramData%\\FooInc",
              "category": "VendorShared",
              "minRiskFloor": null,
              "reason": "Test vendor"
            }
            """);

        var engine = BuildEngine();
        await engine.LoadAsync();

        engine.AllRules.Should().Contain(r => r.Id == "user-vendor-fooinc");
        var rule = engine.AllRules.Single(r => r.Id == "user-vendor-fooinc");
        rule.Category.Should().Be(PathCategory.VendorShared);
        rule.PathPattern.Should().Be(@"%ProgramData%\FooInc");
    }

    // -------------------------------------------------------------------------
    // Test 5: User rule with the SAME Id as an embedded rule OVERRIDES it
    //         (last-write-wins).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_UserRuleSharingEmbeddedId_OverridesEmbedded()
    {
        Directory.CreateDirectory(_tempUserRulesFolder);
        // Use the embedded id "vendor-adobe" and override with a different pattern.
        var overrideJson = """
            {
              "id": "vendor-adobe",
              "pathPattern": "X:\\custom\\adobe",
              "category": "VendorShared",
              "minRiskFloor": "Eleve",
              "reason": "OVERRIDDEN"
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_tempUserRulesFolder, "adobe-override.json"), overrideJson);

        var engine = BuildEngine();
        await engine.LoadAsync();

        var adobe = engine.AllRules.Single(r => r.Id == "vendor-adobe");
        adobe.PathPattern.Should().Be(@"X:\custom\adobe");
        adobe.MinRiskFloor.Should().Be(RiskLevel.Eleve);
        adobe.Reason.Should().Be("OVERRIDDEN");
    }

    // -------------------------------------------------------------------------
    // Test 6: Malformed JSON in user folder is logged + skipped, never thrown.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_MalformedUserJson_DoesNotThrow_AndStillLoadsEmbedded()
    {
        Directory.CreateDirectory(_tempUserRulesFolder);
        var badPath = Path.Combine(_tempUserRulesFolder, "bad.json");
        await File.WriteAllTextAsync(badPath, "{ this is not valid json at all");

        var engine = BuildEngine();
        Func<Task> act = () => engine.LoadAsync();

        await act.Should().NotThrowAsync();
        engine.AllRules.Should().NotBeEmpty(); // embedded still loaded
    }

    // -------------------------------------------------------------------------
    // Test 7: Match returns OsCriticalDoNotPropose for C:\Windows\System32\foo.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Match_System32Path_ReturnsOsCriticalRule()
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var winDir = Environment.GetEnvironmentVariable("WinDir") ?? @"C:\Windows";
        var probe = Path.Combine(winDir, "System32", "foo");

        var hits = engine.Match(probe);

        hits.Should().NotBeEmpty();
        hits.Should().Contain(h => h.Category == PathCategory.OsCriticalDoNotPropose);
        // Most specific hit (longest expanded pattern) should be OsCritical.
        hits[0].Category.Should().Be(PathCategory.OsCriticalDoNotPropose);
    }

    // -------------------------------------------------------------------------
    // Test 8: Match returns PackageCache rule with MinRiskFloor=Eleve for a
    //         %ProgramData%\Package Cache\... path. Verify both that the rule
    //         hits AND that the PathRule object carries MinRiskFloor=Eleve.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Match_PackageCache_ReturnsRuleWithEleveFloor()
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
        var probe = Path.Combine(programData, "Package Cache", "{some-guid}", "vc_redist.x64.exe");

        var hits = engine.Match(probe);
        hits.Should().NotBeEmpty();

        // PackageCache hit must be present (multiple categories may match this path).
        hits.Should().Contain(h => h.Category == PathCategory.PackageCache);

        // The corresponding PathRule must declare MinRiskFloor=Eleve.
        var pkgRules = engine.AllRules
            .Where(r => r.Category == PathCategory.PackageCache && r.PathPattern.Contains("Package Cache", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        pkgRules.Should().NotBeEmpty();
        pkgRules.Should().OnlyContain(r => r.MinRiskFloor == RiskLevel.Eleve);
    }

    // -------------------------------------------------------------------------
    // Test 9: Match on a path not covered by any rule returns empty list.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Match_PathNotCoveredByAnyRule_ReturnsEmpty()
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var hits = engine.Match(@"C:\Users\Public\Documents\GenuineResidueVendorXYZ\file.dat");

        hits.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Test 10: Match returns multiple rules sorted by specificity descending
    //          when patterns nest. %ProgramData%\Microsoft\Crypto and
    //          %ProgramData%\Microsoft\Vault are both OsCritical, but a path
    //          like "%ProgramData%\Microsoft\Crypto\Keys\foo" only matches
    //          Crypto. We test nesting with two carved rules: "X" and "X\sub"
    //          via a user file so we don't depend on embedded rule co-incidence.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Match_NestingPatterns_OrderedBySpecificityDescending()
    {
        Directory.CreateDirectory(_tempUserRulesFolder);
        var userJson = """
            [
              {
                "id": "test-broad",
                "pathPattern": "C:\\TestRoot",
                "category": "VendorShared",
                "minRiskFloor": null,
                "reason": "broad"
              },
              {
                "id": "test-narrow",
                "pathPattern": "C:\\TestRoot\\Sub",
                "category": "PackageCache",
                "minRiskFloor": "Eleve",
                "reason": "narrow"
              }
            ]
            """;
        await File.WriteAllTextAsync(Path.Combine(_tempUserRulesFolder, "nest.json"), userJson);

        var engine = BuildEngine();
        await engine.LoadAsync();

        var hits = engine.Match(@"C:\TestRoot\Sub\file.dat");

        hits.Should().HaveCountGreaterOrEqualTo(2);
        hits[0].RuleId.Should().Be("test-narrow");   // longer pattern = more specific = first
        hits[1].RuleId.Should().Be("test-broad");
    }

    // -------------------------------------------------------------------------
    // Test 11: Match on null/empty/whitespace returns empty list.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Match_NullOrEmptyPath_ReturnsEmpty(string? path)
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var hits = engine.Match(path!);

        hits.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Test 12: corporate-agent rules ALL declare MinRiskFloor=Eleve.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_CorporateAgentRules_AllDeclareEleveFloor()
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var corpRules = engine.AllRules
            .Where(r => r.Category == PathCategory.CorporateAgent)
            .ToArray();

        corpRules.Should().NotBeEmpty();
        corpRules.Should().OnlyContain(r => r.MinRiskFloor == RiskLevel.Eleve);
    }
}
