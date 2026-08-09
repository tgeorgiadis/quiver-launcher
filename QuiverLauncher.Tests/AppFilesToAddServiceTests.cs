using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AppFilesToAddServiceTests
{
    [Fact]
    public void Normalize_rejects_path_segments_and_deduplicates()
    {
        var files = AppFilesToAddService.Normalize(
        [
            " portable.txt ",
            "portable.txt",
            "../evil.txt",
            "sub/dir.txt",
            "config.ini",
            "",
        ]);

        files.Should().Equal("portable.txt", "config.ini");
    }

    [Fact]
    public void ParseCommaSeparated_parses_display_format()
    {
        AppFilesToAddService.ParseCommaSeparated("portable.txt, config.ini")
            .Should()
            .Equal("portable.txt", "config.ini");
    }

    [Fact]
    public void Sync_creates_new_files_and_removes_cleared_ones()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-files-to-add-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            AppFilesToAddService.Sync(root, previous: null, next: ["portable.txt", "keep.txt"]);
            File.Exists(Path.Combine(root, "portable.txt")).Should().BeTrue();
            File.Exists(Path.Combine(root, "keep.txt")).Should().BeTrue();

            File.WriteAllText(Path.Combine(root, "portable.txt"), "existing");
            AppFilesToAddService.Sync(root, previous: ["portable.txt", "keep.txt"], next: ["keep.txt", "new.txt"]);

            File.Exists(Path.Combine(root, "portable.txt")).Should().BeFalse();
            File.Exists(Path.Combine(root, "keep.txt")).Should().BeTrue();
            File.ReadAllText(Path.Combine(root, "keep.txt")).Should().BeEmpty();
            File.Exists(Path.Combine(root, "new.txt")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Sync_does_not_overwrite_existing_file_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-files-to-add-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "portable.txt");
            File.WriteAllText(path, "keep-me");

            AppFilesToAddService.Sync(root, previous: null, next: ["portable.txt"]);

            File.ReadAllText(path).Should().Be("keep-me");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Catalog_compare_detects_filesToAdd_changes_and_copies_on_replace()
    {
        var local = new GameInfo
        {
            Name = "App",
            Repository = "owner/repo",
            FolderName = "App",
            FilesToAdd = [],
        };
        var external = new GameInfo
        {
            Name = "App",
            Repository = "owner/repo",
            FolderName = "App",
            FilesToAdd = ["portable.txt"],
        };

        AppCatalogService.AreCatalogFieldsEquivalent(local, external).Should().BeFalse();
        CatalogCompareService.GetChangedFields(local, external).Should().Contain("filesToAdd");

        var replaced = CatalogCompareService.ReplaceFromExternal(local, external);
        replaced.FilesToAdd.Should().Equal("portable.txt");
    }

    [Fact]
    public void SerializeApp_omits_empty_filesToAdd()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "quiver-files-to-add-ser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var (service, _) = TestFixtures.CreateIsolatedCatalogService(dataDirectory: tempDir);
            var withoutFiles = new List<GameInfo>
            {
                new()
                {
                    Name = "Plain",
                    Repository = "owner/plain",
                    FolderName = "Plain",
                    FilesToAdd = [],
                },
            };
            service.SaveLocalApps(withoutFiles);
            var plainJson = File.ReadAllText(Path.Combine(tempDir, "apps.json"));
            plainJson.Should().NotContain("filesToAdd");

            var withFiles = new List<GameInfo>
            {
                new()
                {
                    Name = "Recomp",
                    Repository = "owner/recomp",
                    FolderName = "Recomp",
                    FilesToAdd = ["portable.txt"],
                },
            };
            service.SaveLocalApps(withFiles);
            var withJson = File.ReadAllText(Path.Combine(tempDir, "apps.json"));
            withJson.Should().Contain("filesToAdd");
            withJson.Should().Contain("portable.txt");

            var loaded = service.ParseAppsFromJson(withJson);
            loaded.Single().FilesToAdd.Should().Equal("portable.txt");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
