using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Tests;

public class CatalogCompareServiceTests
{
    private static GameInfo CreateApp(
        string repository,
        string name = "Test App",
        string? folderName = null,
        string? tags = null)
    {
        return new GameInfo
        {
            Repository = repository,
            Name = name,
            FolderName = folderName ?? repository.Replace('/', '-'),
            Tags = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? [],
        };
    }

    [Fact]
    public void FindRowIndexForLibraryApp_locates_changed_local_row()
    {
        var local = CreateApp("owner/shared", "Old Name");
        var other = CreateApp("owner/other", "Other");
        var rows = CatalogCompareService.BuildCompareRows(
            [local, other],
            [CreateApp("owner/shared", "New Name"), CreateApp("owner/other", "Other")]);

        var index = CatalogCompareService.FindRowIndexForLibraryApp(rows, local);
        index.Should().BeGreaterThanOrEqualTo(0);
        CatalogCompareService.MatchesLocalApp(local, rows[index]).Should().BeTrue();
        rows[index].Status.Should().Be(CatalogSyncStatus.Changed);

        CatalogCompareService.FindRowIndexForLibraryApp(rows, CreateApp("owner/missing", "Missing"))
            .Should().Be(-1);
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
        rows.Should().Contain(r => r.Repository == "owner/shared" && r.Status == CatalogSyncStatus.Changed && r.StatusShortLabel == "Changed");
    }

    [Fact]
    public void BuildCompareRows_grid_title_keeps_project_on_separate_line()
    {
        var external = new GameInfo
        {
            Repository = "owner/mm",
            Name = "Majora's Mask",
            Project = "2 Ship 2 Harkinian",
            FolderName = "Majora-2S2H",
        };

        var row = CatalogCompareService.BuildCompareRows([], [external]).Should().ContainSingle().Subject;
        row.DisplayName.Should().Be("Majora's Mask (2 Ship 2 Harkinian)");
        row.TitleName.Should().Be("Majora's Mask");
        row.ProjectSubtitle.Should().Be("2 Ship 2 Harkinian");
        row.HasProjectSubtitle.Should().BeTrue();
        row.HasStatusBadge.Should().BeTrue();
        row.StatusShortLabel.Should().Be("New");
        row.HasRepository.Should().BeTrue();
    }

    [Fact]
    public void BuildCompareRows_unchanged_grid_hides_up_to_date_badge()
    {
        var local = CreateApp("owner/same", "Same App");
        var external = CreateApp("owner/same", "Same App");

        var row = CatalogCompareService.BuildCompareRows([local], [external]).Should().ContainSingle().Subject;
        row.Status.Should().Be(CatalogSyncStatus.Unchanged);
        row.StatusShortLabel.Should().Be("Up to date");
        row.HasStatusBadge.Should().BeFalse();
    }

