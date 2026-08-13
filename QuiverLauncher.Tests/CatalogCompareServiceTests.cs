using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogCompareServiceTests
{
    private static GameInfo CreateApp(
        string repository,
        string name = "Test App",
        string folderName = "TestFolder",
        string? tags = null)
    {
        return new GameInfo
        {
            Repository = repository,
            Name = name,
            FolderName = folderName,
            Tags = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [],
        };
    }

    [Fact]
    public void BuildCompareRows_classifies_local_external_unchanged_and_changed()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/local-only", "Local Only"),
            CreateApp("owner/shared", "Old Name"),
        };

        var external = new List<GameInfo>
        {
            CreateApp("owner/shared", "New Name"),
            CreateApp("owner/external-only", "External Only"),
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);

        rows.Should().NotContain(r => r.Repository == "owner/local-only");
        rows.Should().Contain(r => r.Repository == "owner/external-only" && r.Status == CatalogSyncStatus.InExternalOnly);
        rows.Should().Contain(r => r.Repository == "owner/shared" && r.Status == CatalogSyncStatus.Changed);
    }

    [Fact]
    public void BuildCompareRows_preserves_external_catalog_order()
    {
        var local = new List<GameInfo>();
        var external = new List<GameInfo>
        {
            CreateApp("owner/zebra", "Zebra App"),
            CreateApp("owner/alpha", "Alpha App"),
            CreateApp("owner/middle", "Middle App"),
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);

        rows.Select(r => r.Repository).Should().Equal("owner/zebra", "owner/alpha", "owner/middle");
    }

    [Fact]
    public void BuildCompareRows_omits_local_library_apps_not_in_external_catalog()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/zebra-local", "Zebra Local"),
            CreateApp("owner/alpha-local", "Alpha Local"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/catalog-first", "Catalog First"),
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);

        rows.Select(r => r.Repository).Should().Equal("owner/catalog-first");
    }

    [Fact]
    public void ComputeLibraryUsageStats_counts_local_matches_against_external_total()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/a"),
            CreateApp("owner/b"),
            CreateApp("owner/c"),
            CreateApp("owner/not-in-catalog"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/a"),
            CreateApp("owner/b"),
            CreateApp("owner/c"),
            CreateApp("owner/d"),
            CreateApp("owner/e"),
        };

        var (usingCount, totalCount) = CatalogCompareService.ComputeLibraryUsageStats(local, external);

        usingCount.Should().Be(3);
        totalCount.Should().Be(5);
    }

    [Fact]
    public void ComputeLibraryUsageStats_returns_zero_using_when_none_in_library()
    {
        var external = Enumerable.Range(1, 5)
            .Select(i => CreateApp($"owner/app{i}"))
            .ToList();

        var (usingCount, totalCount) = CatalogCompareService.ComputeLibraryUsageStats([], external);

        usingCount.Should().Be(0);
        totalCount.Should().Be(5);
    }

    [Fact]
    public void ComputeLibraryUsageStats_deduplicates_external_repositories()
    {
        var local = new List<GameInfo> { CreateApp("owner/shared") };
        var external = new List<GameInfo>
        {
            CreateApp("owner/shared", "First"),
            CreateApp("owner/shared", "Duplicate"),
            CreateApp("owner/other"),
        };

        var (usingCount, totalCount) = CatalogCompareService.ComputeLibraryUsageStats(local, external);

        usingCount.Should().Be(1);
        totalCount.Should().Be(2);
    }

    [Fact]
    public void MergeExternalIntoLocal_unions_tags_and_keeps_local_skipped_version()
    {
        var local = CreateApp("owner/app", "Local Name");
        local.SkippedUpdateVersion = "v1.0.0";
        local.Tags = ["a"];

        var external = CreateApp("owner/app", "External Name");
        external.Tags = ["b"];

        var merged = CatalogCompareService.MergeExternalIntoLocal(local, external);

        merged.Name.Should().Be("External Name");
        merged.SkippedUpdateVersion.Should().Be("v1.0.0");
        merged.Tags.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void MergeAndReplace_preserve_local_AutoUpdate()
    {
        var local = CreateApp("owner/app", "Local");
        local.AutoUpdate = true;

        var external = CreateApp("owner/app", "External");
        external.AutoUpdate = false;

        CatalogCompareService.MergeExternalIntoLocal(local, external).AutoUpdate.Should().BeTrue();
        CatalogCompareService.ReplaceFromExternal(local, external).AutoUpdate.Should().BeTrue();
    }

    [Fact]
    public void ApplyAddAllExternalOnly_appends_missing_repositories()
    {
        var local = new List<GameInfo> { CreateApp("owner/existing") };
        var rows = CatalogCompareService.BuildCompareRows(
            local,
            [CreateApp("owner/existing"), CreateApp("owner/new")]);

        var updated = CatalogCompareService.ApplyAddAllExternalOnly(local, rows);

        updated.Select(a => a.Repository).Should().BeEquivalentTo(["owner/existing", "owner/new"]);
    }

    [Fact]
    public void CloneForLocal_uses_autoUpdate_argument_not_external_flag()
    {
        var external = CreateApp("owner/new", "New App");
        external.AutoUpdate = true;

        CatalogCompareService.CloneForLocal(external).AutoUpdate.Should().BeFalse();
        CatalogCompareService.CloneForLocal(external, autoUpdate: true).AutoUpdate.Should().BeTrue();
    }

    [Fact]
    public void ApplyRowAdd_sets_AutoUpdate_when_requested()
    {
        var local = new List<GameInfo> { CreateApp("owner/existing") };
        var rows = CatalogCompareService.BuildCompareRows(
            local,
            [CreateApp("owner/existing"), CreateApp("owner/new", "New App")]);
        var addRow = rows.Single(r => r.Repository == "owner/new");

        var withoutFlag = CatalogCompareService.ApplyRowAdd(local, addRow);
        withoutFlag.Should().ContainSingle(a => a.Repository == "owner/new" && !a.AutoUpdate);

        var withFlag = CatalogCompareService.ApplyRowAdd(local, addRow, autoUpdateNewlyAdded: true);
        withFlag.Should().ContainSingle(a => a.Repository == "owner/new" && a.AutoUpdate);
        withFlag.Should().ContainSingle(a => a.Repository == "owner/existing" && !a.AutoUpdate);
    }

    [Fact]
    public void ApplyAddAllExternalOnly_sets_AutoUpdate_on_new_apps_only()
    {
        var local = new List<GameInfo> { CreateApp("owner/existing") };
        var rows = CatalogCompareService.BuildCompareRows(
            local,
            [CreateApp("owner/existing"), CreateApp("owner/new", "New App")]);

        var updated = CatalogCompareService.ApplyAddAllExternalOnly(
            local,
            rows,
            autoUpdateNewlyAdded: true);

        updated.Should().ContainSingle(a => a.Repository == "owner/existing" && !a.AutoUpdate);
        updated.Should().ContainSingle(a => a.Repository == "owner/new" && a.AutoUpdate);
    }

    [Fact]
    public void ApplyReplaceAllChanged_overwrites_catalog_fields()
    {
        var local = new List<GameInfo> { CreateApp("owner/app", "Old Name", "OldFolder") };
        var external = new List<GameInfo> { CreateApp("owner/app", "New Name", "NewFolder") };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        var updated = CatalogCompareService.ApplyReplaceAllChanged(local, rows);

        updated.Single().Name.Should().Be("New Name");
        // Folder mapping is preserved so installed apps are not retargeted by catalog renames.
        updated.Single().FolderName.Should().Be("OldFolder");
    }

    [Fact]
    public void FilterVisibleRows_hides_unchanged_by_default()
    {
        var local = new List<GameInfo> { CreateApp("owner/same", "Same", "Folder") };
        var external = new List<GameInfo>
        {
            CreateApp("owner/same", "Same", "Folder"),
            CreateApp("owner/new", "New", "NewFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        CatalogCompareService.FilterVisibleRows(rows, source, showUpToDateApps: false)
            .Should()
            .ContainSingle(r => r.Repository == "owner/new");

        CatalogCompareService.FilterVisibleRows(rows, source, showUpToDateApps: true)
            .Should()
            .HaveCount(2);
    }

    [Fact]
    public void IgnoreChangesForCurrentVersion_hides_actionable_row()
    {
        var local = new List<GameInfo> { CreateApp("owner/app", "Old", "Folder") };
        var external = new List<GameInfo> { CreateApp("owner/app", "New", "Folder") };
        var source = new AppCatalogSource { CachedListVersion = "2.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        CatalogCompareService.HasActionableChanges(source, rows).Should().BeTrue();

        CatalogCompareService.IgnoreChangesForCurrentVersion(source, "owner/app");

        CatalogCompareService.HasActionableChanges(source, rows).Should().BeFalse();
        CatalogCompareService.FilterVisibleRows(rows, source, showUpToDateApps: false).Should().BeEmpty();
    }

    [Fact]
    public void BuildTagDiff_highlights_local_only_and_external_only_tags()
    {
        var local = CreateApp("owner/app");
        local.Tags = ["n64", "pokemon", "ai"];
        var external = CreateApp("owner/app");
        external.Tags = ["n64", "pokemon", "recomp"];

        var diff = CatalogSyncFieldDiffBuilder.BuildFieldDiffs(
            CatalogSyncStatus.Changed,
            local,
            external,
            ["tags"]).Single();

        diff.IsTagDiff.Should().BeTrue();
        diff.TagDiffs.Single(t => t.Tag == "ai").Kind.Should().Be(CatalogSyncTagDiffKind.LocalOnly);
        diff.TagDiffs.Single(t => t.Tag == "recomp").Kind.Should().Be(CatalogSyncTagDiffKind.ExternalOnly);
        diff.TagDiffs.Single(t => t.Tag == "pokemon").Kind.Should().Be(CatalogSyncTagDiffKind.Shared);
    }

    [Fact]
    public void BuildIconDiff_uses_icon_kind_with_urls()
    {
        var local = CreateApp("owner/app");
        local.GameIconUrl = "https://example.com/local.png";
        var external = CreateApp("owner/app");
        external.GameIconUrl = "https://example.com/external.png";

        var diff = CatalogSyncFieldDiffBuilder.BuildFieldDiffs(
            CatalogSyncStatus.Changed,
            local,
            external,
            ["appIconUrl"]).Single();

        diff.Kind.Should().Be(CatalogSyncFieldDiffKind.Icon);
        diff.IsIconDiff.Should().BeTrue();
        diff.IsTextValueDiff.Should().BeFalse();
        diff.LocalValue.Should().Be("https://example.com/local.png");
        diff.ExternalValue.Should().Be("https://example.com/external.png");
    }

    [Fact]
    public void BuildValueDiff_shows_old_and_new_values()
    {
        var local = CreateApp("owner/app", "Old Name", "OldFolder");
        var external = CreateApp("owner/app", "New Name", "OldFolder");

        var diff = CatalogSyncFieldDiffBuilder.BuildFieldDiffs(
            CatalogSyncStatus.Changed,
            local,
            external,
            ["name"]).Single();

        diff.LocalValue.Should().Be("Old Name");
        diff.ExternalValue.Should().Be("New Name");
        diff.ShowArrow.Should().BeTrue();
    }

    [Fact]
    public void FormatVersionForDisplay_shortens_long_hashes()
    {
        const string hash = "C926BD289048466BFFD7494256C430205E57E3D8D8430661ECF2C39A919813FC";
        CatalogCompareService.FormatVersionForDisplay(hash).Should().Be("C926BD28…919813FC");
    }

    [Fact]
    public void FormatCatalogVersionSummary_shows_not_reviewed_for_unacknowledged()
    {
        CatalogCompareService.FormatCatalogVersionSummary("1.0.0", null)
            .Should().Be("List version: 1.0.0\nLast reviewed: not yet");
        CatalogCompareService.FormatCatalogVersionSummary("1.0.0", "0")
            .Should().Be("List version: 1.0.0\nLast reviewed: not yet");
    }

    [Fact]
    public void FormatCatalogVersionSummary_shows_reviewed_version_when_acknowledged()
    {
        CatalogCompareService.FormatCatalogVersionSummary("1.0.0", "1.0.0")
            .Should().Be("List version: 1.0.0\nLast reviewed: 1.0.0");
    }

    [Fact]
    public void FormatCatalogVersionParts_returns_compact_labels()
    {
        var parts = CatalogCompareService.FormatCatalogVersionParts("1.0.2", "1.0.0");

        parts.ListVersionText.Should().Be("1.0.2");
        parts.LastReviewedText.Should().Be("1.0.0");
        parts.LastReviewedUnreviewed.Should().BeFalse();
        parts.VersionRowVisible.Should().BeTrue();
    }

    [Fact]
    public void FormatCatalogVersionParts_marks_unreviewed_last_reviewed()
    {
        var parts = CatalogCompareService.FormatCatalogVersionParts("1.0.0", null);

        parts.ListVersionText.Should().Be("1.0.0");
        parts.LastReviewedText.Should().Be("not yet");
        parts.LastReviewedUnreviewed.Should().BeTrue();
        parts.VersionRowVisible.Should().BeTrue();
    }

    [Fact]
    public void TryAutoAcknowledgeIfReviewComplete_updates_acknowledged_version()
    {
        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            AcknowledgedListVersion = "0",
            UpdateAvailable = true,
        };

        AppCatalogService.TryAutoAcknowledgeIfReviewComplete(source, pendingCount: 0).Should().BeTrue();
        source.AcknowledgedListVersion.Should().Be("1.0.0");
        source.UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public void TryAutoAcknowledgeIfReviewComplete_does_nothing_when_pending_remains()
    {
        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            AcknowledgedListVersion = "0",
        };

        AppCatalogService.TryAutoAcknowledgeIfReviewComplete(source, pendingCount: 3).Should().BeFalse();
        source.AcknowledgedListVersion.Should().Be("0");
    }

    [Fact]
    public void BuildCompareRows_includes_inline_field_diffs_for_changed_apps()
    {
        var local = new List<GameInfo> { CreateApp("owner/app", "Old", "Folder") };
        local[0].Tags = ["ai"];
        var external = new List<GameInfo> { CreateApp("owner/app", "New", "Folder") };
        external[0].Tags = ["ai", "recomp"];

        var rows = CatalogCompareService.BuildCompareRows(local, external);
        var row = rows.Single(r => r.Repository == "owner/app");

        row.HasInlineDiff.Should().BeTrue();
        row.FieldDiffs.Should().NotBeEmpty();
    }

    [Fact]
    public void FilterByReviewFilter_all_includes_unchanged_and_ignored()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/same", "Same", "Folder"),
            CreateApp("owner/changed", "Old", "Folder"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/same", "Same", "Folder"),
            CreateApp("owner/changed", "New", "Folder"),
            CreateApp("owner/new", "New App", "NewFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "2.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);
        CatalogCompareService.IgnoreChangesForCurrentVersion(source, "owner/changed");

        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.All)
            .Should()
            .HaveCount(3);
    }

    [Fact]
    public void FilterByReviewFilter_needs_review_excludes_ignored_and_unchanged()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/same", "Same", "Folder"),
            CreateApp("owner/changed", "Old", "Folder"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/same", "Same", "Folder"),
            CreateApp("owner/changed", "New", "Folder"),
            CreateApp("owner/new", "New App", "NewFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "2.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);
        CatalogCompareService.IgnoreChangesForCurrentVersion(source, "owner/changed");

        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.NeedsReview)
            .Should()
            .ContainSingle(r => r.Repository == "owner/new");
    }

    [Fact]
    public void FilterByReviewFilter_not_in_library_includes_ignored_external_only()
    {
        var local = new List<GameInfo>();
        var external = new List<GameInfo>
        {
            CreateApp("owner/new", "New App", "NewFolder"),
            CreateApp("owner/ignored", "Ignored App", "IgnoredFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);
        CatalogCompareService.IgnoreChangesForCurrentVersion(source, "owner/ignored");

        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.New)
            .Select(r => r.Repository)
            .Should()
            .BeEquivalentTo(["owner/new"]);

        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.NotInLibrary)
            .Select(r => r.Repository)
            .Should()
            .BeEquivalentTo(["owner/new", "owner/ignored"]);
    }

    [Fact]
    public void FilterByReviewFilter_not_in_library_excludes_hidden_and_non_external_rows()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/local-only", "Local Only", "LocalFolder"),
            CreateApp("owner/changed", "Old", "Folder"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/local-only"),
            CreateApp("owner/changed", "New", "Folder"),
            CreateApp("owner/new", "New App", "NewFolder"),
            CreateApp("owner/hidden", "Hidden App", "HiddenFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        CatalogCompareService.HideFromReview(source, "owner/hidden");

        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.NotInLibrary)
            .Select(r => r.Repository)
            .Should()
            .BeEquivalentTo(["owner/new"]);

        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.Hidden)
            .Select(r => r.Repository)
            .Should()
            .Contain("owner/hidden");
    }

    [Fact]
    public void BuildCompareRows_sets_icon_url_from_external_or_local()
    {
        var local = CreateApp("owner/app", "Local");
        local.GameIconUrl = "https://example.com/local.png";
        var external = CreateApp("owner/app", "External");
        external.GameIconUrl = "https://example.com/external.png";

        var row = CatalogCompareService.BuildCompareRows([local], [external]).Single();
        row.IconUrl.Should().Be("https://example.com/external.png");
    }

    [Fact]
    public void HideFromReview_excludes_row_from_all_and_needs_review_filters()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/same", "Same", "Folder"),
            CreateApp("owner/changed", "Old", "Folder"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/same", "Same", "Folder"),
            CreateApp("owner/changed", "New", "Folder"),
            CreateApp("owner/new", "New App", "NewFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "2.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        CatalogCompareService.HideFromReview(source, "owner/new");

        CatalogCompareService.IsActionableRow(rows.Single(r => r.Repository == "owner/new"), source).Should().BeFalse();
        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.All)
            .Select(r => r.Repository)
            .Should().BeEquivalentTo(["owner/same", "owner/changed"]);
        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.NeedsReview)
            .Should()
            .ContainSingle(r => r.Repository == "owner/changed");
        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.New)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Hidden_filter_shows_only_hidden_rows()
    {
        var local = new List<GameInfo> { CreateApp("owner/local-only") };
        var external = new List<GameInfo>
        {
            CreateApp("owner/local-only"),
            CreateApp("owner/new", "New App", "NewFolder"),
        };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        CatalogCompareService.HideFromReview(source, "owner/new");
        CatalogCompareService.HideFromReview(source, "owner/local-only");

        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.Hidden)
            .Select(r => r.Repository)
            .Should()
            .BeEquivalentTo(["owner/local-only", "owner/new"]);
    }

    [Fact]
    public void UnhideFromReview_restores_row_to_review_filters()
    {
        var local = new List<GameInfo>();
        var external = new List<GameInfo> { CreateApp("owner/new", "New App", "NewFolder") };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        CatalogCompareService.HideFromReview(source, "owner/new");
        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.New).Should().BeEmpty();

        CatalogCompareService.UnhideFromReview(source, "owner/new");
        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.New)
            .Should()
            .ContainSingle(r => r.Repository == "owner/new");
    }

    [Fact]
    public void Hidden_repositories_survive_acknowledge_source_version()
    {
        var local = new List<GameInfo>();
        var external = new List<GameInfo> { CreateApp("owner/new", "New App", "NewFolder") };
        var source = new AppCatalogSource
        {
            CachedListVersion = "2.0.0",
            AcknowledgedListVersion = "1.0.0",
        };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        CatalogCompareService.HideFromReview(source, "owner/new");

        var service = new AppCatalogService();
        service.AcknowledgeSourceVersion(source);

        source.HiddenFromReviewRepositories.Should().Contain("owner/new");
        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.Hidden)
            .Should()
            .ContainSingle(r => r.Repository == "owner/new");
        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.New).Should().BeEmpty();
    }

    [Fact]
    public void CanRemoveFromLibrary_true_when_local_present_false_for_external_only()
    {
        var local = new List<GameInfo> { CreateApp("owner/in-library", "In Library") };
        var external = new List<GameInfo>
        {
            CreateApp("owner/in-library", "In Library"),
            CreateApp("owner/not-in-library", "Not In Library"),
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);

        rows.Single(r => r.Repository == "owner/in-library").CanRemoveFromLibrary.Should().BeTrue();
        rows.Single(r => r.Repository == "owner/not-in-library").CanRemoveFromLibrary.Should().BeFalse();
    }

    [Fact]
    public void ApplyRowRemove_removes_matching_repo_and_leaves_others()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/keep", "Keep"),
            CreateApp("owner/remove", "Remove Me"),
        };
        var external = new List<GameInfo> { CreateApp("owner/remove", "Remove Me") };
        var row = CatalogCompareService.BuildCompareRows(local, external).Single();

        var updated = CatalogCompareService.ApplyRowRemove(local, row);

        updated.Should().ContainSingle(a => a.Repository == "owner/keep");
        updated.Should().NotContain(a => a.Repository == "owner/remove");
    }

    [Fact]
    public void ApplyRowRemove_no_op_when_local_is_null()
    {
        var local = new List<GameInfo> { CreateApp("owner/keep", "Keep") };
        var external = new List<GameInfo> { CreateApp("owner/new", "New App") };
        var row = CatalogCompareService.BuildCompareRows(local, external)
            .Single(r => r.Repository == "owner/new");

        var updated = CatalogCompareService.ApplyRowRemove(local, row);

        updated.Should().BeEquivalentTo(local);
    }

    [Fact]
    public void Ignored_external_only_row_excluded_from_needs_review_filter()
    {
        var local = new List<GameInfo>();
        var external = new List<GameInfo> { CreateApp("owner/removed", "Removed App") };
        var source = new AppCatalogSource { CachedListVersion = "1.0.0" };
        var rows = CatalogCompareService.BuildCompareRows(local, external);
        var row = rows.Single();

        CatalogCompareService.IsActionableRow(row, source).Should().BeTrue();

        CatalogCompareService.IgnoreChangesForCurrentVersion(source, "owner/removed");

        CatalogCompareService.IsActionableRow(row, source).Should().BeFalse();
        CatalogCompareService.FilterByReviewFilter(rows, source, CatalogReviewFilter.NeedsReview)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void SortRows_orders_by_display_name_asc_by_default()
    {
        var rows = new List<CatalogSyncRowItem>
        {
            CreateRow("owner/zebra", "Zebra App", CatalogSyncStatus.Unchanged),
            CreateRow("owner/alpha", "Alpha App", CatalogSyncStatus.Unchanged),
            CreateRow("owner/middle", "Middle App", CatalogSyncStatus.Unchanged),
        };

        CatalogCompareService.SortRows(rows, "Name")
            .Select(r => r.DisplayName)
            .Should()
            .Equal("Alpha App", "Middle App", "Zebra App");
    }

    [Fact]
    public void SortRows_orders_by_name_desc()
    {
        var rows = new List<CatalogSyncRowItem>
        {
            CreateRow("owner/alpha", "Alpha App", CatalogSyncStatus.Unchanged),
            CreateRow("owner/zebra", "Zebra App", CatalogSyncStatus.Unchanged),
        };

        CatalogCompareService.SortRows(rows, "NameDesc")
            .Select(r => r.DisplayName)
            .Should()
            .Equal("Zebra App", "Alpha App");
    }

    [Fact]
    public void SortRows_name_strips_leading_articles_when_enabled()
    {
        var rows = new List<CatalogSyncRowItem>
        {
            CreateRow("owner/zelda", "Zelda", CatalogSyncStatus.Unchanged),
            CreateRow("owner/legend", "The Legend of Zelda", CatalogSyncStatus.Unchanged),
            CreateRow("owner/hike", "A Short Hike", CatalogSyncStatus.Unchanged),
            CreateRow("owner/banjo", "Banjo", CatalogSyncStatus.Unchanged),
        };

        CatalogCompareService.SortRows(rows, "Name", ignoreArticlesWhenSorting: true)
            .Select(r => r.DisplayName)
            .Should()
            .Equal("Banjo", "The Legend of Zelda", "A Short Hike", "Zelda");
    }

    [Fact]
    public void SortRows_name_keeps_leading_articles_when_disabled()
    {
        var rows = new List<CatalogSyncRowItem>
        {
            CreateRow("owner/zelda", "Zelda", CatalogSyncStatus.Unchanged),
            CreateRow("owner/legend", "The Legend of Zelda", CatalogSyncStatus.Unchanged),
            CreateRow("owner/banjo", "Banjo", CatalogSyncStatus.Unchanged),
        };

        CatalogCompareService.SortRows(rows, "Name", ignoreArticlesWhenSorting: false)
            .Select(r => r.DisplayName)
            .Should()
            .Equal("Banjo", "The Legend of Zelda", "Zelda");
    }

    [Fact]
    public void SortRows_orders_by_repository()
    {
        var rows = new List<CatalogSyncRowItem>
        {
            CreateRow("owner/zebra", "Zebra App", CatalogSyncStatus.Unchanged),
            CreateRow("owner/alpha", "Alpha App", CatalogSyncStatus.Unchanged),
        };

        CatalogCompareService.SortRows(rows, "Repository")
            .Select(r => r.Repository)
            .Should()
            .Equal("owner/alpha", "owner/zebra");
    }

    [Fact]
    public void SortRows_groups_by_status_then_name()
    {
        var rows = new List<CatalogSyncRowItem>
        {
            CreateRow("owner/up", "Up To Date", CatalogSyncStatus.Unchanged),
            CreateRow("owner/new", "New App", CatalogSyncStatus.InExternalOnly),
            CreateRow("owner/changed", "Changed App", CatalogSyncStatus.Changed),
        };

        CatalogCompareService.SortRows(rows, "Status")
            .Select(r => r.Repository)
            .Should()
            .Equal("owner/changed", "owner/new", "owner/up");
    }

    private static CatalogSyncRowItem CreateRow(
        string repository,
        string displayName,
        CatalogSyncStatus status) =>
        new()
        {
            Repository = repository,
            DisplayName = displayName,
            Status = status,
        };
}
