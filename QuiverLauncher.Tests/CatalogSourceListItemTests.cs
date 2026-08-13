using FluentAssertions;

using QuiverLauncher.Services;



namespace QuiverLauncher.Tests;



public class CatalogSourceListItemTests

{

    [Fact]

    public void GetStatusText_returns_update_available_when_flag_set()

    {

        var source = new AppCatalogSource

        {

            UpdateAvailable = true,

            LastFetchedUtc = DateTime.UtcNow,

            CachedListVersion = "2.0.0",

        };



        CatalogSourceListItem.GetStatusText(source).Should().Be("Update available");

    }



    [Fact]

    public void GetStatusText_omits_version_summary()

    {

        var source = new AppCatalogSource

        {

            UpdateAvailable = true,

            CachedListVersion = "2.0.0",

            AcknowledgedListVersion = "1.0.0",

        };



        CatalogSourceListItem.GetStatusText(source).Should().Be("Update available");

        CatalogSourceListItem.GetStatusText(source).Should().NotContain("List version");

    }



    [Fact]

    public void GetStatusText_returns_last_error_when_present()

    {

        var source = new AppCatalogSource

        {

            LastError = "Network timeout",

            LastFetchedUtc = DateTime.UtcNow,

        };



        CatalogSourceListItem.GetStatusText(source).Should().Be("Network timeout");

    }



    [Fact]

    public void GetStatusText_returns_not_loaded_when_never_fetched()

    {

        CatalogSourceListItem.GetStatusText(new AppCatalogSource()).Should().Be("Not loaded yet");

    }



    [Fact]

    public void GetStatusText_returns_updated_timestamp_when_fetched_successfully()

    {

        var fetchedAt = new DateTime(2026, 6, 11, 14, 30, 0, DateTimeKind.Utc);

        var source = new AppCatalogSource { LastFetchedUtc = fetchedAt };



        CatalogSourceListItem.GetStatusText(source).Should().Be($"Updated {fetchedAt.ToLocalTime():g}");

    }



    [Fact]

    public void GetReviewButtonText_shows_count_when_pending()

    {

        var source = new AppCatalogSource { PendingReviewCount = 3 };



        CatalogSourceListItem.GetReviewButtonText(source).Should().Be("Review (3)");

    }



    [Fact]

    public void GetReviewButtonText_without_pending_uses_view_label()

    {

        CatalogSourceListItem.GetReviewButtonText(new AppCatalogSource()).Should().Be("View");

    }



    [Fact]

    public void FormatUsageStats_returns_empty_when_list_not_loaded()

    {

        CatalogSourceListItem.FormatUsageStats(new AppCatalogSource()).Should().BeEmpty();

    }



    [Fact]

    public void FormatUsageStats_uses_singular_app_label()

    {

        var source = new AppCatalogSource { LibraryAppCount = 1, ListAppCount = 1 };

        CatalogSourceListItem.FormatUsageStats(source).Should().Be("Using 1/1 app from this list");

    }



    [Fact]

    public void FormatUsageStats_uses_plural_app_label()

    {

        var source = new AppCatalogSource { LibraryAppCount = 3, ListAppCount = 24 };

        CatalogSourceListItem.FormatUsageStats(source).Should().Be("Using 3/24 apps from this list");

    }



    [Fact]

    public void FromSource_maps_pending_review_count()

    {

        var source = new AppCatalogSource

        {

            Id = "id-1",

            Name = "NAS",

            Location = "http://example/apps.json",

            PendingReviewCount = 5,

        };



        var item = CatalogSourceListItem.FromSource(source);



        item.PendingReviewCount.Should().Be(5);

        item.ReviewButtonText.Should().Be("Review (5)");

        item.PendingReviewBadgeVisible.Should().BeTrue();

        item.LocationToolTip.Should().Be("http://example/apps.json");

    }



    [Fact]

    public void FromSource_maps_description_and_version_parts()

    {

        var source = new AppCatalogSource

        {

            CachedListVersion = "1.0.2",

            AcknowledgedListVersion = "1.0.0",

            Description = "N64 recompilation projects",

        };



        var item = CatalogSourceListItem.FromSource(source);



        item.Description.Should().Be("N64 recompilation projects");

        item.DescriptionVisible.Should().BeTrue();

        item.ListVersionText.Should().Be("1.0.2");

        item.LastReviewedText.Should().Be("1.0.0");

        item.LastReviewedUnreviewed.Should().BeFalse();

        item.VersionRowVisible.Should().BeTrue();

    }



    [Fact]

    public void IsAllReviewedSource_true_when_versions_match_and_no_pending()

    {

        var source = new AppCatalogSource

        {

            CachedListVersion = "1.0.2",

            AcknowledgedListVersion = "1.0.2",

            PendingReviewCount = 0,

            UpdateAvailable = false,

        };



        CatalogSourceListItem.IsAllReviewedSource(source).Should().BeTrue();

        CatalogSourceListItem.FromSource(source).IsAllReviewed.Should().BeTrue();

        CatalogSourceListItem.FromSource(source).AllReviewedVisible.Should().BeTrue();

    }



    [Fact]

    public void ShowReviewPendingStyle_false_when_disabled()

    {

        var source = new AppCatalogSource

        {

            Enabled = false,

            PendingReviewCount = 2,

        };



        var item = CatalogSourceListItem.FromSource(source);



        item.ShowReviewPendingStyle.Should().BeFalse();

        item.NeedsReviewHighlight.Should().BeFalse();

    }



    [Fact]

    public void ShowReviewPendingStyle_true_when_enabled_with_pending()

