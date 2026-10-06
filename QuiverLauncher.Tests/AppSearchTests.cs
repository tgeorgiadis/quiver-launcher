using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class AppSearchTests
{
    [Fact]
    public void Matches_empty_query_matches_all()
    {
        var app = new GameInfo { Name = "Ocarina of Time", FolderName = "OoT" };

        AppSearch.Matches(app, null).Should().BeTrue();
        AppSearch.Matches(app, "").Should().BeTrue();
        AppSearch.Matches(app, "   ").Should().BeTrue();
        AppSearch.Matches(null, "ocarina").Should().BeFalse();
        AppSearch.Matches(null, "").Should().BeTrue();
    }

    [Fact]
    public void Matches_name_repository_tags_and_display_name()
    {
        var app = new GameInfo
        {
            Name = "Ocarina of Time",
            Project = "Zelda64Recomp",
            CustomDisplayName = "OoT Recomp",
            FolderName = "OcarinaOfTime-Zelda64Recomp",
            Repository = "Zelda64Recomp/Zelda64Recomp",
            Tags = ["n64", "recomp", "zelda"],
        };

        AppSearch.Matches(app, "ocarina").Should().BeTrue();
        AppSearch.Matches(app, "oot").Should().BeTrue();
        AppSearch.Matches(app, "zelda64recomp/zelda64recomp").Should().BeTrue();
        AppSearch.Matches(app, "recomp").Should().BeTrue();
        AppSearch.Matches(app, "mario").Should().BeFalse();
    }

    [Fact]
    public void Matches_requires_every_token()
    {
        var app = new GameInfo
        {
            Name = "Ocarina of Time",
            Tags = ["recomp", "n64"],
        };

        AppSearch.Matches(app, "zelda recomp").Should().BeFalse();
        AppSearch.Matches(app, "ocarina recomp").Should().BeTrue();
    }
}
