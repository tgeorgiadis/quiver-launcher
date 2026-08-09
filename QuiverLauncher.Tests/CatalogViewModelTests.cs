using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogViewModelTests
{
    [Fact]
    public void BuildSourceListItems_maps_enabled_sources()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(new AppCatalogSource
        {
            Name = "Local",
            Location = "apps.json",
            Enabled = true,
            CachedListVersion = "1.0.0",
            AcknowledgedListVersion = "1.0.0",
        });

        var viewModel = new CatalogViewModel();
        var items = viewModel.BuildSourceListItems(settings);

        items.Should().ContainSingle();
        items[0].Name.Should().Be("Local");
        items[0].ListVersionText.Should().Be("1.0.0");
        items[0].LastReviewedText.Should().Be("1.0.0");
        items[0].IsAllReviewed.Should().BeTrue();
    }

    [Fact]
    public void BuildSourceListItems_sorts_enabled_before_disabled_then_alphabetically()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.AddRange([
            new AppCatalogSource { Name = "Zebra", Enabled = false },
            new AppCatalogSource { Name = "Alpha", Enabled = true, PendingReviewCount = 0 },
            new AppCatalogSource { Name = "Beta", Enabled = true, PendingReviewCount = 2 },
            new AppCatalogSource { Name = "Charlie", Enabled = false },
        ]);

        var viewModel = new CatalogViewModel();
        var items = viewModel.BuildSourceListItems(settings, CatalogSourceListFilter.All);

        items.Select(i => i.Name).Should().Equal("Beta", "Alpha", "Charlie", "Zebra");
    }

    [Fact]
    public void BuildSourceListItems_enabled_filter_returns_only_enabled_by_default()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.AddRange([
            new AppCatalogSource { Name = "Active", Enabled = true },
            new AppCatalogSource { Name = "Inactive", Enabled = false },
        ]);

        var viewModel = new CatalogViewModel();
        viewModel.SourceListFilter.Should().Be(CatalogSourceListFilter.Enabled);

        var items = viewModel.BuildSourceListItems(settings);

        items.Should().ContainSingle();
        items[0].Name.Should().Be("Active");
    }

    [Fact]
    public void BuildSourceListItems_disabled_filter_returns_only_disabled()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.AddRange([
            new AppCatalogSource { Name = "Active", Enabled = true },
            new AppCatalogSource { Name = "Inactive", Enabled = false },
        ]);

        var viewModel = new CatalogViewModel();
        var items = viewModel.BuildSourceListItems(settings, CatalogSourceListFilter.Disabled);

        items.Should().ContainSingle();
        items[0].Name.Should().Be("Inactive");
    }

    [Fact]
    public void BuildSourceListItems_all_filter_returns_every_source()
    {
        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.AddRange([
            new AppCatalogSource { Name = "Active", Enabled = true },
            new AppCatalogSource { Name = "Inactive", Enabled = false },
        ]);

        var viewModel = new CatalogViewModel();
        var items = viewModel.BuildSourceListItems(settings, CatalogSourceListFilter.All);

        items.Should().HaveCount(2);
        items.Select(i => i.Name).Should().Equal("Active", "Inactive");
    }

    [Fact]
    public void CreateSource_sets_enabled_defaults()
    {
        var viewModel = new CatalogViewModel();
        var source = viewModel.CreateSource("https://example.com/list.json");

        source.Name.Should().BeEmpty();
        source.Location.Should().Be("https://example.com/list.json");
        source.Enabled.Should().BeTrue();
    }
}
