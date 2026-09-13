using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class MetadataEditorTests
{
    [Fact]
    public async Task Reopening_editor_ignores_previous_save_completion_and_keeps_new_text()
    {
        var model = new MetadataEditorViewModel();
        var saving = new TaskCompletionSource();
        var calls = 0;
        model.Configure((_, _, _, _) => { calls++; return saving.Task; });
        model.Open(new GameInfo { Name = "First" }, MetadataEditMode.Tags);
        model.Text = "action, favourite";
        var first = model.SaveAsync(TestContext.Current.CancellationToken);
        (await model.SaveAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        calls.Should().Be(1);
        model.Close();
        model.Open(new GameInfo { Name = "Second" }, MetadataEditMode.CustomDisplayName);
        model.Text = "My title";
        saving.SetResult();
        (await first).Should().BeFalse();
        model.Text.Should().Be("My title");
        model.Title.Should().Be("Custom Display Name");
        model.CanSave.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Editor_binds_mode_and_text_and_disables_save_until_opened()
    {
        var view = new MetadataEditorView();
        view.FindControl<Button>("TagEditSaveButton")!.IsEnabled.Should().BeFalse();
        view.Model.Open(new GameInfo { Name = "App", CustomDisplayName = "My app" }, MetadataEditMode.CustomDisplayName);
        Dispatcher.UIThread.RunJobs();
        view.FindControl<TextBlock>("TagEditTitleText")!.Text.Should().Be("Custom Display Name");
        var input = view.FindControl<TextBox>("TagEditTextBox")!;
        input.Text.Should().Be("My app");
        input.Text = "Edited";
        view.Model.Text.Should().Be("Edited");
        view.FindControl<Button>("TagEditSaveButton")!.IsEnabled.Should().BeTrue();
    }

    private sealed class NavigationHost : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new();
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.Library;
        public bool IsFocusActive => true;
        public bool ApplyTransition(GamepadZoneTransition transition) => false;
        public void ClearFocus() { }
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }

    [AvaloniaFact]
    public void Form_navigation_keeps_action_row_horizontal_and_restores_selection()
    {
        var input = new TextBox();
        var cancel = new Button { Content = "Cancel" };
        var save = new Button { Content = "Save" };
        var root = new StackPanel
        {
            Children = { input, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { cancel, save } } },
        };
        var window = new Window { Width = 400, Height = 240, Content = root };
        var closed = false;
        var navigation = new FeatureFormNavigation(root, () => [input, cancel, save], new NavigationHost(),
            GamepadNavigationZone.TagEditOverlay, () => closed = true);
        try
        {
            window.Show();
            window.UpdateLayout();
            navigation.ApplySelection(1);
            navigation.Navigate(QuiverLauncher.Services.NavigationDirection.Right);
            navigation.FocusIndex.Should().Be(2);
            navigation.Navigate(QuiverLauncher.Services.NavigationDirection.Down);
            navigation.FocusIndex.Should().Be(2);
            navigation.ClearFocus();
            navigation.RestoreFocus();
            save.Classes.Should().Contain("gamepad-focused");
            navigation.Cancel();
            closed.Should().BeTrue();
        }
        finally { window.Close(); GamepadTextInput.Reset(); }
    }
}
