using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogSyncTests
{
    [Fact]
    public async Task FetchSourceAsync_sets_UpdateAvailable_when_version_differs()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var cachePath = Path.Combine(service.CatalogSourcesCacheFolder, $"{sourceId}.json");
            await File.WriteAllTextAsync(cachePath, """
                {
                  "version": "2.0.0",
                  "apps": [
                    { "name": "App", "repository": "owner/app", "folderName": "AppFolder" }
                  ]
                }
                """);

            var source = new AppCatalogSource
            {
                Id = sourceId,
                Name = "Test",
                Location = "unused",
                Enabled = true,
                AcknowledgedListVersion = "1.0.0",
            };

            using var httpClient = new HttpClient();
            var fetched = await service.FetchSourceAsync(httpClient, source);

            fetched.Should().BeTrue();
            source.CachedListVersion.Should().Be("2.0.0");
            source.UpdateAvailable.Should().BeTrue();
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task RegisterNewSourceAsync_acknowledges_initial_version()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var source = new AppCatalogSource
            {
                Id = sourceId,
                Name = "Test",
                Location = "unused",
                Enabled = true,
            };

            var json = """
                {
                  "version": "1.5.0",
                  "apps": [
                    { "name": "App", "repository": "owner/app", "folderName": "AppFolder" }
                  ]
                }
                """;

            await service.RegisterNewSourceAsync(new HttpClient(), source, json);

            source.CachedListVersion.Should().Be("1.5.0");
            source.AcknowledgedListVersion.Should().Be("1.5.0");
            source.UpdateAvailable.Should().BeFalse();
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task LoadLocalCatalogAsync_returns_only_local_apps()
    {
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var settings = new AppSettings();
            settings.EnsureInitialized();

            await service.SaveLocalAppsAsync(
            [
                new GameInfo { Repository = "owner/local", Name = "Local", FolderName = "LocalFolder" },
            ]);

            var apps = await service.LoadLocalCatalogAsync(settings);

            apps.Should().ContainSingle();
            apps[0].Repository.Should().Be("owner/local");
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task RefreshUpdateAvailableAsync_auto_acknowledges_when_no_actionable_rows()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var cachePath = Path.Combine(service.CatalogSourcesCacheFolder, $"{sourceId}.json");
            await File.WriteAllTextAsync(cachePath, """
                {
                  "version": "1.0.0",
                  "apps": [
                    { "name": "App", "repository": "owner/app", "folderName": "AppFolder" }
                  ]
                }
                """);

            await service.SaveLocalAppsAsync(
            [
                new GameInfo { Repository = "owner/app", Name = "App", FolderName = "AppFolder" },
            ]);

            var source = new AppCatalogSource
            {
                Id = sourceId,
                Name = "Test",
                CachedListVersion = "1.0.0",
                AcknowledgedListVersion = "0",
                UpdateAvailable = true,
            };

            await service.RefreshUpdateAvailableAsync(source);

            source.AcknowledgedListVersion.Should().Be("1.0.0");
            source.UpdateAvailable.Should().BeFalse();
            source.PendingReviewCount.Should().Be(0);
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task RefreshUpdateAvailableAsync_does_not_auto_ack_when_actionable_rows_remain()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var cachePath = Path.Combine(service.CatalogSourcesCacheFolder, $"{sourceId}.json");
            await File.WriteAllTextAsync(cachePath, """
                {
                  "version": "1.0.0",
                  "apps": [
                    { "name": "App", "repository": "owner/app", "folderName": "AppFolder" }
                  ]
                }
                """);

            await service.SaveLocalAppsAsync([]);

            var source = new AppCatalogSource
            {
                Id = sourceId,
                Name = "Test",
                CachedListVersion = "1.0.0",
                AcknowledgedListVersion = "0",
            };

            await service.RefreshUpdateAvailableAsync(source);

            source.AcknowledgedListVersion.Should().Be("0");
            source.UpdateAvailable.Should().BeTrue();
            source.PendingReviewCount.Should().Be(1);
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task RefreshUpdateAvailableAsync_counts_actionable_rows_when_version_already_acknowledged()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var cachePath = Path.Combine(service.CatalogSourcesCacheFolder, $"{sourceId}.json");
            await File.WriteAllTextAsync(cachePath, """
                {
                  "version": "1.0.0",
                  "apps": [
                    { "name": "App", "repository": "owner/app", "folderName": "AppFolder" }
                  ]
                }
                """);

            await service.SaveLocalAppsAsync([]);

            var source = new AppCatalogSource
            {
                Id = sourceId,
                Name = "Test",
                CachedListVersion = "1.0.0",
                AcknowledgedListVersion = "1.0.0",
            };

            await service.RefreshUpdateAvailableAsync(source);

            source.PendingReviewCount.Should().Be(1);
            source.UpdateAvailable.Should().BeTrue();
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task RefreshUpdateAvailableAsync_zero_pending_when_removed_app_is_ignored()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var cachePath = Path.Combine(service.CatalogSourcesCacheFolder, $"{sourceId}.json");
            await File.WriteAllTextAsync(cachePath, """
                {
                  "version": "1.0.0",
                  "apps": [
                    { "name": "App", "repository": "owner/app", "folderName": "AppFolder" }
                  ]
                }
                """);

            await service.SaveLocalAppsAsync([]);

            var source = new AppCatalogSource
            {
                Id = sourceId,
                Name = "Test",
                Enabled = true,
                CachedListVersion = "1.0.0",
                AcknowledgedListVersion = "1.0.0",
            };

            CatalogCompareService.IgnoreChangesForCurrentVersion(source, "owner/app");
            await service.RefreshUpdateAvailableAsync(source);

            source.PendingReviewCount.Should().Be(0);
            source.UpdateAvailable.Should().BeFalse();
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task IgnoreRepositoryInMatchingSources_ignores_only_sources_containing_repository()
    {
        var matchingSourceId = Guid.NewGuid().ToString();
        var otherSourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(service.CatalogSourcesCacheFolder, $"{matchingSourceId}.json"),
                """
                {
                  "version": "1.0.0",
                  "apps": [
                    { "name": "App", "repository": "owner/app", "folderName": "AppFolder" }
                  ]
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(service.CatalogSourcesCacheFolder, $"{otherSourceId}.json"),
                """
                {
                  "version": "1.0.0",
                  "apps": [
                    { "name": "Other", "repository": "owner/other", "folderName": "OtherFolder" }
                  ]
                }
                """);

            var settings = new AppSettings
            {
                AppCatalogSources =
                [
                    new AppCatalogSource
                    {
                        Id = matchingSourceId,
                        Name = "Matching",
                        Enabled = true,
                        CachedListVersion = "1.0.0",
                        AcknowledgedListVersion = "1.0.0",
                    },
                    new AppCatalogSource
                    {
                        Id = otherSourceId,
                        Name = "Other",
                        Enabled = true,
                        CachedListVersion = "1.0.0",
                        AcknowledgedListVersion = "1.0.0",
                    },
                ],
            };

            await service.SaveLocalAppsAsync([]);

            await service.IgnoreRepositoryInMatchingSourcesAsync(settings, "owner/app");

            var matchingSource = settings.AppCatalogSources.Single(s => s.Id == matchingSourceId);
            var otherSource = settings.AppCatalogSources.Single(s => s.Id == otherSourceId);

            matchingSource.IgnoredChangesAtVersion.Should().ContainKey("owner/app");
            otherSource.IgnoredChangesAtVersion.Should().NotContainKey("owner/app");
            matchingSource.PendingReviewCount.Should().Be(0);

            await service.RefreshUpdateAvailableAsync(otherSource);
            otherSource.PendingReviewCount.Should().Be(1);
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task ApplyPendingCatalogChangeFlagsAsync_marks_changed_library_apps_only()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var cachePath = Path.Combine(service.CatalogSourcesCacheFolder, $"{sourceId}.json");
            await File.WriteAllTextAsync(cachePath, """
                {
                  "version": "2.0.0",
                  "apps": [
                    { "name": "Changed App", "repository": "owner/changed", "folderName": "Changed", "tags": ["n64"] },
                    { "name": "New App", "repository": "owner/new", "folderName": "New" }
                  ]
                }
                """);

            await service.SaveLocalAppsAsync([
                new GameInfo
                {
                    Name = "Old Name",
                    Repository = "owner/changed",
                    FolderName = "Changed",
                    Tags = [],
                },
                new GameInfo
                {
                    Name = "Unchanged",
                    Repository = "owner/same",
                    FolderName = "Same",
                },
            ]);

            var settings = new AppSettings
            {
                AppCatalogSources =
                [
                    new AppCatalogSource
                    {
                        Id = sourceId,
                        Name = "Test",
                        Enabled = true,
                        CachedListVersion = "2.0.0",
                        AcknowledgedListVersion = "1.0.0",
                    },
                ],
            };

            var libraryGames = new[]
            {
                new GameInfo { Name = "Old Name", Repository = "owner/changed", FolderName = "Changed" },
                new GameInfo { Name = "Unchanged", Repository = "owner/same", FolderName = "Same" },
            };

            await service.ApplyPendingCatalogChangeFlagsAsync(libraryGames, settings);

            libraryGames[0].HasPendingCatalogChanges.Should().BeTrue();
            libraryGames[1].HasPendingCatalogChanges.Should().BeFalse();
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task ApplyPendingCatalogChangeFlagsAsync_restores_flags_on_reloaded_game_instances()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var cachePath = Path.Combine(service.CatalogSourcesCacheFolder, $"{sourceId}.json");
            await File.WriteAllTextAsync(cachePath, """
                {
                  "version": "2.0.0",
                  "apps": [
                    { "name": "Changed App", "repository": "owner/changed", "folderName": "Changed", "tags": ["n64"] }
                  ]
                }
                """);

            await service.SaveLocalAppsAsync([
                new GameInfo
                {
                    Name = "Old Name",
                    Repository = "owner/changed",
                    FolderName = "Changed",
                    Tags = [],
                },
            ]);

            var settings = new AppSettings
            {
                AppCatalogSources =
                [
                    new AppCatalogSource
                    {
                        Id = sourceId,
                        Name = "Test",
                        Enabled = true,
                        CachedListVersion = "2.0.0",
                        AcknowledgedListVersion = "1.0.0",
                    },
                ],
            };

            var firstLoad = new[]
            {
                new GameInfo { Name = "Old Name", Repository = "owner/changed", FolderName = "Changed" },
            };
            await service.ApplyPendingCatalogChangeFlagsAsync(firstLoad, settings);
            firstLoad[0].HasPendingCatalogChanges.Should().BeTrue();

            // LoadGamesAsync builds new GameInfo instances; flags must be reapplied.
            var reloaded = new[]
            {
                new GameInfo { Name = "Old Name", Repository = "owner/changed", FolderName = "Changed" },
            };
            reloaded[0].HasPendingCatalogChanges.Should().BeFalse();

            await service.ApplyPendingCatalogChangeFlagsAsync(reloaded, settings);
            reloaded[0].HasPendingCatalogChanges.Should().BeTrue();
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }

    [Fact]
    public async Task FindPendingCatalogSourceIdAsync_returns_source_for_changed_app()
    {
        var sourceId = Guid.NewGuid().ToString();
        var (service, tempDir) = TestFixtures.CreateIsolatedCatalogService();

        try
        {
            var cachePath = Path.Combine(service.CatalogSourcesCacheFolder, $"{sourceId}.json");
            await File.WriteAllTextAsync(cachePath, """
                {
                  "version": "2.0.0",
                  "apps": [
                    { "name": "Changed App", "repository": "owner/changed", "folderName": "Changed", "tags": ["n64"] }
                  ]
                }
                """);

            await service.SaveLocalAppsAsync([
                new GameInfo
                {
                    Name = "Old Name",
                    Repository = "owner/changed",
                    FolderName = "Changed",
                    Tags = [],
                },
                new GameInfo
                {
                    Name = "Unchanged",
                    Repository = "owner/same",
                    FolderName = "Same",
                },
            ]);

            var settings = new AppSettings
            {
                AppCatalogSources =
                [
                    new AppCatalogSource
                    {
                        Id = sourceId,
                        Name = "Test",
                        Enabled = true,
                        CachedListVersion = "2.0.0",
                        AcknowledgedListVersion = "1.0.0",
                    },
                ],
            };

            var changed = new GameInfo { Name = "Old Name", Repository = "owner/changed", FolderName = "Changed" };
            var unchanged = new GameInfo { Name = "Unchanged", Repository = "owner/same", FolderName = "Same" };
            var missingRepo = new GameInfo { Name = "No Repo" };

            (await service.FindPendingCatalogSourceIdAsync(changed, settings)).Should().Be(sourceId);
            (await service.FindPendingCatalogSourceIdAsync(unchanged, settings)).Should().BeNull();
            (await service.FindPendingCatalogSourceIdAsync(missingRepo, settings)).Should().BeNull();
        }
        finally
        {
            TestFixtures.CleanupDirectory(tempDir);
        }
    }
}
