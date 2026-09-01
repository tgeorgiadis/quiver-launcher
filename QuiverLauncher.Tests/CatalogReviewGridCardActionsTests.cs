using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogReviewGridCardActionsTests
{
    [Fact]
    public void Two_actions_show_details_and_remove_without_more()
    {
        var layout = CatalogReviewGridCardActions.ForDesktop(
            canAdd: false,
            canMerge: false,
            showHide: false,
            showUnhide: false,
            showRemove: true,
            identityKey: "up-to-date");

        layout.ShowMore.Should().BeFalse();
        layout.ColumnCount.Should().Be(2);
        layout.Inline.Should().Equal(
            CatalogReviewGridCardActions.Action.Details,
            CatalogReviewGridCardActions.Action.Remove);
        layout.Menu.Should().BeEmpty();
        layout.Chrome.Select(c => c.Kind).Should().Equal(
            CatalogReviewGridCardActions.ChromeKind.Details,
            CatalogReviewGridCardActions.ChromeKind.Remove);
    }

    [Fact]
    public void Three_actions_show_merge_details_remove_without_more()
    {
        var layout = CatalogReviewGridCardActions.ForDesktop(
            canAdd: false,
            canMerge: true,
            showHide: false,
            showUnhide: false,
            showRemove: true);

        layout.ShowMore.Should().BeFalse();
        layout.ColumnCount.Should().Be(3);
        layout.Inline.Should().Equal(
            CatalogReviewGridCardActions.Action.Merge,
            CatalogReviewGridCardActions.Action.Details,
            CatalogReviewGridCardActions.Action.Remove);
        layout.IsInline(CatalogReviewGridCardActions.Action.Merge).Should().BeTrue();
        layout.IsMenu(CatalogReviewGridCardActions.Action.Remove).Should().BeFalse();
    }

    [Fact]
    public void Four_actions_keep_first_two_inline_and_fold_the_rest_into_more()
    {
        var layout = CatalogReviewGridCardActions.ForDesktop(
            canAdd: true,
            canMerge: true,
            showHide: true,
            showUnhide: false,
            showRemove: true,
            identityKey: "overflow");

        layout.ShowMore.Should().BeTrue();
        layout.ColumnCount.Should().Be(3);
        layout.Inline.Should().Equal(
            CatalogReviewGridCardActions.Action.Add,
            CatalogReviewGridCardActions.Action.Merge);
        layout.Menu.Should().Equal(
            CatalogReviewGridCardActions.Action.Details,
            CatalogReviewGridCardActions.Action.Hide,
            CatalogReviewGridCardActions.Action.Remove);
        layout.Chrome.Select(c => c.Kind).Should().Equal(
            CatalogReviewGridCardActions.ChromeKind.Add,
            CatalogReviewGridCardActions.ChromeKind.Merge,
            CatalogReviewGridCardActions.ChromeKind.More);
        layout.Chrome.Should().OnlyContain(c => c.IdentityKey == "overflow");
        var more = layout.Chrome.Last();
        more.MenuDetails.Should().BeTrue();
        more.MenuHide.Should().BeTrue();
        more.MenuRemove.Should().BeTrue();
        more.MenuAdd.Should().BeFalse();
        more.MenuMerge.Should().BeFalse();
        layout.IsMenu(CatalogReviewGridCardActions.Action.Details).Should().BeTrue();
        layout.IsInline(CatalogReviewGridCardActions.Action.Details).Should().BeFalse();
    }

    [Fact]
    public void New_app_shows_add_details_hide()
    {
        var layout = CatalogReviewGridCardActions.ForDesktop(
            canAdd: true,
            canMerge: false,
            showHide: true,
            showUnhide: false,
            showRemove: false);

        layout.ShowMore.Should().BeFalse();
        layout.Inline.Should().Equal(
            CatalogReviewGridCardActions.Action.Add,
            CatalogReviewGridCardActions.Action.Details,
            CatalogReviewGridCardActions.Action.Hide);
    }
}
