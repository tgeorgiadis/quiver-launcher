using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class UpdatePromptPolicyTests
{
    [Fact]
    public void New_settings_default_to_quiet_catalog_and_app_prompts_with_badges_on()
    {
        var settings = new AppSettings();

        settings.PromptCatalogUpdates.Should().BeFalse();
        settings.PromptAppUpdateReviews.Should().BeFalse();
        settings.ShowLibraryAppUpdateBadges.Should().BeTrue();
    }

    [Fact]
    public void ShouldPromptCatalogUpdates_follows_setting()
    {
        UpdatePromptPolicy.ShouldPromptCatalogUpdates(null).Should().BeFalse();
        UpdatePromptPolicy.ShouldPromptCatalogUpdates(new AppSettings()).Should().BeFalse();
        UpdatePromptPolicy.ShouldPromptCatalogUpdates(new AppSettings { PromptCatalogUpdates = true })
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldPromptAppUpdateReviews_follows_setting()
    {
        UpdatePromptPolicy.ShouldPromptAppUpdateReviews(null).Should().BeFalse();
        UpdatePromptPolicy.ShouldPromptAppUpdateReviews(new AppSettings()).Should().BeFalse();
        UpdatePromptPolicy.ShouldPromptAppUpdateReviews(new AppSettings { PromptAppUpdateReviews = true })
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldShowLibraryAppUpdateBadges_defaults_on()
    {
        UpdatePromptPolicy.ShouldShowLibraryAppUpdateBadges(null).Should().BeTrue();
        UpdatePromptPolicy.ShouldShowLibraryAppUpdateBadges(new AppSettings()).Should().BeTrue();
        UpdatePromptPolicy.ShouldShowLibraryAppUpdateBadges(new AppSettings { ShowLibraryAppUpdateBadges = false })
            .Should().BeFalse();
    }
}
