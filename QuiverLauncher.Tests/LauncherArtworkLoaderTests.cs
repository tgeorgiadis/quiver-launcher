using Avalonia;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherArtworkLoaderTests
{
    [Theory]
    [InlineData(600, 900, 512, 341, 512)]
    [InlineData(920, 430, 512, 512, 239)]
    [InlineData(256, 256, 512, 256, 256)]
    [InlineData(600, 900, 1024, 600, 900)]
    public void Artwork_is_shrunk_to_fit_without_changing_its_shape(int width, int height, int longSide, int expectedWidth, int expectedHeight) =>
        LauncherArtworkLoader.FittedSize(new PixelSize(width, height), longSide).Should().Be(new PixelSize(expectedWidth, expectedHeight));

    [Fact]
    public void Full_size_artwork_is_asked_for_once_and_empty_sources_stay_empty()
    {
        var hero = LauncherArtworkLoader.FullSize("https://example.com/hero.png");
        hero.Should().NotBe("https://example.com/hero.png");
        LauncherArtworkLoader.FullSize(hero).Should().Be(hero);
        LauncherArtworkLoader.FullSize(null).Should().BeNull();
        LauncherArtworkLoader.FullSize("").Should().Be("");
    }
}
