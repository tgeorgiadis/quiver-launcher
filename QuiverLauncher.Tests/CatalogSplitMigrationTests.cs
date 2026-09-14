using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogSplitMigrationTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    private sealed class DiskReviewService(string root, List<GameInfo> external) : ICatalogReviewService
    {
        private readonly AppCatalogService _catalog = new(dataDirectory: root);
        public Task<List<GameInfo>> LoadLocalAsync() => _catalog.LoadLocalAppsForMutationAsync();
        public Task<List<GameInfo>> LoadCachedAsync(string id) => Task.FromResult(external);
        public Task FetchAsync(AppCatalogSource source) => Task.CompletedTask;
        public Task SaveLocalAsync(List<GameInfo> apps, AppCatalogSource source, CancellationToken token) => _catalog.SaveLocalAppsAsync(apps);
        public void RefreshAvailability(AppCatalogSource source, List<GameInfo> local, List<GameInfo> catalog) { }
        public void Acknowledge(AppCatalogSource source) { }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Review_workspace_actions_keep_the_merged_row_up_to_date(bool fireRed, bool addFirst)
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-split-review-" + Guid.NewGuid().ToString("N"));
        var (legacy, catalog) = Split(fireRed);
        var source = new AppCatalogSource { Id = "nintendo", CachedListVersion = "1.0.10" };
        ICatalogReviewService service = new DiskReviewService(root, catalog);
        var model = new CatalogSyncViewModel();
        var errors = new List<string>();
        using var workspace = new CatalogReviewWorkspace(model, new(new Store()), service,
            (message, _, _) => { errors.Add(message); return Task.FromResult(true); }, () => Task.CompletedTask);
        try
        {
            await service.SaveLocalAsync([legacy], source, TestContext.Current.CancellationToken);
            await workspace.OpenAsync(source, CatalogReviewFilter.All, TestContext.Current.CancellationToken);
            var actions = addFirst ? new[] { CatalogReviewAction.Add, CatalogReviewAction.Merge } : new[] { CatalogReviewAction.Merge, CatalogReviewAction.Add };
            foreach (var action in actions)
            {
                var external = action == CatalogReviewAction.Merge ? catalog[0] : catalog[1];
                var row = model.AllRows.Single(r => r.External!.Name == external.Name);
                await workspace.ExecuteAsync(action, row.IdentityKey, TestContext.Current.CancellationToken);
                model.AllRows.Single(r => r.External!.Name == catalog[0].Name).CanAdd.Should().BeFalse();
            }
            errors.Should().BeEmpty();
            model.AllRows.Should().OnlyContain(r => r.Status == CatalogSyncStatus.Unchanged);
            await workspace.OpenAsync(source, CatalogReviewFilter.All, TestContext.Current.CancellationToken);
            model.AllRows.Should().OnlyContain(r => r.Status == CatalogSyncStatus.Unchanged);
            var staleAdd = await service.CommitAddAsync(source, catalog[0], false);
            staleAdd.Outcome.Should().Be(CatalogAddOutcome.AlreadyAdded);
            var saved = await service.LoadLocalAsync();
            saved.Should().HaveCount(2);
            saved.Single(a => a.ReleaseAssetFilter == catalog[0].ReleaseAssetFilter).FolderName.Should().Be(legacy.FolderName);
        }
        finally { Directory.Delete(root, true); }
    }

    private static (GameInfo Legacy, List<GameInfo> Catalog) Split(bool fireRed)
    {
        var names = fireRed ? new[] { "FireRed", "LeafGreen" } : new[] { "Ruby", "Sapphire" };
        var project = fireRed ? "FireRedLeafGreenRecomp" : "RubySapphireRecomp";
        GameInfo App(string name, string folder, string? filter) => new()
        {
            Name = "Pokemon " + name, Repository = "mstan/" + project, Project = project,
            FolderName = folder, ReleaseAssetFilter = filter, Tags = ["gba", "pokemon"]
        };
        return (App(string.Join(" / ", names), "Pokemon" + string.Join("And", names) + "-" + project, null),
            names.Select(n => App(n, "Pokemon" + n + "-" + project, n)).ToList());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Split_merge_and_add_stay_matched_across_saves_and_catalog_reordering(bool fireRed, bool addFirst, bool reverse)
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-split-" + Guid.NewGuid().ToString("N"));
        var (legacy, catalog) = Split(fireRed);
        var inherited = catalog[0];
        var sibling = catalog[1];
        var install = Path.Combine(root, "Custom game folder");
        Directory.CreateDirectory(install);
        var save = Path.Combine(install, "save.dat");
        await File.WriteAllTextAsync(save, "preserve existing save", TestContext.Current.CancellationToken);
        legacy.InstallPath = install;
        legacy.PreferredVersion = "1.0";
        legacy.AutoUpdate = true;
        var source = new AppCatalogSource { Id = "nintendo", CachedListVersion = "1.0.10" };
        var model = new CatalogSyncViewModel();
        try
        {
            var service = new AppCatalogService(dataDirectory: root);
            await service.SaveLocalAppsAsync([legacy]);
            var local = await service.LoadLocalAppsForMutationAsync();
            model.Refresh(source, local, catalog);
            model.AllRows.Single(r => r.External!.Name == inherited.Name).Status.Should().Be(CatalogSyncStatus.Changed);
            model.AllRows.Single(r => r.External!.Name == sibling.Name).CanAdd.Should().BeTrue();

            if (addFirst)
                local = CatalogReviewService.PlanAdd(local, sibling, false).Library;
            else
                local = CatalogCompareService.ApplyRowMerge(local, model.AllRows.Single(r => r.External!.Name == inherited.Name));
            await service.SaveLocalAppsAsync(local);
            local = await new AppCatalogService(dataDirectory: root).LoadLocalAppsForMutationAsync();
            if (reverse) catalog.Reverse();
            model.Refresh(source, local, catalog);

            if (addFirst)
            {
                var merge = model.AllRows.Single(r => r.External!.Name == inherited.Name);
                merge.CanMerge.Should().BeTrue();
                merge.CanAdd.Should().BeFalse();
                local = CatalogCompareService.ApplyRowMerge(local, merge);
                model.Refresh(source, local, catalog);
            }
            else
            {
                local = CatalogReviewService.PlanAdd(local, sibling, false).Library;
                model.ReconcileAddition(local); // Same incremental path used by the live Review UI.
            }
            model.AllRows.Should().OnlyContain(r => r.Status == CatalogSyncStatus.Unchanged && !r.CanAdd);
            await service.SaveLocalAppsAsync(local);
            local = await new AppCatalogService(dataDirectory: root).LoadLocalAppsForMutationAsync();
            CatalogCompareService.BuildCompareRows(local, catalog).Should().OnlyContain(r => r.Status == CatalogSyncStatus.Unchanged);
            local.Should().HaveCount(2);
            var kept = local.Single(a => a.ReleaseAssetFilter == inherited.ReleaseAssetFilter);
            kept.FolderName.Should().Be(legacy.FolderName);
            kept.InstallPath.Should().Be(install);
            kept.PreferredVersion.Should().Be("1.0");
            kept.AutoUpdate.Should().BeTrue();
            (await File.ReadAllTextAsync(save, TestContext.Current.CancellationToken)).Should().Be("preserve existing save");
            CatalogCompareService.ComputeLibraryUsageStats(local, catalog).Should().Be((2, 2));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Exact_matches_are_reserved_before_earlier_sibling_repository_fallback()
    {
        var (legacy, catalog) = Split(false);
        legacy.FolderName = catalog[0].FolderName;
        catalog.Reverse();
        var rows = CatalogCompareService.BuildCompareRows([legacy], catalog);
        rows.Single(r => r.External!.ReleaseAssetFilter == "Ruby").Local.Should().BeSameAs(legacy);
        rows.Single(r => r.External!.ReleaseAssetFilter == "Sapphire").CanAdd.Should().BeTrue();
    }

    [Fact]
    public void Stale_single_and_bulk_adds_cannot_duplicate_merged_variants()
    {
        var (legacy, catalog) = Split(false);
        var staleRows = CatalogCompareService.BuildCompareRows([], catalog);
        var ruby = CatalogCompareService.MergeExternalIntoLocal(legacy, catalog[0]);
        ruby.ReleaseAssetFilter = " ruby ";
        var commit = CatalogReviewService.PlanAdd([ruby], catalog[0], false);
        commit.Outcome.Should().Be(CatalogAddOutcome.AlreadyAdded);
        commit.App.Should().BeSameAs(ruby);
        commit.Library.Should().ContainSingle();
        CatalogCompareService.ApplyRowAdd([ruby], staleRows[0]).Should().ContainSingle().Which.Should().BeSameAs(ruby);
        var bulk = CatalogCompareService.ApplyAddAllExternalOnly([ruby], staleRows);
        bulk.Should().HaveCount(2);
        bulk.Should().Contain(ruby);
        bulk.Should().Contain(a => a.ReleaseAssetFilter == "Sapphire");
    }

    [Fact]
    public void Filtered_sibling_does_not_match_a_different_game_or_repository_source()
    {
        var (legacy, catalog) = Split(false);
        var ruby = CatalogCompareService.MergeExternalIntoLocal(legacy, catalog[0]);
        CatalogCompareService.BuildCompareRows([ruby], [catalog[1]]).Should().ContainSingle().Which.CanAdd.Should().BeTrue();
        CatalogReviewService.PlanAdd([ruby], catalog[1], false).Outcome.Should().Be(CatalogAddOutcome.Added);
        catalog[0].RepositorySource = "gitlab";
        CatalogCompareService.BuildCompareRows([ruby], [catalog[0]]).Should().ContainSingle().Which.Local.Should().BeNull();
        CatalogReviewService.PlanAdd([ruby], catalog[0], false).Outcome.Should().Be(CatalogAddOutcome.Added);
    }

    [Fact]
    public void Unfiltered_and_manual_instances_remain_distinct_and_ambiguous_variants_are_not_guessed()
    {
        var (legacy, catalog) = Split(false);
        var second = CatalogCompareService.CloneForLocal(legacy);
        second.FolderName = "Another install";
        CatalogReviewService.PlanAdd([legacy], second, false).Outcome.Should().Be(CatalogAddOutcome.Added);
        legacy.Repository = second.Repository = "";
        legacy.ReleaseAssetFilter = second.ReleaseAssetFilter = "Ruby";
        CatalogReviewService.PlanAdd([legacy], second, false).Outcome.Should().Be(CatalogAddOutcome.Added);
        legacy.Repository = second.Repository = catalog[0].Repository;
        var rows = CatalogCompareService.BuildCompareRows([legacy, second], [catalog[0]]);
        rows.Should().ContainSingle().Which.Local.Should().BeNull();
        CatalogReviewService.PlanAdd([legacy, second], catalog[0], false).Outcome.Should().Be(CatalogAddOutcome.AlreadyAdded);
    }
}
