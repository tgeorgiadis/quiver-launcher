using FluentAssertions;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogPresentationPerformanceTests : IDisposable
{
    [Fact]
    public void Unchanged_source_summaries_keep_cards_and_focus_without_collection_reset()
    {
        var model = new CatalogViewModel();
        var settings = new AppSettings { AppCatalogSources = [new() { Id = "source", Name = "Nintendo", Enabled = true }] };
        model.RefreshSourceList(model.Sources, settings);
        var row = model.Sources.Single();
        row.IsGamepadFocused = true;
        var changes = 0;
        model.Sources.CollectionChanged += (_, _) => changes++;
        model.RefreshSourceList(model.Sources, settings);
        changes.Should().Be(0);
        model.Sources.Single().Should().BeSameAs(row);
        row.IsGamepadFocused.Should().BeTrue();
        settings.AppCatalogSources[0].PendingReviewCount = 1;
        model.RefreshSourceList(model.Sources, settings);
        model.Sources.Single().PendingReviewCount.Should().Be(1);
    }

    [Fact]
    public void Metadata_classification_is_isolated_by_filter_and_successful_revision()
    {
        var entry = new CatalogPlatformEntry("1", ["client-Android.apk", "server-Windows.zip"], DateTimeOffset.UtcNow);
        CatalogPlatformSupport.FromMetadata(entry, "client").Should().Be(CatalogPlatformFlags.Android);
        CatalogPlatformSupport.FromMetadata(entry, "server").Should().Be(CatalogPlatformFlags.Windows);
        CatalogPlatformSupport.FromMetadata(entry with { AssetNames = [] }, "client").Should().Be(CatalogPlatformFlags.None);
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "catalog-presentation-" + Guid.NewGuid());
    public CatalogPresentationPerformanceTests() { Directory.CreateDirectory(_root); GitHubApiCache.Initialize(_root); }
    public void Dispose() => TestFixtures.CleanupDirectory(_root);

    [Theory]
    [InlineData(62, 0)]
    [InlineData(62, 100)]
    [InlineData(150, 0)]
    [InlineData(150, 500)]
    public void Warm_filters_reuse_classification_and_rows_without_catalog_io(int count, int libraryCount)
    {
        var catalog = Enumerable.Range(0, count).Select(i => new GameInfo {
            Name = $"App {i:D3}", Repository = $"fixture/app-{i}", FolderName = $"app-{i}",
            Tags = [i % 2 == 0 ? "even" : "odd"] }).ToList();
        var library = Enumerable.Range(0, libraryCount).Select(i => new GameInfo {
            Name = $"Library {i}", Repository = $"local/app-{i}", FolderName = $"library-{i}" }).ToList();
        foreach (var app in catalog)
            CatalogPlatformIndex.Set("github", app.Repository!, null, null, new() {
                tag_name = "1", assets = [new() { name = "fixture-Android.apk" }, new() { name = "fixture-Windows.zip" }] });
        var model = new CatalogSyncViewModel { PlatformFilters = ["Android"] };
        model.Refresh(new() { CachedListVersion = "1" }, library, catalog);
        model.AcceptPlatformDiscoveries();
        model.BeginPresentation();
        var original = model.GetFilteredRows();
        original.Should().HaveCount(count);
        model.GetFilteredRows().Should().BeSameAs(original);
        var classifications = CatalogPlatformSupport.ClassificationCount;
        var disk = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.GetLastWriteTimeUtc);
        for (var i = 0; i < 10; i++)
        {
            model.PlatformFilters = i % 2 == 0 ? ["Windows"] : ["Android"];
            model.ReviewFilter = i % 2 == 0 ? CatalogReviewFilter.All : CatalogReviewFilter.NeedsReview;
            model.SearchText = "App";
            model.BeginPresentation();
            model.GetFilteredRows().Should().Equal(original);
            model.VisiblePendingReviewCount.Should().Be(count);
            model.GetFilteredBulkAddRows().Should().HaveCount(count);
            model.CycleTagChip("even");
            model.BeginPresentation();
            model.GetFilteredRows().Should().HaveCount((count + 1) / 2);
            model.ClearTagChips();
        }
        CatalogPlatformSupport.ClassificationCount.Should().Be(classifications);
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.GetLastWriteTimeUtc)
            .Should().BeEquivalentTo(disk);
        model.AllRows.Should().Contain(original, "filtering retains the compared row instances");
    }

    [Fact]
    public void Metadata_updates_are_deferred_and_successful_empty_refresh_invalidates_support()
    {
        var app = new GameInfo { Name = "App", Repository = "fixture/deferred", FolderName = "app" };
        var model = new CatalogSyncViewModel { PlatformFilters = ["Android"] };
        model.Refresh(new() { CachedListVersion = "1" }, [], [app]);
        model.AcceptPlatformDiscoveries();
        model.BeginPresentation();
        model.GetFilteredRows().Should().ContainSingle();
        CatalogPlatformIndex.Set("github", app.Repository, null, null, new() { tag_name = "1", assets = [new() { name = "app.apk" }] });
        model.DeferPlatformDiscoveries();
        model.BeginPresentation();
        model.MoreAppsAvailable.Should().Be(0);
        model.GetFilteredRows().Should().ContainSingle();
        model.GetFilteredBulkAddRows().Should().ContainSingle();
        model.AcceptPlatformDiscoveries();
        model.BeginPresentation();
        model.GetFilteredRows().Should().ContainSingle();
        CatalogPlatformIndex.Set("github", app.Repository, null, null, new() { tag_name = "2", assets = [] });
        model.AcceptPlatformDiscoveries();
        model.BeginPresentation();
        model.GetFilteredRows().Should().BeEmpty();
        model.UnverifiedPlatformCount.Should().Be(0, "an empty release is known evidence");
    }
}
