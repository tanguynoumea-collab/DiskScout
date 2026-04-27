using DiskScout.Services;
using FluentAssertions;

namespace DiskScout.Tests;

/// <summary>
/// Tests for <see cref="ParentContextAnalyzer.GetSignificantParent"/>. Pin the
/// generic-leaf walk-up behavior + edge cases (empty path, root drive, no
/// generic ancestor).
/// </summary>
public class ParentContextAnalyzerTests
{
    private readonly ParentContextAnalyzer _analyzer = new();

    // -------------------------------------------------------------------------
    // Test 1 (Theory): A single generic leaf walks up to its immediate parent.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(@"C:\ProgramData\Adobe\Logs", @"C:\ProgramData\Adobe")]
    [InlineData(@"C:\ProgramData\Adobe\Cache", @"C:\ProgramData\Adobe")]
    [InlineData(@"C:\ProgramData\Adobe\Settings", @"C:\ProgramData\Adobe")]
    [InlineData(@"C:\ProgramData\Adobe\Updates", @"C:\ProgramData\Adobe")]
    [InlineData(@"C:\ProgramData\Adobe\Downloads", @"C:\ProgramData\Adobe")]
    [InlineData(@"C:\ProgramData\Adobe\Resources", @"C:\ProgramData\Adobe")]
    [InlineData(@"C:\ProgramData\Adobe\en-us", @"C:\ProgramData\Adobe")]
    [InlineData(@"C:\ProgramData\Adobe\fr-fr", @"C:\ProgramData\Adobe")]
    public void GetSignificantParent_SingleGenericLeaf_WalksUpOneLevel(string input, string expected)
    {
        _analyzer.GetSignificantParent(input).Should().BeEquivalentTo(expected);
    }

    // -------------------------------------------------------------------------
    // Test 2 (Theory): Generic-leaf matching is case-insensitive.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(@"C:\Vendor\LOGS", @"C:\Vendor")]
    [InlineData(@"C:\Vendor\cache", @"C:\Vendor")]
    [InlineData(@"C:\Vendor\EN-US", @"C:\Vendor")]
    [InlineData(@"C:\Vendor\FR-fr", @"C:\Vendor")]
    public void GetSignificantParent_GenericLeafMatching_IsCaseInsensitive(string input, string expected)
    {
        _analyzer.GetSignificantParent(input).Should().BeEquivalentTo(expected);
    }

    // -------------------------------------------------------------------------
    // Test 3: Multiple nested generic leaves walk up multiple levels.
    // -------------------------------------------------------------------------
    [Fact]
    public void GetSignificantParent_NestedGenericLeaves_WalksUpMultipleLevels()
    {
        var result = _analyzer.GetSignificantParent(@"C:\Vendor\Logs\Cache");

        result.Should().BeEquivalentTo(@"C:\Vendor");
    }

    // -------------------------------------------------------------------------
    // Test 4: Deeply nested generic chain ends at the first non-generic
    //         ancestor (Vendor in this case).
    // -------------------------------------------------------------------------
    [Fact]
    public void GetSignificantParent_DeeplyNestedChain_StopsAtFirstNonGenericAncestor()
    {
        var result = _analyzer.GetSignificantParent(@"C:\Vendor\Logs\Cache\en-us\Resources");

        result.Should().BeEquivalentTo(@"C:\Vendor");
    }

    // -------------------------------------------------------------------------
    // Test 5: Non-generic leaf returns the original path unchanged.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData(@"C:\ProgramData\Adobe")]
    [InlineData(@"C:\ProgramData\NinjaRMMAgent")]
    [InlineData(@"C:\Users\Public\Documents")]
    public void GetSignificantParent_NonGenericLeaf_ReturnsPathUnchanged(string input)
    {
        _analyzer.GetSignificantParent(input).Should().BeEquivalentTo(input);
    }

    // -------------------------------------------------------------------------
    // Test 6: Trailing separator is normalized away before evaluation.
    // -------------------------------------------------------------------------
    [Fact]
    public void GetSignificantParent_TrailingSeparator_IsTrimmed()
    {
        var result = _analyzer.GetSignificantParent(@"C:\Vendor\Logs\");

        result.Should().BeEquivalentTo(@"C:\Vendor");
    }

    // -------------------------------------------------------------------------
    // Test 7: Empty / null path is returned as-is (defensive — caller checks).
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("")]
    public void GetSignificantParent_EmptyPath_ReturnsAsIs(string input)
    {
        _analyzer.GetSignificantParent(input).Should().Be(input);
    }

    // -------------------------------------------------------------------------
    // Test 8: A drive root ("C:\") yields itself (no further ancestor).
    // -------------------------------------------------------------------------
    [Fact]
    public void GetSignificantParent_DriveRoot_ReturnsItself()
    {
        var result = _analyzer.GetSignificantParent(@"C:\");

        // Path.GetFileName("C:\") returns "" so we stop immediately and return current.
        result.Should().BeOneOf(@"C:", @"C:\");
    }

    // -------------------------------------------------------------------------
    // Test 9: A single-segment generic directly under the drive root
    //         (e.g., "C:\Logs") walks up to the drive root and stops there.
    // -------------------------------------------------------------------------
    [Fact]
    public void GetSignificantParent_GenericLeafDirectlyUnderDriveRoot_WalksToRoot()
    {
        var result = _analyzer.GetSignificantParent(@"C:\Logs");

        // Implementation walks up: leaf "Logs" is generic -> parent "C:\" -> leaf "" -> stop.
        result.Should().BeOneOf(@"C:\", @"C:");
    }

    // -------------------------------------------------------------------------
    // Test 10: All locale codes from the documented set are recognized.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("en-us")]
    [InlineData("fr-fr")]
    [InlineData("de-de")]
    [InlineData("es-es")]
    [InlineData("it-it")]
    [InlineData("ja-jp")]
    [InlineData("ko-kr")]
    [InlineData("pt-br")]
    [InlineData("ru-ru")]
    [InlineData("zh-cn")]
    [InlineData("zh-tw")]
    public void GetSignificantParent_AllLocaleCodes_AreRecognizedAsGeneric(string locale)
    {
        var input = $@"C:\Vendor\{locale}";

        _analyzer.GetSignificantParent(input).Should().BeEquivalentTo(@"C:\Vendor");
    }

    // -------------------------------------------------------------------------
    // Test 11: Locale-shaped strings NOT in the documented set are treated as
    //          significant (no regex — exact string match). E.g. "xx-xx".
    // -------------------------------------------------------------------------
    [Fact]
    public void GetSignificantParent_UndocumentedLocaleShapedString_IsSignificant()
    {
        var input = @"C:\Vendor\xx-xx";

        _analyzer.GetSignificantParent(input).Should().BeEquivalentTo(input);
    }
}
