using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DiskScout.Helpers;
using DiskScout.Models;
using DiskScout.Services;
using FluentAssertions;
using Serilog;

namespace DiskScout.Tests;

/// <summary>
/// Tests for <see cref="AuditCsvWriter"/>. Pin schema, escaping, encoding,
/// filename pattern, output path, and edge cases (empty list, fields with
/// commas / quotes / newlines).
/// </summary>
public class AuditCsvWriterTests : IDisposable
{
    private readonly ILogger _logger = new LoggerConfiguration().MinimumLevel.Warning().CreateLogger();
    private readonly List<string> _filesToCleanUp = new();

    public void Dispose()
    {
        // Best-effort: any test-written CSV in AuditFolder.
        // Note: CLAUDE.md prohibits File.Delete in src/, but allows it in tests
        // for fixture cleanup.
        foreach (var path in _filesToCleanUp)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
    }

    private static AppDataOrphanCandidate Candidate(
        string fullPath,
        long size = 0,
        int score = 75,
        RiskLevel risk = RiskLevel.Faible,
        RecommendedAction action = RecommendedAction.CorbeilleOk,
        IReadOnlyList<RuleHit>? triggered = null,
        IReadOnlyList<MatcherHit>? matchers = null,
        PathCategory category = PathCategory.Generic)
    {
        return new AppDataOrphanCandidate(
            NodeId: 1,
            FullPath: fullPath,
            SizeBytes: size,
            LastWriteUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ParentSignificantPath: fullPath,
            Category: category,
            MatchedSources: matchers ?? Array.Empty<MatcherHit>(),
            TriggeredRules: triggered ?? Array.Empty<RuleHit>(),
            ConfidenceScore: score,
            Risk: risk,
            Action: action,
            Reason: "test");
    }

    // -------------------------------------------------------------------------
    // Test 1: Header row exactly matches the documented schema.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task WriteAsync_HeaderRow_MatchesSchema()
    {
        var writer = new AuditCsvWriter(_logger);
        var path = await writer.WriteAsync(Array.Empty<AppDataOrphanCandidate>());
        _filesToCleanUp.Add(path);

        var lines = File.ReadAllLines(path, new UTF8Encoding(true));
        lines.Should().HaveCountGreaterThanOrEqualTo(1);
        lines[0].Should().Be(AuditCsvWriter.HeaderLine);
        lines[0].Should().Be("Path,SizeBytes,ConfidenceScore,RiskLevel,TriggeredRules,ExonerationRules,RecommendedAction");
    }

    // -------------------------------------------------------------------------
    // Test 2: Single candidate with TriggeredRules + MatchedSources serializes
    //   as semicolon-joined fields.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task WriteAsync_SingleCandidate_FieldsJoinedWithSemicolons()
    {
        var writer = new AuditCsvWriter(_logger);
        var c = Candidate(
            @"C:\ProgramData\Foo",
            size: 1024,
            score: 75,
            risk: RiskLevel.Faible,
            action: RecommendedAction.CorbeilleOk,
            triggered: new[]
            {
                new RuleHit("rule-a", PathCategory.Generic, "reason a"),
                new RuleHit("rule-b", PathCategory.Generic, "reason b"),
            },
            matchers: new[]
            {
                new MatcherHit("Registry", "Foo Inc.", -50),
                new MatcherHit("Service", "FooSvc", -45),
            });

        var path = await writer.WriteAsync(new[] { c });
        _filesToCleanUp.Add(path);

        var lines = File.ReadAllLines(path, new UTF8Encoding(true));
        lines.Should().HaveCount(2);

        var dataRow = lines[1];
        dataRow.Should().Contain("rule-a;rule-b");
        dataRow.Should().Contain("Registry:Foo Inc.;Service:FooSvc");
        dataRow.Should().Contain("75");
        dataRow.Should().Contain("Faible");
        dataRow.Should().Contain("CorbeilleOk");
    }

    // -------------------------------------------------------------------------
    // Test 3: Field with comma is double-quoted (RFC-4180 minimal compliance).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task WriteAsync_FieldWithComma_IsDoubleQuoted()
    {
        var writer = new AuditCsvWriter(_logger);
        var c = Candidate(@"C:\Vendor, with comma\Stuff");

        var path = await writer.WriteAsync(new[] { c });
        _filesToCleanUp.Add(path);

        var content = File.ReadAllText(path, new UTF8Encoding(true));
        content.Should().Contain("\"C:\\Vendor, with comma\\Stuff\"");
    }

    // -------------------------------------------------------------------------
    // Test 4: Field with double-quote is doubled and wrapped (RFC-4180).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task WriteAsync_FieldWithQuote_IsDoubledAndWrapped()
    {
        var writer = new AuditCsvWriter(_logger);
        var c = Candidate(@"C:\Vendor""Quote""\Stuff");

        var path = await writer.WriteAsync(new[] { c });
        _filesToCleanUp.Add(path);

        var content = File.ReadAllText(path, new UTF8Encoding(true));
        content.Should().Contain("\"C:\\Vendor\"\"Quote\"\"\\Stuff\"");
    }

    // -------------------------------------------------------------------------
    // Test 5: Returned filename matches `audit_YYYYMMDD_HHmmss.csv` pattern.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task WriteAsync_ReturnedFileName_MatchesAuditPattern()
    {
        var writer = new AuditCsvWriter(_logger);
        var path = await writer.WriteAsync(Array.Empty<AppDataOrphanCandidate>());
        _filesToCleanUp.Add(path);

        var name = Path.GetFileName(path);
        Regex.IsMatch(name, @"^audit_\d{8}_\d{6}\.csv$").Should().BeTrue(
            $"actual name was '{name}'");
    }

    // -------------------------------------------------------------------------
    // Test 6: Returned path is rooted at AppPaths.AuditFolder.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task WriteAsync_ReturnedPath_IsUnderAuditFolder()
    {
        var writer = new AuditCsvWriter(_logger);
        var path = await writer.WriteAsync(Array.Empty<AppDataOrphanCandidate>());
        _filesToCleanUp.Add(path);

        path.Should().StartWith(AppPaths.AuditFolder);
    }

    // -------------------------------------------------------------------------
    // Test 7: UTF-8 BOM is present (Excel-friendly).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task WriteAsync_File_StartsWithUtf8Bom()
    {
        var writer = new AuditCsvWriter(_logger);
        var path = await writer.WriteAsync(Array.Empty<AppDataOrphanCandidate>());
        _filesToCleanUp.Add(path);

        using var fs = File.OpenRead(path);
        var bom = new byte[3];
        var read = fs.Read(bom, 0, 3);
        read.Should().Be(3);
        bom.Should().BeEquivalentTo(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    // -------------------------------------------------------------------------
    // Test 8: EscapeCsv unit-test (white-box).
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("", "")]
    [InlineData("simple", "simple")]
    [InlineData("with,comma", "\"with,comma\"")]
    [InlineData("with\"quote", "\"with\"\"quote\"")]
    [InlineData("with\nnewline", "\"with\nnewline\"")]
    [InlineData("with\rcr", "\"with\rcr\"")]
    public void EscapeCsv_HandlesRfc4180Cases(string input, string expected)
    {
        AuditCsvWriter.EscapeCsv(input).Should().Be(expected);
    }
}
