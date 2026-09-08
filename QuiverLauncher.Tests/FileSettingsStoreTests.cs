using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class FileSettingsStoreTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _settingsPath;

    public FileSettingsStoreTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "QuiverLauncher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _settingsPath = Path.Combine(_tempDirectory, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new FileSettingsStore(_settingsPath);

        store.Current.FirstStartup.Should().BeTrue();
        store.Current.CatalogReviewUseGridView.Should().BeTrue();
        store.Current.TruncateLibraryCardTitles.Should().BeTrue();
        store.Current.GridCompactCards.Should().BeFalse();
        store.Current.IconFill.Should().BeFalse();
        store.Current.SlotSize.Should().Be(180);
        store.Current.IconSize.Should().Be(124);
        store.Current.ActionButtonSize.Should().Be(36);
        store.Current.LibraryCardTagMaxLines.Should().Be(2);
        store.Current.LibraryCardTagZeroMeansHidden.Should().BeTrue();
        store.Current.IconMargin.Should().Be(0);
        store.Current.SlotTextMargin.Should().Be(0);
        store.Current.AppCatalogSources.Should().NotBeNull();
        store.Current.HiddenApps.Should().NotBeNull();
        store.Current.CatalogPlatformFilters.Should().NotBeNull().And.BeEmpty();
        store.Current.CatalogPlatformFilterChosen.Should().BeFalse();
        store.Current.GitHubTokenBannerPermanentlyDismissed.Should().BeFalse();
        store.Current.GitHubTokenBannerSnoozedUntilUtc.Should().BeNull();
    }

    [Fact]
    public void Load_starts_with_empty_catalog_sources_when_file_missing()
    {
        var store = new FileSettingsStore(_settingsPath);

        store.Current.AppCatalogSources.Should().BeEmpty();
    }

    [Fact]
    public void Save_and_Load_round_trip_settings()
    {
        var store = new FileSettingsStore(_settingsPath);
        var settings = store.Load();
        settings.GitHubApiToken = "test-token";
        settings.GitLabApiToken = "gitlab-token";
        settings.SortBy = "Name";
        settings.CatalogReviewSortBy = "Repository";
        settings.CatalogReviewUseGridView = true;
        settings.CatalogPlatformFilters = ["Windows", "Linux"];
        settings.CatalogPlatformFilterChosen = true;
        settings.TruncateLibraryCardTitles = false;
        settings.ListScope = AppListScope.InstalledOnly;
        settings.ManuallyHiddenApps.Add("folder:TestGame");
        settings.GitHubTokenBannerPermanentlyDismissed = true;
        settings.GitHubTokenBannerSnoozedUntilUtc = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

        store.Save(settings);

        var reloaded = new FileSettingsStore(_settingsPath).Load();
        reloaded.GitHubApiToken.Should().Be("test-token");
        reloaded.GitLabApiToken.Should().Be("gitlab-token");
        reloaded.SortBy.Should().Be("Name");
        reloaded.CatalogReviewSortBy.Should().Be("Repository");
        reloaded.CatalogReviewUseGridView.Should().BeTrue();
        reloaded.CatalogPlatformFilters.Should().Equal("Windows", "Linux");
        reloaded.CatalogPlatformFilterChosen.Should().BeTrue();
        reloaded.TruncateLibraryCardTitles.Should().BeFalse();
        reloaded.ListScope.Should().Be(AppListScope.InstalledOnly);
        reloaded.ManuallyHiddenApps.Should().Contain("folder:TestGame");
        reloaded.HiddenApps.Should().BeEmpty();
        reloaded.GitHubTokenBannerPermanentlyDismissed.Should().BeTrue();
        reloaded.GitHubTokenBannerSnoozedUntilUtc.Should().Be(
            new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Save_and_Load_round_trip_hidden_from_review_repositories()
    {
        var store = new FileSettingsStore(_settingsPath);
        var settings = store.Load();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Name = "Test List",
            Location = "https://example.com/list.json",
            HiddenFromReviewRepositories = ["owner/hidden", "other/app"],
        });

        store.Save(settings);

        var reloaded = new FileSettingsStore(_settingsPath).Load();
        var testSource = reloaded.AppCatalogSources.Single(s => s.Name == "Test List");
        testSource.HiddenFromReviewRepositories
            .Should()
            .BeEquivalentTo(["owner/hidden", "other/app"]);
    }

    [Fact]
    public void Current_reflects_last_saved_settings()
    {
        var store = new FileSettingsStore(_settingsPath);
        var settings = store.Load();
        settings.LocalFirstCatalogMigrationComplete = true;

        store.Save(settings);

        store.Current.LocalFirstCatalogMigrationComplete.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_static_methods_use_default_store()
    {
        var original = SettingsStoreProvider.Default;
        try
        {
            SettingsStoreProvider.Default = new FileSettingsStore(_settingsPath);
            var settings = AppSettings.Load();
            settings.AppsPath = "D:\\Apps";
            AppSettings.Save(settings);

            AppSettings.Load().AppsPath.Should().Be("D:\\Apps");
        }
        finally
        {
            SettingsStoreProvider.Default = original;
        }
    }

    [Fact]
    public void Save_handles_concurrent_writes()
    {
        var store = new FileSettingsStore(_settingsPath);
        var settings = store.Load();
        settings.SortBy = "Name";

        Parallel.For(0, 8, i =>
        {
            var copy = store.Load();
            copy.SortBy = $"Thread-{i}";
            store.Save(copy);
        });

        File.Exists(_settingsPath).Should().BeTrue();
        store.Load().SortBy.Should().StartWith("Thread-");
    }
}