    [Fact]
    public void CatalogSyncRowItem_scrolls_title_when_hovered_or_gamepad_focused()
    {
        var row = CatalogCompareService.BuildCompareRows([], [CreateApp("owner/app", "App")]).Should().ContainSingle().Subject;
        row.ShouldScrollTitle.Should().BeFalse();

        row.IsHovered = true;
        row.ShouldScrollTitle.Should().BeTrue();

        row.IsHovered = false;
        row.IsGamepadFocused = true;
        row.ShouldScrollTitle.Should().BeTrue();

        row.IsGamepadFocused = false;
        row.ShouldScrollTitle.Should().BeFalse();
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
    public void Local_preferredVersion_pin_does_not_mark_library_row_changed()
    {
        var local = CreateApp("owner/app", "App");
        local.PreferredVersion = "v0.1-11609";
        var external = CreateApp("owner/app", "App");
        external.PreferredVersion = null;

        CatalogCompareService.IsLibrarySyncedWithCatalog(local, external).Should().BeTrue();
        CatalogCompareService.GetChangedFields(local, external).Should().NotContain("preferredVersion");

        var rows = CatalogCompareService.BuildCompareRows([local], [external]);
        rows.Should().ContainSingle(r => r.Repository == "owner/app" && r.Status == CatalogSyncStatus.Unchanged);

        CatalogCompareService.MergeExternalIntoLocal(local, external).PreferredVersion.Should().Be("v0.1-11609");
        CatalogCompareService.ReplaceFromExternal(local, external).PreferredVersion.Should().Be("v0.1-11609");
    }

    [Fact]
    public void Local_extra_tags_do_not_mark_library_row_changed()
    {
        var local = CreateApp("owner/app", "App");
        local.Tags = ["n64", "recomp", "bla bla"];
        var external = CreateApp("owner/app", "App");
        external.Tags = ["n64", "recomp"];

        CatalogCompareService.IsLibrarySyncedWithCatalog(local, external).Should().BeTrue();
        CatalogCompareService.GetChangedFields(local, external).Should().NotContain("tags");

        var rows = CatalogCompareService.BuildCompareRows([local], [external]);
        rows.Should().ContainSingle(r => r.Repository == "owner/app" && r.Status == CatalogSyncStatus.Unchanged);

        var merged = CatalogCompareService.MergeExternalIntoLocal(local, external);
        merged.Tags.Should().BeEquivalentTo(["n64", "recomp", "bla bla"]);
        CatalogCompareService.IsLibrarySyncedWithCatalog(merged, external).Should().BeTrue();
    }

    [Fact]
    public void Catalog_added_tag_is_changed_until_merge_unions_it()
    {
        var local = CreateApp("owner/app", "App");
        local.Tags = ["n64"];
        var external = CreateApp("owner/app", "App");
        external.Tags = ["n64", "recomp"];

        var rows = CatalogCompareService.BuildCompareRows([local], [external]);
        var row = rows.Should().ContainSingle(r => r.Repository == "owner/app").Subject;
        row.Status.Should().Be(CatalogSyncStatus.Changed);
        row.ChangedFields.Should().Contain("tags");

        var mergedApps = CatalogCompareService.ApplyRowMerge([local], row);
        TagHelper.NormalizeTags(mergedApps[0].Tags).Should().BeEquivalentTo(["n64", "recomp"]);
        CatalogCompareService.BuildCompareRows(mergedApps, [external])
            .Should()
            .ContainSingle(r => r.Repository == "owner/app" && r.Status == CatalogSyncStatus.Unchanged);

        var replacedApps = CatalogCompareService.ApplyRowReplace([local], row);
        TagHelper.NormalizeTags(replacedApps[0].Tags).Should().BeEquivalentTo(["n64", "recomp"]);
    }

    [Fact]
    public void Replace_drops_extra_local_tags_but_keeps_preferredVersion()
    {
        var local = CreateApp("owner/app", "App");
        local.Tags = ["n64", "favorites"];
        local.PreferredVersion = "v1.0.0";
        var external = CreateApp("owner/app", "App");
        external.Tags = ["n64", "recomp"];
        external.PreferredVersion = null;

        var replaced = CatalogCompareService.ReplaceFromExternal(local, external);
        TagHelper.NormalizeTags(replaced.Tags).Should().BeEquivalentTo(["n64", "recomp"]);
        replaced.PreferredVersion.Should().Be("v1.0.0");
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
    public void MergeAndReplace_preserve_local_skipped_version()
    {
        var local = CreateApp("owner/app", "Local");
        local.SkippedUpdateVersion = "v1.2.3";

        var external = CreateApp("owner/app", "External");
        external.SkippedUpdateVersion = null;

        CatalogCompareService.MergeExternalIntoLocal(local, external).SkippedUpdateVersion.Should().Be("v1.2.3");
        CatalogCompareService.ReplaceFromExternal(local, external).SkippedUpdateVersion.Should().Be("v1.2.3");
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
    public void BuildCompareRows_blocks_add_when_folder_already_used_by_another_library_app()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/existing", "Existing App", "SharedFolder"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/existing", "Existing App", "SharedFolder"),
            CreateApp("owner/new", "New App", "SharedFolder"),
        };

        var rows = CatalogCompareService.BuildCompareRows(local, external);
        var blocked = rows.Should().ContainSingle(r => r.Repository == "owner/new").Subject;

        blocked.Status.Should().Be(CatalogSyncStatus.InExternalOnly);
        blocked.CanAdd.Should().BeFalse();
        blocked.HasAddBlockedReason.Should().BeTrue();
        blocked.AddBlockedReason.Should().Contain("SharedFolder");
        blocked.AddBlockedReason.Should().Contain("Existing App");

        CatalogCompareService.ApplyRowAdd(local, blocked).Should().BeEquivalentTo(local);
    }