    {

        var source = new AppCatalogSource

        {

            Enabled = true,

            PendingReviewCount = 2,

            UpdateAvailable = true,

        };



        var item = CatalogSourceListItem.FromSource(source);



        item.ShowReviewPendingStyle.Should().BeTrue();

        item.NeedsReviewHighlight.Should().BeTrue();

    }

    [Fact]
    public void FormatUsageStatsShort_uses_compact_label()
    {
        CatalogSourceListItem.FormatUsageStatsShort(5, 5).Should().Be("5/5 apps in library");
        CatalogSourceListItem.FormatUsageStatsShort(1, 1).Should().Be("1/1 app in library");
        CatalogSourceListItem.FormatUsageStatsShort(0, 0).Should().BeEmpty();
    }

    [Fact]
    public void GetFetchStatusText_returns_timestamp_when_no_warning()
    {
        var fetchedAt = new DateTime(2026, 7, 4, 10, 3, 0, DateTimeKind.Utc);
        var source = new AppCatalogSource { LastFetchedUtc = fetchedAt };

        CatalogSourceListItem.GetFetchStatusText(source).Should().Be($"Updated {fetchedAt.ToLocalTime():g}");
    }

    [Fact]
    public void GetStatusWarningText_prioritizes_update_over_error_and_fetch()
    {
        var source = new AppCatalogSource
        {
            UpdateAvailable = true,
            LastError = "Network timeout",
            LastFetchedUtc = DateTime.UtcNow,
        };

        var warning = CatalogSourceListItem.GetStatusWarningText(source);
        warning.Should().NotBeNull();
        warning!.Value.Text.Should().Be("Update available");
        warning.Value.IsError.Should().BeFalse();
    }

    [Fact]
    public void GetStatusWarningText_returns_error_when_no_update()
    {
        var warning = CatalogSourceListItem.GetStatusWarningText(new AppCatalogSource
        {
            LastError = "Network timeout",
        });

        warning.Should().NotBeNull();
        warning!.Value.Text.Should().Be("Network timeout");
        warning.Value.IsError.Should().BeTrue();
    }

    [Fact]
    public void FromSource_splits_warning_and_fetch_status()
    {
        var fetchedAt = new DateTime(2026, 7, 4, 10, 3, 0, DateTimeKind.Utc);
        var source = new AppCatalogSource
        {
            UpdateAvailable = true,
            LastFetchedUtc = fetchedAt,
            LibraryAppCount = 2,
            ListAppCount = 5,
        };

        var item = CatalogSourceListItem.FromSource(source);

        item.StatusWarningText.Should().Be("Update available");
        item.StatusWarningVisible.Should().BeTrue();
        item.StatusWarningIsWarning.Should().BeTrue();
        item.FetchStatusText.Should().BeEmpty();
        item.FetchStatusVisible.Should().BeFalse();
    }

    [Fact]
    public void FromSource_sets_usage_stats_full_library_when_all_apps_used()
    {
        var source = new AppCatalogSource
        {
            LibraryAppCount = 5,
            ListAppCount = 5,
            CachedListVersion = "1.0.0",
            AcknowledgedListVersion = "1.0.0",
        };

        var item = CatalogSourceListItem.FromSource(source);

        item.UsageStatsShort.Should().Be("5/5 apps in library");
        item.UsageStatsFullLibrary.Should().BeTrue();
    }

    [Fact]
    public void FromSource_version_line_omits_reviewed_suffix_when_all_reviewed()
    {
        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            AcknowledgedListVersion = "1.0.0",
            PendingReviewCount = 0,
            UpdateAvailable = false,
        };

        var item = CatalogSourceListItem.FromSource(source);

        item.VersionLineText.Should().Be("List v1.0.0");
        item.VersionLineUnreviewed.Should().BeFalse();
    }

    [Fact]
    public void FromSource_version_line_shows_not_yet_when_unreviewed()
    {
        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.0",
            AcknowledgedListVersion = null,
        };

        var item = CatalogSourceListItem.FromSource(source);

        item.VersionLineText.Should().Be("List v1.0.0 · Reviewed not yet");
        item.VersionLineUnreviewed.Should().BeTrue();
    }

    [Fact]
    public void FromSource_version_line_shows_both_versions_when_reviewed_but_outdated()
    {
        var source = new AppCatalogSource
        {
            CachedListVersion = "1.0.2",
            AcknowledgedListVersion = "1.0.0",
            UpdateAvailable = true,
        };

        var item = CatalogSourceListItem.FromSource(source);

        item.VersionLineText.Should().Be("List v1.0.2 · Reviewed v1.0.0");
    }

    [Fact]
    public void FromSource_meta_strip_visible_when_any_meta_present()
    {
        CatalogSourceListItem.FromSource(new AppCatalogSource
        {
            LastFetchedUtc = DateTime.UtcNow,
        }).MetaStripVisible.Should().BeTrue();

        CatalogSourceListItem.FromSource(new AppCatalogSource()).MetaStripVisible.Should().BeTrue();
    }

    [Fact]
    public void GetStatusWarningText_formats_404_as_list_not_found()
    {
        var warning = CatalogSourceListItem.GetStatusWarningText(new AppCatalogSource
        {
            LastError = "Response status code does not indicate success: 404 (Not Found).",
        });

        warning.Should().NotBeNull();
        warning!.Value.Text.Should().Be("List not found");
        warning.Value.IsError.Should().BeTrue();
    }

    [Fact]
    public void GetStatusWarningText_formats_cached_404_as_kept_last_copy()
    {
        var warning = CatalogSourceListItem.GetStatusWarningText(new AppCatalogSource
        {
            LastError = "Response status code does not indicate success: 404 (Not Found). (using cached copy)",
        });

        warning.Should().NotBeNull();
        warning!.Value.Text.Should().Be("List not found (kept last copy)");
        warning.Value.IsError.Should().BeTrue();
    }

}


