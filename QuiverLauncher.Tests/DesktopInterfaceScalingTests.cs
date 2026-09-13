using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class DesktopInterfaceScalingTests
{
    [Theory]
    [InlineData(225, 1920, 1080, 200)]
    [InlineData(225, 3840, 2160, 225)]
    [InlineData(225, 1280, 720, 125)]
    [InlineData(125, 1920, 1080, 125)]
    [InlineData(225, 750, 490, 100)]
    [InlineData(175, 600, 400, 100)]
    [InlineData(123, 3840, 2160, 100)]
    public void Scale_fits_both_dimensions_without_exceeding_requested(int requested, double width, double height, int expected)
        => InterfaceScale.Fit(requested, new Size(width, height)).Should().Be(expected);

    [Fact]
    public void Loading_old_or_invalid_settings_does_not_save_and_selection_round_trips()
    {
        foreach (var json in new[] { "{}", "{\"InterfaceScalePercent\":0}", "{\"InterfaceScalePercent\":126}" })
        {
            var store = new Store(JsonSerializer.Deserialize<AppSettings>(json)!);
            store.Current.EnsureInitialized();
            var model = new SettingsViewModel(store);
            model.InterfaceScalePercent.Should().Be(100);
            store.Saves.Should().Be(0);
            var gridSize = store.Current.SlotSize;
            var rowHeight = store.Current.ListRowHeight;
            model.InterfaceScaleIndex = 5;
            store.Saves.Should().Be(1);
            JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(store.Current))!.InterfaceScalePercent.Should().Be(225);
            store.Current.SlotSize.Should().Be(gridSize);
            store.Current.ListRowHeight.Should().Be(rowHeight);
        }
    }

    [AvaloniaFact]
    public void Maximized_and_fullscreen_state_survives_scaling_changes()
    {
        var model = new SettingsViewModel(new Store(new()));
        var window = new Window { Width = 1280, Height = 720, Content = new Border() };
        using var scaling = new DesktopInterfaceScaling(window, model, () => new Size(3840, 2160));
        try
        {
            window.Show();
            foreach (var state in new[] { WindowState.Maximized, WindowState.FullScreen })
            {
                window.WindowState = state;
                model.InterfaceScalePercent = state == WindowState.Maximized ? 150 : 225;
                Settle(window);
                window.WindowState.Should().Be(state);
                scaling.AppliedPercent.Should().Be(model.InterfaceScalePercent);
            }
        }
        finally { window.Close(); }
    }

    [Fact]
    public void File_store_load_is_read_only_and_persists_the_requested_scale()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quiver-scale-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"FirstStartup\":false,\"InterfaceScalePercent\":999}");
            var original = File.ReadAllText(path);
            var store = new FileSettingsStore(path);
            store.Current.InterfaceScalePercent.Should().Be(100);
            File.ReadAllText(path).Should().Be(original);
            new SettingsViewModel(store).InterfaceScalePercent = 175;
            new FileSettingsStore(path).Current.InterfaceScalePercent.Should().Be(175);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void Display_changes_restore_requested_scale_and_disposal_ignores_queued_updates()
    {
        var store = new Store(new() { InterfaceScalePercent = 225 });
        var model = new SettingsViewModel(store);
        var child = new TextBox { Text = "dummy text" };
        var window = new Window { Width = 800, Height = 600, Content = child };
        var available = new Size(1280, 720);
        using var controller = new DesktopInterfaceScaling(window, model, () => available);
        try
        {
            window.Show(); Settle(window);
            controller.AppliedPercent.Should().Be(125);
            model.InterfaceScaleNotice.Should().Be("Using 125% to fit this display. Selected: 225%.");
            window.MinWidth.Should().Be(937.5);
            child.Focus(); child.SelectionStart = 1; child.SelectionEnd = 5;
            available = new Size(3840, 2160);
            controller.Refresh(); Settle(window);
            controller.AppliedPercent.Should().Be(225);
            model.ShowInterfaceScaleNotice.Should().BeFalse();
            child.IsFocused.Should().BeTrue(); child.SelectedText.Should().Be("ummy");
            store.Saves.Should().Be(0);
            var enlargedWidth = window.Width;
            model.InterfaceScalePercent = 100; Settle(window);
            window.Width.Should().Be(enlargedWidth);
            model.InterfaceScalePercent = 150;
            controller.Dispose(); controller.Dispose(); Settle(window);
            controller.AppliedPercent.Should().Be(100);
            using var replacement = new DesktopInterfaceScaling(window, model, () => available);
            Settle(window);
            ((LayoutTransformControl)window.Content!).Child.Should().BeSameAs(child);
            replacement.AppliedPercent.Should().Be(150);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(100)] [InlineData(125)] [InlineData(150)]
    [InlineData(175)] [InlineData(200)] [InlineData(225)]
    public async Task Real_shell_reflows_and_preserves_search_editing_at_each_scale(int percent)
    {
        var store = new Store(new() { FirstStartup = false, AppsPath = Path.Combine(Path.GetTempPath(), "quiver-scale", Guid.NewGuid().ToString("N")) });
        var view = new MainView(new() { SettingsStore = store, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 750, Height = 490 };
        using var scaling = new DesktopInterfaceScaling(window, view.SettingsModel, () => new Size(3840, 2160));
        try
        {
            window.Show(); Settle(window);
            var toolbar = view.FindControl<LibraryToolbarView>("LibraryToolbar")!;
            var search = toolbar.FindControl<TextBox>("LibrarySearchTextBox")!;
            search.Text = "dummy query";
            toolbar.SelectSort("NameDesc");
            GamepadTextInput.BeginEdit(search);
            search.SelectionStart = 2; search.SelectionEnd = 7;
            view.SettingsModel.InterfaceScalePercent = percent; Settle(window);
            scaling.AppliedPercent.Should().Be(percent);
            search.IsFocused.Should().BeTrue();
            GamepadTextInput.IsEditing.Should().BeTrue();
            search.SelectedText.Should().Be("mmy q");
            view.Library.SortBy.Should().Be("NameDesc");
            view.Bounds.Width.Should().BeApproximately(750, 1);
            foreach (var name in new[] { "MinimizeButton", "ToggleMaximizeButton", "CloseLauncherButton" })
            {
                var button = view.FindControl<Button>(name)!;
                button.IsEffectivelyVisible.Should().BeTrue();
                AssertInside(button, window);
            }
            AssertInside(search, window);
            window.KeyTextInput("test");
            search.Text.Should().Be("dutestuery");
            GamepadTextInput.Reset();
            foreach (var mode in new[] { "sources", "review", "updates", "mods" })
            {
                view.Shell.Mode = mode is "sources" or "review" ? MainViewMode.AppCatalog : MainViewMode.Library;
                view.Shell.CatalogSubView = mode == "review" ? AppCatalogSubView.Review : AppCatalogSubView.Sources;
                view.Shell.AppUpdatesOpen = mode == "updates";
                view.Shell.ModsOpen = mode == "mods";
                ((IModsFeatureHost)view).RefreshShell();
                Settle(window);
                foreach (var name in new[] { "MinimizeButton", "ToggleMaximizeButton", "CloseLauncherButton", "SettingsButton" })
                    AssertInside(view.FindControl<Button>(name)!, window);
            }
        }
        finally { GamepadTextInput.Reset(); window.Close(); await view.ShutdownAsync(); }
    }

    [AvaloniaFact]
    public void Popups_and_nested_submenus_inherit_scale_once_and_dialogs_are_constrained()
    {
        var model = new SettingsViewModel(new Store(new() { InterfaceScalePercent = 200 }));
        var button = new Button { Content = "Open", Width = 100, Height = 40 };
        var window = new Window { Content = button, Width = 1500, Height = 1000 };
        using var scaling = new DesktopInterfaceScaling(window, model, () => new Size(1920, 1080));
        var nestedChild = new MenuItem { Header = "Run" };
        var popupChild = new MenuItem { Header = "Launch options", Items = { nestedChild } };
        var popup = new ContextMenu { Items = { popupChild } };
        var dialog = new Window { Width = 1200, Height = 900, Content = new Button { Content = "Dialog action" } };
        try
        {
            window.Show(); Settle(window);
            popup.Open(button); Settle(window);
            popupChild.IsSubMenuOpen = true; Settle(window);
            Settle(TopLevel.GetTopLevel(popupChild)!);
            Settle(TopLevel.GetTopLevel(nestedChild)!);
            popupChild.TransformToVisual(TopLevel.GetTopLevel(popupChild)!)!.Value.M11.Should().Be(2);
            nestedChild.TransformToVisual(TopLevel.GetTopLevel(nestedChild)!)!.Value.M11.Should().Be(2);
            DesktopInterfaceScaling.PrepareDialog(dialog, window);
            DesktopInterfaceScaling.PrepareDialog(dialog, window);
            dialog.Width.Should().Be(1920); dialog.Height.Should().Be(1080);
            var content = (LayoutTransformControl)dialog.Content!;
            content.Child.Should().BeOfType<ScrollViewer>();
            ((ScaleTransform)content.LayoutTransform!).ScaleX.Should().Be(2);
            model.InterfaceScalePercent = 125; Settle(window);
            ((ScaleTransform)content.LayoutTransform!).ScaleX.Should().Be(1.25);
            popupChild.TransformToVisual(TopLevel.GetTopLevel(popupChild)!)!.Value.M11.Should().Be(1.25);
        }
        finally { popup.Close(); dialog.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Appearance_scale_is_reachable_and_controller_confirm_applies_the_selection()
    {
        var store = new Store(new() { FirstStartup = false });
        var view = new MainView(new() { SettingsStore = store, EnableInput = false, EnableMusic = false, InitializeOnOpen = false });
        var window = new Window { Content = view, Width = 1280, Height = 900 };
        using var scaling = new DesktopInterfaceScaling(window, view.SettingsModel, () => new Size(3840, 2160));
        try
        {
            window.Show(); Settle(window);
            view.OpenGitHubApiTokenSettings(); Settle(window);
            var settings = view.FindControl<SettingsView>("SettingsPanel")!;
            settings.FindControl<TabControl>("SettingsTabControl")!.SelectedIndex = 2;
            Settle(window);
            var combo = settings.FindControl<ComboBox>("InterfaceScaleComboBox")!;
            var controls = settings.Navigation.CollectSettingsFocusableControls();
            controls.Should().Contain(combo);
            settings.Navigation.ApplySettingsGamepadSelection(controls.IndexOf(combo));
            settings.Navigation.Confirm(); Settle(window);
            combo.IsDropDownOpen.Should().BeTrue();
            GamepadComboBoxNavigation.Instance.TryHandleNavigation(Services.NavigationDirection.Down).Should().BeTrue();
            GamepadComboBoxNavigation.Instance.TryHandleConfirm().Should().BeTrue();
            Settle(window);
            view.SettingsModel.InterfaceScalePercent.Should().Be(125);
            scaling.AppliedPercent.Should().Be(125);
            combo.IsDropDownOpen.Should().BeFalse();
            combo.IsFocused.Should().BeTrue();
            AssertInside(combo, window);
            settings.Navigation.Confirm(); Settle(window);
            GamepadComboBoxNavigation.Instance.TryHandleNavigation(Services.NavigationDirection.Down);
            GamepadComboBoxNavigation.Instance.TryHandleCancel().Should().BeTrue();
            Settle(window);
            view.SettingsModel.InterfaceScalePercent.Should().Be(125);
        }
        finally { GamepadTextInput.Reset(); window.Close(); await view.ShutdownAsync(); }
    }

    private static void AssertInside(Control control, Control root)
    {
        var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(root)!.Value);
        bounds.Left.Should().BeGreaterThanOrEqualTo(-1);
        bounds.Right.Should().BeLessThanOrEqualTo(root.Bounds.Width + 1);
        bounds.Bottom.Should().BeLessThanOrEqualTo(root.Bounds.Height + 1);
    }
    private static void Settle(TopLevel window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private sealed class Store(AppSettings settings) : ISettingsStore
    {
        public AppSettings Current { get; } = settings;
        public int Saves { get; private set; }
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) => Saves++;
    }
}
