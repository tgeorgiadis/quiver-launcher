using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AcceptCatalogUpdateTests
{
    [Fact]
    public void MigrateLegacyCatalogSources_removes_accepted_snapshot_and_sets_versions()
    {
        var sourceId = Guid.NewGuid().ToString();
        var cacheFolder = Path.Combine(AppContext.BaseDirectory, "Cache", "CatalogSources");
        Directory.CreateDirectory(cacheFolder);

        var cachePath = Path.Combine(cacheFolder, $"{sourceId}.json");
        File.WriteAllText(cachePath, """
            {
              "version": "3.0.0",
              "apps": [
                { "name": "App", "repository": "owner/app", "folderName": "AppFolder" }
              ]
            }
            """);

        var acceptedPath = Path.Combine(cacheFolder, $"{sourceId}.accepted.json");
        File.WriteAllText(acceptedPath, """{"apps":[]}""");

        var settings = new AppSettings();
        settings.EnsureInitialized();
        settings.AppCatalogSources.Add(new AppCatalogSource { Id = sourceId, Name = "Legacy" });

        var changed = AppCatalogService.MigrateLegacyCatalogSources(settings, cacheFolder);

        changed.Should().BeTrue();
        File.Exists(acceptedPath).Should().BeFalse();
        settings.AppCatalogSources[0].CachedListVersion.Should().Be("3.0.0");
        settings.AppCatalogSources[0].AcknowledgedListVersion.Should().BeNull();
        settings.AppCatalogSources[0].UpdateAvailable.Should().BeTrue();
    }

    [Fact]
    public void AcknowledgeSourceVersion_clears_update_flag()
    {
        var source = new AppCatalogSource
        {
            CachedListVersion = "2.0.0",
            AcknowledgedListVersion = "1.0.0",
            UpdateAvailable = true,
        };

        new AppCatalogService().AcknowledgeSourceVersion(source);

        source.AcknowledgedListVersion.Should().Be("2.0.0");
        source.UpdateAvailable.Should().BeFalse();
    }
}
