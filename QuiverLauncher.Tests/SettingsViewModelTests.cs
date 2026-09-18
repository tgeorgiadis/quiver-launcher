using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class SettingsViewModelTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; private set; } = new() { FirstStartup = false, AppsPath = "original" };
        public int Saves { get; private set; }
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { Current = settings; Saves++; }
    }

    [Fact]
    public async Task New_path_edit_cancels_older_debounce()
    {
        var store = new Store();
        var model = new SettingsViewModel(store);
        var applied = new List<string>();
        model.ConfigurePathChanges((_, _) => Task.FromResult(true), (path, _) =>
        {
            applied.Add(path);
            return Task.CompletedTask;
        });
        var old = model.ChangeAppsPathAsync("old", TestContext.Current.CancellationToken);
        var latest = model.ChangeAppsPathAsync("latest", TestContext.Current.CancellationToken, debounce: false);
        (await old).Should().BeFalse();
        (await latest).Should().BeTrue();
        applied.Should().Equal("latest");
        store.Current.AppsPath.Should().Be("latest");
        store.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Superseded_prompt_cannot_apply_its_path()
    {
        var store = new Store();
        var model = new SettingsViewModel(store);
        var prompt = new TaskCompletionSource<bool>();
        var applied = new List<string>();
        model.ConfigurePathChanges((path, _) => path == "old" ? prompt.Task : Task.FromResult(true), (path, _) =>
        {
            applied.Add(path);
            return Task.CompletedTask;
        });
        var old = model.ChangeAppsPathAsync("old", TestContext.Current.CancellationToken, debounce: false);
        var latest = model.ChangeAppsPathAsync("latest", TestContext.Current.CancellationToken, debounce: false);
        prompt.SetResult(true);
        (await old).Should().BeFalse();
        (await latest).Should().BeTrue();
        applied.Should().Equal("latest");
    }

    [Fact]
    public async Task Declined_path_keeps_saved_setting_and_cancelled_completion_does_not_save()
    {
        var store = new Store();
        var model = new SettingsViewModel(store);
        model.ConfigurePathChanges((_, _) => Task.FromResult(false), (_, _) => throw new Exception("Must not apply"));
        (await model.ChangeAppsPathAsync("declined", TestContext.Current.CancellationToken, false)).Should().BeFalse();
        store.Current.AppsPath.Should().Be("original");
        var pending = new TaskCompletionSource();
        model.ConfigurePathChanges((_, _) => Task.FromResult(true), (_, _) => pending.Task);
        var changing = model.ChangeAppsPathAsync("pending", TestContext.Current.CancellationToken, false);
        model.CancelPathChange();
        pending.SetResult();
        (await changing).Should().BeFalse();
        store.Saves.Should().Be(0);
    }

    [AvaloniaFact]
    public void Settings_bindings_initialize_without_saving_and_actions_persist_once()
    {
        var store = new Store();
        var model = new SettingsViewModel(store);
        var view = new SettingsView { DataContext = model };
        Dispatcher.UIThread.RunJobs();
        store.Saves.Should().Be(0);
        var check = view.FindControl<CheckBox>("CloseToTrayCheckBox")!;
        check.IsChecked.Should().Be(store.Current.CloseToTray);
        check.IsChecked = !store.Current.CloseToTray;
        Dispatcher.UIThread.RunJobs();
        store.Saves.Should().Be(1);
        model.CloseToTray.Should().Be(check.IsChecked!.Value);
        model.Refresh();
        Dispatcher.UIThread.RunJobs();
        store.Saves.Should().Be(1);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Scroll_speed_dropdown_navigation_selects_and_cancels_without_leaving_choices(bool nativeOpen)
    {
        var store = new Store();
        store.Current.MouseWheelScrollSpeed = 3;
        var model = new SettingsViewModel(store);
        var view = new SettingsView { DataContext = model };
        var window = new Window { Content = view, Width = 800, Height = 900 };
        var combo = view.FindControl<ComboBox>("MouseWheelScrollSpeedComboBox")!;
        var navigation = GamepadComboBoxNavigation.Instance;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            combo.SelectedIndex.Should().Be(2);
            if (nativeOpen) combo.IsDropDownOpen = true;
            else GamepadComboBoxNavigation.Open(combo);
            navigation.TryHandleNavigation(NavigationDirection.Down).Should().BeTrue();
            ((ComboBoxItem)combo.Items[3]!).Classes.Should().Contain("gamepad-focused");
            store.Saves.Should().Be(0);
            navigation.TryHandleCancel().Should().BeTrue();
            model.MouseWheelScrollSpeed.Should().Be(3);
            GamepadComboBoxNavigation.Open(combo);
            navigation.TryHandleNavigation(NavigationDirection.Down).Should().BeTrue();
            navigation.TryHandleConfirm().Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            combo.IsDropDownOpen.Should().BeFalse();
            model.MouseWheelScrollSpeed.Should().Be(5);
            store.Saves.Should().Be(1);
        }
        finally
        {
            combo.IsDropDownOpen = false;
            navigation.Close(combo);
            window.Close();
        }
    }
}
