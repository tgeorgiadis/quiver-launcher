using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class CatalogReviewFocusCleanupTests
{
    private sealed class Host : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new();
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.CatalogReviewList;
        public bool IsFocusActive => true;
        public bool ApplyTransition(GamepadZoneTransition transition) => false;
        public void ClearFocus() { }
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }

    [AvaloniaFact]
    public void Cleanup_removes_old_row_highlights_even_after_selected_row_disappears()
    {
        var buttons = Enumerable.Range(0, 4).Select(_ => new Button { Content = "Add" }).ToList();
        foreach (var button in buttons) { button.Classes.Add("options"); button.Classes.Add("gamepad-focused"); }
        buttons[0].IsVisible = false; // A hidden control may be reused later.
        var panel = new StackPanel();
        foreach (var button in buttons) panel.Children.Add(button);
        var view = new CatalogReviewView { Content = panel };
        var window = new Window { Content = view };
        var host = new Host();
        var navigation = new CatalogReviewNavigation(view, host);
        try
        {
            window.Show();
            buttons[1].Focus();
            host.Navigation.CatalogReviewSelectedIndex = 3;
            host.Navigation.CatalogReviewRowActionIndex = 0;
            // No rows remain, but controls can still carry synthetic focus styling.
            navigation.ClearCatalogReviewRowActionsGamepadFocus();
            buttons.Should().OnlyContain(b => !b.Classes.Contains("gamepad-focused"));
            buttons[1].IsFocused.Should().BeFalse();
            host.Navigation.CatalogReviewRowActionIndex.Should().Be(-1);

            foreach (var button in buttons) button.Classes.Add("gamepad-focused");
            view.ReplaceCatalogSyncRows([new CatalogSyncRowItem { IdentityKey = "next" }]);
            buttons.Should().OnlyContain(b => !b.Classes.Contains("gamepad-focused"));
        }
        finally { window.Close(); }
    }
}
