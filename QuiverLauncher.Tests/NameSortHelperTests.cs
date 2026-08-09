using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class NameSortHelperTests
{
    [Theory]
    [InlineData("The Legend of Zelda", "Legend of Zelda")]
    [InlineData("the legend of zelda", "legend of zelda")]
    [InlineData("A Short Hike", "Short Hike")]
    [InlineData("An Adventure", "Adventure")]
    [InlineData("  The Legend of Zelda  ", "Legend of Zelda")]
    public void GetAlphabeticalSortKey_strips_leading_articles(string name, string expected)
    {
        NameSortHelper.GetAlphabeticalSortKey(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("Theater")]
    [InlineData("Another World")]
    [InlineData("Banjo")]
    [InlineData("Legend of Zelda")]
    public void GetAlphabeticalSortKey_leaves_non_article_prefixes_unchanged(string name)
    {
        NameSortHelper.GetAlphabeticalSortKey(name).Should().Be(name);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("The", "The")]
    [InlineData("A", "A")]
    [InlineData("An", "An")]
    public void GetAlphabeticalSortKey_handles_empty_and_article_only_names(string? name, string expected)
    {
        NameSortHelper.GetAlphabeticalSortKey(name).Should().Be(expected);
    }
}
