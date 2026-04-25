using System.IO;
using System.Reflection;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// Tests cover schema fidelity (each shipped JSON deserializes), embedded-resource
/// enumeration, user-rules folder loading + override semantics, malformed-JSON
/// resilience, match specificity ranking, and token expansion (env vars + literal tokens).
/// </summary>
public class PublisherRuleEngineTests : IDisposable
{
    private readonly string _tempUserRulesFolder;
    private readonly ILogger _logger;
    private readonly Assembly _appAssembly;

    public PublisherRuleEngineTests()
    {
        _tempUserRulesFolder = Path.Combine(
            Path.GetTempPath(),
            "DiskScoutPublisherRulesTests_" + Guid.NewGuid().ToString("N"));
        // Intentionally do NOT pre-create the folder — Test 1 verifies the no-user-folder path.
        _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
        _appAssembly = typeof(PublisherRuleEngine).Assembly;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempUserRulesFolder, recursive: true); } catch { /* best-effort */ }
    }

    private PublisherRuleEngine BuildEngine() =>
        new PublisherRuleEngine(_logger, _tempUserRulesFolder, _appAssembly);

    // -------------------------------------------------------------------------
    // Test 1: Embedded resource count + names — proves the <EmbeddedResource>
    //         glob in the .csproj actually shipped the 7 rule files.
    // -------------------------------------------------------------------------
    [Fact]
    public void ManifestResources_ContainAtLeast7PublisherRules()
    {
        var publisherResources = _appAssembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith("DiskScout.Resources.PublisherRules.", StringComparison.Ordinal)
                && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        publisherResources.Should().HaveCountGreaterOrEqualTo(7);
        publisherResources.Should().Contain(n => n.EndsWith("adobe.json"));
        publisherResources.Should().Contain(n => n.EndsWith("autodesk.json"));
        publisherResources.Should().Contain(n => n.EndsWith("jetbrains.json"));
        publisherResources.Should().Contain(n => n.EndsWith("mozilla.json"));
        publisherResources.Should().Contain(n => n.EndsWith("microsoft.json"));
        publisherResources.Should().Contain(n => n.EndsWith("steam.json"));
        publisherResources.Should().Contain(n => n.EndsWith("epic.json"));
    }

    // -------------------------------------------------------------------------
    // Test 2: AppPaths.PublisherRulesFolder creates and returns the folder.
    // -------------------------------------------------------------------------
    [Fact]
    public void AppPaths_PublisherRulesFolder_CreatesDirectoryUnderLocalAppData()
    {
        var folder = DiskScout.Helpers.AppPaths.PublisherRulesFolder;

        folder.Should().NotBeNullOrWhiteSpace();
        folder.Should().EndWith(@"DiskScout\publisher-rules");
        Directory.Exists(folder).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Test 3: LoadAsync with no user folder — exactly the embedded rules load.
    //         Each rule has at least one non-empty array among
    //         filesystemPaths/registryPaths/services/scheduledTasks.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_NoUserFolder_LoadsEmbeddedRulesOnly()
    {
        var engine = BuildEngine();

        await engine.LoadAsync();

        engine.AllRules.Should().HaveCount(7);
        engine.AllRules.Should().OnlyContain(r =>
            r.FilesystemPaths.Length > 0
            || r.RegistryPaths.Length > 0
            || r.Services.Length > 0
            || r.ScheduledTasks.Length > 0);

        // Sanity: each shipped rule has a non-empty Id and non-empty PublisherPattern.
        engine.AllRules.Should().OnlyContain(r =>
            !string.IsNullOrWhiteSpace(r.Id) && !string.IsNullOrWhiteSpace(r.PublisherPattern));
    }

    // -------------------------------------------------------------------------
    // Test 4: User rule with a NEW id is added on top of the embedded set.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_UserRuleWithNewId_IsMergedIntoAllRules()
    {
        Directory.CreateDirectory(_tempUserRulesFolder);
        var userRulePath = Path.Combine(_tempUserRulesFolder, "user-test.json");
        await File.WriteAllTextAsync(userRulePath, """
            {
              "id": "user-test",
              "publisherPattern": "(?i)^Test$",
              "displayNamePattern": null,
              "filesystemPaths": ["%LocalAppData%\\TestVendor"],
              "registryPaths": [],
              "services": [],
              "scheduledTasks": []
            }
            """);

        var engine = BuildEngine();
        await engine.LoadAsync();

        engine.AllRules.Should().HaveCount(8);
        engine.AllRules.Should().Contain(r => r.Id == "user-test");
    }

    // -------------------------------------------------------------------------
    // Test 5: User rule with the SAME id as an embedded one OVERRIDES it
    //         (last-write-wins).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_UserRuleSharingEmbeddedId_OverridesEmbedded()
    {
        Directory.CreateDirectory(_tempUserRulesFolder);
        var overrideJson = """
            {
              "id": "adobe",
              "publisherPattern": "(?i)^OVERRIDDEN$",
              "displayNamePattern": null,
              "filesystemPaths": ["X:\\custom\\adobe"],
              "registryPaths": [],
              "services": [],
              "scheduledTasks": []
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_tempUserRulesFolder, "adobe-override.json"), overrideJson);

        var engine = BuildEngine();
        await engine.LoadAsync();

        engine.AllRules.Should().HaveCount(7); // same count — overriden, not added
        var adobe = engine.AllRules.Single(r => r.Id == "adobe");
        adobe.PublisherPattern.Should().Be("(?i)^OVERRIDDEN$");
        adobe.FilesystemPaths.Should().ContainSingle().Which.Should().Be(@"X:\custom\adobe");
    }

    // -------------------------------------------------------------------------
    // Test 6: Malformed JSON in user folder is logged + skipped, never thrown.
    //         (Rule 1 of <important_notes>: "engine never throws on bad input".)
    // -------------------------------------------------------------------------
    [Fact]
    public async Task LoadAsync_MalformedUserJson_DoesNotThrow_AndStillLoadsEmbedded()
    {
        Directory.CreateDirectory(_tempUserRulesFolder);
        var badPath = Path.Combine(_tempUserRulesFolder, "bad.json");
        await File.WriteAllTextAsync(badPath, "\"this is not a publisher rule object\"");

        var engine = BuildEngine();
        Func<Task> act = () => engine.LoadAsync();

        await act.Should().NotThrowAsync();
        engine.AllRules.Should().HaveCount(7); // bad file skipped; 7 embedded still loaded
    }

    // -------------------------------------------------------------------------
    // Test 7: Match returns adobe rule for ("Adobe Inc.", "Acrobat DC").
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Match_AdobePublisher_ReturnsAdobeRule()
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var matches = engine.Match("Adobe Inc.", "Acrobat DC");

        matches.Should().NotBeEmpty();
        matches[0].Rule.Id.Should().Be("adobe");
        matches[0].SpecificityScore.Should().BeGreaterThan(0);
    }

    // -------------------------------------------------------------------------
    // Test 8: Match("Microsoft Corporation", "Excel") returns microsoft-office
    //         with HIGHER specificity than a publisher-only rule. And
    //         Match("Microsoft Corporation", "Visual Studio Code") returns no match
    //         (DisplayName regex rejects).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Match_MicrosoftOffice_DisplayNameNarrowingRanksHigher_AndRejectsNonMatchingDisplayName()
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var hits = engine.Match("Microsoft Corporation", "Excel");
        hits.Should().NotBeEmpty();
        hits[0].Rule.Id.Should().Be("microsoft-office");
        // 10 (publisher base) + 100 (DisplayName-narrowed) = 110.
        hits[0].SpecificityScore.Should().BeGreaterOrEqualTo(110);

        var noHits = engine.Match("Microsoft Corporation", "Visual Studio Code");
        noHits.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Test 9: ExpandTokens replaces {Publisher} and {DisplayName} AFTER env-var expansion.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task ExpandTokens_ReplacesPublisherAndDisplayNameTokens_AfterEnvVarExpansion()
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var expanded = engine.ExpandTokens(
            template: @"%LocalAppData%\{Publisher}\{DisplayName}",
            publisher: "Adobe",
            displayName: "Acrobat DC");

        expanded.Should().NotContain("%LocalAppData%");
        expanded.Should().NotContain("{Publisher}");
        expanded.Should().NotContain("{DisplayName}");
        expanded.Should().EndWith(@"\Adobe\Acrobat DC");
        // %LocalAppData% on Windows expands to ...\AppData\Local
        expanded.Should().Contain(@"AppData\Local");
    }

    // -------------------------------------------------------------------------
    // Test 10 (Theory): ExpandTokens handles each well-known env var without
    //                   leaving a literal %...% in the result.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(@"%ProgramFiles(x86)%\Vendor")]
    [InlineData(@"%ProgramData%\Vendor")]
    [InlineData(@"%UserProfile%\.config")]
    [InlineData(@"%AppData%\Vendor")]
    [InlineData(@"%LocalAppData%\Vendor")]
    [InlineData(@"%Temp%\Vendor")]
    [InlineData(@"%WinDir%\Vendor")]
    public async Task ExpandTokens_KnownEnvVars_AreFullyExpanded(string template)
    {
        var engine = BuildEngine();
        await engine.LoadAsync();

        var expanded = engine.ExpandTokens(template, publisher: null, displayName: "any");

        expanded.Should().NotContainAny("%ProgramFiles(x86)%", "%ProgramData%", "%UserProfile%",
            "%AppData%", "%LocalAppData%", "%Temp%", "%WinDir%");
    }
}
