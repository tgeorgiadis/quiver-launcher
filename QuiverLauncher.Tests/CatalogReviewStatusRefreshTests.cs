using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogReviewStatusRefreshTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("malformed-first-source")]
    public async Task Source_refresh_reconciles_and_persists_review_status_from_saved_definitions(string scenario)
    {
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "catalog-review-status", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        try
        {
            var store = new FileSettingsStore();
            store.Current.AppsPath = Path.Combine(root, "Apps");
            var source = new AppCatalogSource { Id = "nintendo", Name = "Nintendo", Enabled = true,
                CachedListVersion = "1.0.8", UpdateAvailable = true };
            store.Current.AppCatalogSources = [source];
            store.Save(store.Current);
            using var manager = new GameManager(store);
            var apps = Enumerable.Range(0, 62).Select(i => new GameInfo
            { Name = $"App {i}", Repository = $"fixture/app{i}", FolderName = $"App{i}" }).ToList();
            await manager.CatalogService.SaveLocalAppsAsync(apps);
            var cache = Path.Combine(manager.CatalogService.CatalogSourcesCacheFolder, source.Id + ".json");
            if (scenario == "changed") apps[0].Name = "Changed name";
            if (scenario == "malformed") await File.WriteAllTextAsync(cache, "{broken");
            else if (scenario != "missing") await manager.CatalogService.ExportLocalAppsToFileAsync(cache, apps);
            if (scenario == "malformed-first-source")
            {
                var damaged = new AppCatalogSource { Id = "damaged", Name = "Damaged", Enabled = true,
                    LibraryAppCount = 3, ListAppCount = 10, UpdateAvailable = true };
                store.Current.AppCatalogSources.Insert(0, damaged);
                await File.WriteAllTextAsync(Path.Combine(manager.CatalogService.CatalogSourcesCacheFolder, "damaged.json"), "{broken");
            }
            var model = new CatalogViewModel();
            model.Configure(new SettingsViewModel(store), new CatalogSourcesService(manager),
                (_, _, _) => Task.FromResult(false), () => Task.CompletedTask,
                () => Task.CompletedTask, () => Task.CompletedTask);
            await model.RefreshAsync(CancellationToken.None);
            source = store.Current.AppCatalogSources.Single(s => s.Id == "nintendo");
            var card = model.Sources.Single(s => s.SourceId == "nintendo");
            var persisted = new FileSettingsStore().Current.AppCatalogSources.Single(s => s.Id == "nintendo");
            if (scenario is "complete" or "malformed-first-source")
            {
                source.LibraryAppCount.Should().Be(62);
                card.IsAllReviewed.Should().BeTrue();
                card.ReviewStatusText.Should().Be("All reviewed");
                card.ReviewButtonText.Should().Be("Browse apps");
                card.VersionLineUnreviewed.Should().BeFalse();
                model.PendingReviewCount.Should().Be(0);
                persisted.AcknowledgedListVersion.Should().Be("1.0.8");
                persisted.UpdateAvailable.Should().BeFalse();
                store.Load();
                model.RefreshPresentation();
                var reloadedCard = model.Sources.Single(s => s.SourceId == "nintendo");
                reloadedCard.UsageStatsShort.Should().Be("62 of 62 apps in your library");
                reloadedCard.IsAllReviewed.Should().BeTrue();
                if (scenario == "malformed-first-source")
                    model.Sources.Single(s => s.SourceId == "damaged").UsageStatsShort.Should().Be("3 of 10 apps in your library");
                // Reloading saved state and rebuilding startup availability stays clear.
                await manager.CatalogService.RefreshAllSourcesUsageStatsAsync(new FileSettingsStore().Current);
            }
            else
            {
                card.IsAllReviewed.Should().BeFalse();
                persisted.AcknowledgedListVersion.Should().BeNullOrEmpty();
                persisted.UpdateAvailable.Should().BeTrue();
                if (scenario == "changed")
                {
                    source.LibraryAppCount.Should().Be(62);
                    model.PendingReviewCount.Should().Be(1, "full membership does not acknowledge changed app definitions");
                }
            }
        }
        finally
        {
            QuiverLauncherPaths.OverrideUserDataRoot = previous;
            Directory.Delete(root, true);
        }
    }
}