    [Fact]
    public void ApplyAddAllExternalOnly_skips_blocked_folder_collisions()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/existing", "Existing App", "SharedFolder"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/existing", "Existing App", "SharedFolder"),
            CreateApp("owner/new", "New App", "SharedFolder"),
            CreateApp("owner/other", "Other App", "OtherFolder"),
        };
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        var updated = CatalogCompareService.ApplyAddAllExternalOnly(local, rows);

        updated.Select(a => a.Repository).Should().BeEquivalentTo(["owner/existing", "owner/other"]);
        updated.Count(a => string.Equals(a.FolderName, "SharedFolder", StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(1);
        CatalogCompareService.FormatAddBlockedMessage(rows.Where(r => r.HasAddBlockedReason).ToList())
            .Should()
            .Contain("SharedFolder");
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
    public void GetAppsNeedingInstallSync_returns_new_and_layout_changed_apps_only()
    {
        var previous = new List<GameInfo>
        {
            CreateApp("owner/same", "Same"),
            CreateApp("owner/folder", "Folder App", "OldFolder"),
            CreateApp("owner/files", "Files App"),
        };
        previous[2].FilesToAdd = ["save.dat"];

        var current = new List<GameInfo>
        {
            CreateApp("owner/same", "Renamed But Same Install"),
            CreateApp("owner/folder", "Folder App", "NewFolder"),
            CreateApp("owner/files", "Files App"),
            CreateApp("owner/new", "New App"),
        };
        current[2].FilesToAdd = ["save.dat", "extra.bin"];

        var mutated = CatalogCompareService.GetAppsNeedingInstallSync(previous, current);

        mutated.Select(a => a.Repository).Should().BeEquivalentTo([
            "owner/folder",
            "owner/files",
            "owner/new",
        ]);
    }

    [Fact]
    public void ApplyMergeAllChanged_unions_local_tags_and_takes_catalog_name()
    {
        var local = new List<GameInfo> { CreateApp("owner/app", "Old Name", "Folder") };
        local[0].Tags = ["user-tag"];
        var external = new List<GameInfo> { CreateApp("owner/app", "New Name", "Folder") };
        external[0].Tags = ["n64"];
        var rows = CatalogCompareService.BuildCompareRows(local, external);

        var updated = CatalogCompareService.ApplyMergeAllChanged(local, rows);

        updated.Single().Name.Should().Be("New Name");
        TagHelper.NormalizeTags(updated.Single().Tags).Should().BeEquivalentTo(["user-tag", "n64"]);
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
        row.HasReviewCardFooter.Should().BeTrue();
        row.FieldDiffs.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildCompareRows_new_app_includes_folder_and_tags_preview()
    {
        var external = CreateApp("owner/new", "Chameleon Twist", "ChameleonTwist-Recomp");
        external.Project = "Chameleon Twist: Recompiled";
        external.Tags = ["recomp", "n64"];
        external.GameIconUrl = "https://example.com/icon.png";

        var row = CatalogCompareService.BuildCompareRows([], [external]).Single();

        row.HasInlineDiff.Should().BeTrue();
        row.HasReviewCardFooter.Should().BeTrue();
        row.FieldDiffs.Select(d => d.FieldLabel).Should().Equal("Project", "Folder", "Tags");
        row.FieldDiffs.Single(d => d.FieldLabel == "Project").ExternalValue.Should().Be("Chameleon Twist: Recompiled");
        row.FieldDiffs.Single(d => d.FieldLabel == "Folder").ExternalValue.Should().Be("ChameleonTwist-Recomp");
        row.FieldDiffs.Single(d => d.FieldLabel == "Tags").TagDiffs.Select(t => t.Tag).Should().Equal("recomp", "n64");
        row.IconUrl.Should().Be("https://example.com/icon.png");
    }

    [Fact]
    public void BuildFieldDiffs_new_app_includes_preview_rows()
    {
        var external = CreateApp("owner/new", "App", "Folder");
        external.Project = "Project";
        external.Tags = ["n64"];
        external.GameIconUrl = "https://example.com/icon.png";
        external.PreferredVersion = "1.2.3";
        external.ModsPath = "mods";
        external.ModsSources =
        [
            new GameModSource
            {
                Provider = ModProviderIds.Thunderstore,
                SourceUrl = "https://thunderstore.io/c/banjo-recompiled/",
            },
        ];

        var diffs = CatalogSyncFieldDiffBuilder.BuildFieldDiffs(
            CatalogSyncStatus.InExternalOnly,
            local: null,
            external,
            []);

        diffs.Select(d => d.FieldLabel)
            .Should()
            .Equal("Project", "Folder", "Tags", "Preferred version", "Mods");
        diffs.Single(d => d.FieldLabel == "Project").ExternalValue.Should().Be("Project");
        diffs.Single(d => d.FieldLabel == "Preferred version").ExternalValue.Should().Be("1.2.3");
        diffs.Single(d => d.FieldLabel == "Mods").ExternalValue.Should().Contain("path=mods");
        diffs.Single(d => d.FieldLabel == "Mods").ExternalValue.Should().Contain("thunderstore.io");

        var row = CatalogCompareService.BuildCompareRows([], [external]).Single();
        row.HasInlineDiff.Should().BeTrue();
        row.HasReviewCardFooter.Should().BeTrue();
        row.FieldDiffs.Should().HaveCount(5);
    }

    [Fact]
    public void BuildFieldDiffs_puts_project_directly_under_name()
    {
        var local = CreateApp("owner/app", "Old Name", "Folder");
        local.Project = "Old Project";
        var external = CreateApp("owner/app", "New Name", "Folder");
        external.Project = "New Project";
        external.Tags = ["n64"];

        var diffs = CatalogSyncFieldDiffBuilder.BuildFieldDiffs(
            CatalogSyncStatus.Changed,
            local,
            external,
            ["tags", "project", "name"]);

        diffs.Select(d => d.FieldLabel).Should().Equal("Name", "Project", "Tags");
    }

    [Fact]
    public void BuildCompareRows_up_to_date_details_show_project_folder_and_tags()
    {
        var local = CreateApp("sonicdcer/ExtremeGRecomp", "Extreme-G", "Extreme-G");
        local.Project = "ExtremeGRecomp";
        local.Tags = ["n64", "recomp"];
        var external = CreateApp("sonicdcer/ExtremeGRecomp", "Extreme-G", "Extreme-G");
        external.Project = "ExtremeGRecomp";
        external.Tags = ["n64", "recomp"];

        var row = CatalogCompareService.BuildCompareRows([local], [external]).Should().ContainSingle().Subject;

        row.Status.Should().Be(CatalogSyncStatus.Unchanged);
        row.FieldDiffs.Should().BeEmpty();
        row.HasInlineDiff.Should().BeFalse();
        row.HasDetailsFields.Should().BeTrue();
        row.DetailsFields.Select(d => d.FieldLabel).Should().Equal("Project", "Folder", "Tags");
        row.DetailsFields.Single(d => d.FieldLabel == "Project").IsSnapshot.Should().BeTrue();
        row.DetailsFields.Single(d => d.FieldLabel == "Project").ExternalValue.Should().Be("ExtremeGRecomp");
        row.DetailsFields.Single(d => d.FieldLabel == "Folder").ExternalValue.Should().Be("Extreme-G");
        row.DetailsFields.Single(d => d.FieldLabel == "Tags").TagDiffs
            .Should().OnlyContain(t => t.Kind == CatalogSyncTagDiffKind.Shared);
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
    public void ApplyReviewActionButtons_hidden_filter_shows_unhide_not_remove()
    {
        var local = new List<GameInfo> { CreateApp("owner/in-library", "In Library") };
        var external = new List<GameInfo>
        {
            CreateApp("owner/in-library", "In Library"),
            CreateApp("owner/not-in-library", "Not In Library"),
        };
        var rows = CatalogCompareService.BuildCompareRows(local, external);
        var source = new AppCatalogSource
        {
            HiddenFromReviewRepositories = ["owner/in-library", "owner/not-in-library"],
        };

        foreach (var row in rows)
            CatalogCompareService.ApplyReviewActionButtons(row, source, CatalogReviewFilter.Hidden);

        var inLibrary = rows.Single(r => r.Repository == "owner/in-library");
        inLibrary.ShowHideButton.Should().BeFalse();
        inLibrary.ShowCardHideButton.Should().BeFalse();
        inLibrary.ShowUnhideButton.Should().BeTrue();
        inLibrary.ShowRemoveFromLibrary.Should().BeFalse();
        inLibrary.CanRemoveFromLibrary.Should().BeTrue();

        var notAdded = rows.Single(r => r.Repository == "owner/not-in-library");
        notAdded.ShowHideButton.Should().BeFalse();
        notAdded.ShowCardHideButton.Should().BeFalse();
        notAdded.ShowUnhideButton.Should().BeTrue();
        notAdded.ShowRemoveFromLibrary.Should().BeFalse();
    }

    [Fact]
    public void ApplyReviewActionButtons_non_hidden_filter_shows_hide_and_remove()
    {
        var local = new List<GameInfo> { CreateApp("owner/in-library", "In Library") };
        var external = new List<GameInfo>
        {
            CreateApp("owner/in-library", "In Library"),
            CreateApp("owner/not-in-library", "Not In Library"),
        };
        var rows = CatalogCompareService.BuildCompareRows(local, external);
        var source = new AppCatalogSource();

        foreach (var row in rows)
            CatalogCompareService.ApplyReviewActionButtons(row, source, CatalogReviewFilter.All);

        var inLibrary = rows.Single(r => r.Repository == "owner/in-library");
        inLibrary.ShowHideButton.Should().BeTrue();
        inLibrary.ShowCardHideButton.Should().BeFalse();
        inLibrary.ShowUnhideButton.Should().BeFalse();
        inLibrary.ShowRemoveFromLibrary.Should().BeTrue();

        var notAdded = rows.Single(r => r.Repository == "owner/not-in-library");
        notAdded.ShowHideButton.Should().BeTrue();
        notAdded.ShowCardHideButton.Should().BeTrue();
        notAdded.ShowUnhideButton.Should().BeFalse();
        notAdded.ShowRemoveFromLibrary.Should().BeFalse();
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
    public void IndexByIdentityKey_keeps_same_repo_on_different_sources()
    {
        var github = new GameInfo
        {
            Repository = "sonicdcer/DNZHRecomp",
            RepositorySource = "github",
            Name = "GitHub copy",
        };
        var gitlab = new GameInfo
        {
            Repository = "sonicdcer/DNZHRecomp",
            RepositorySource = "gitlab",
            Name = "GitLab copy",
        };

        var indexed = CatalogCompareService.IndexByIdentityKey([github, gitlab]);

        indexed.Should().HaveCount(2);
        indexed[github.IdentityKey].Name.Should().Be("GitHub copy");
        indexed[gitlab.IdentityKey].Name.Should().Be("GitLab copy");
    }

    [Fact]
    public void IndexByIdentityKey_keeps_first_when_identity_is_duplicated()
    {
        var first = new GameInfo
        {
            Repository = "sonicdcer/DNZHRecomp",
            RepositorySource = "github",
            Name = "First",
        };
        var second = new GameInfo
        {
            Repository = "sonicdcer/DNZHRecomp",
            RepositorySource = "github",
            Name = "Second",
        };

        var indexed = CatalogCompareService.IndexByIdentityKey([first, second]);

        indexed.Should().ContainSingle();
        indexed[first.IdentityKey].Name.Should().Be("First");
    }

    [Fact]
    public void BuildCompareRows_does_not_throw_when_local_has_duplicate_identity()
    {
        var local = new List<GameInfo>
        {
            CreateApp("owner/shared", "First"),
            CreateApp("owner/shared", "Second"),
        };
        var external = new List<GameInfo>
        {
            CreateApp("owner/shared", "Catalog"),
        };

        var act = () => CatalogCompareService.BuildCompareRows(local, external);

        act.Should().NotThrow();
        var rows = act();
        rows.Should().ContainSingle(r => r.Repository == "owner/shared");
        rows[0].Local!.Name.Should().Be("First");
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

    [Fact]
    public void Grid_card_shows_one_primary_action_and_keeps_it_off_the_more_menu()
    {
        var add = CreateRow("owner/new", "New", CatalogSyncStatus.InExternalOnly);
        add.ShowGridCardPrimaryAdd.Should().BeTrue();
        add.ShowGridCardPrimaryMerge.Should().BeFalse();
        add.ShowMenuAdd.Should().BeFalse();
        add.ShowMenuMerge.Should().BeFalse();

        var merge = CreateRow("owner/changed", "Changed", CatalogSyncStatus.Changed);
        merge.ShowGridCardPrimaryAdd.Should().BeFalse();
        merge.ShowGridCardPrimaryMerge.Should().BeTrue();
        merge.ShowMenuAdd.Should().BeFalse();
        merge.ShowMenuMerge.Should().BeFalse();
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
