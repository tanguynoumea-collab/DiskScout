using DiskScout.Helpers;
using FluentAssertions;

namespace DiskScout.Tests;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData("Google Chrome", "Google LLC", "Google Chrome")]
    public void ExactMatch_ReturnsPerfectScore(string folder, string? publisher, string? displayName)
    {
        FuzzyMatcher.ComputeMatch(folder, publisher, displayName).Should().Be(1.0);
    }

    [Theory]
    [InlineData("Adobe", "Adobe Inc.", "Adobe Photoshop")]
    [InlineData("Microsoft", "Microsoft Corporation", "Microsoft Visual Studio")]
    public void PrefixMatch_ReachesThreshold(string folder, string? publisher, string? displayName)
    {
        FuzzyMatcher.IsMatch(folder, publisher, displayName).Should().BeTrue();
    }

    [Theory]
    [InlineData("Visual Studio 2022", "Microsoft Corporation", "Microsoft Visual Studio 2022")]
    [InlineData("Chrome", "Google LLC", "Google Chrome")]
    public void TokenMatch_ReachesThreshold(string folder, string? publisher, string? displayName)
    {
        FuzzyMatcher.IsMatch(folder, publisher, displayName).Should().BeTrue();
    }

    [Theory]
    [InlineData("random-uuid-abc123", "Acme Corp", "Acme Office Suite")]
    [InlineData("ZX12", "Foo", "Foo Bar Baz")]
    public void NoCommonTokens_ReturnsLowScore(string folder, string? publisher, string? displayName)
    {
        FuzzyMatcher.IsMatch(folder, publisher, displayName).Should().BeFalse();
    }

    [Fact]
    public void EmptyFolderName_ReturnsZero()
    {
        FuzzyMatcher.ComputeMatch(string.Empty, "Anything", "Anything").Should().Be(0);
    }

    [Fact]
    public void SingleTokenOverlap_DoesNotSatisfyMargin()
    {
        FuzzyMatcher.IsMatch("Microsoft", "Adobe", "Adobe Photoshop").Should().BeFalse();
    }
}
