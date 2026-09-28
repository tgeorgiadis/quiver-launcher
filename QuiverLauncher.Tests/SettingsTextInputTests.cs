using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class SettingsTextInputTests
{
    [AvaloniaFact]
    public async Task Hosted_entry_save_reveals_persisted_app_without_network_or_install_folder()
    {
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "quiver-entry-save-" + Guid.NewGuid().ToString("N"));
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var store = new Store();
        store.Current.AppsPath = Path.Combine(root, "Apps");
        using var handler = new EntryNetworkProbe();
        using var http = new HttpClient(handler);
        using var manager = new GameManager(store, http);
        var view = new MainView(new() { SettingsStore = store, GameManager = manager,
            InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        try
        {
            var editor = view.FindControl<AppEntryEditorView>("EntryFormOverlay")!;
            editor.Model.Open();
            editor.Model.Name = "jakdexter";
            editor.Model.FolderName = "jakanddexter";
            editor.Model.Repository = "https://github.com/open-goal/launcher";
            var created = new TaskCompletionSource<string>();
            editor.EntryCreated += folder => created.TrySetResult(folder);
            editor.CreateNewEntry_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
            (await created.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("jakanddexter");
            manager.LibraryApps.Should().ContainSingle(app => app.FolderName == "jakanddexter");
            (await manager.CatalogService.LoadLocalAppsAsync()).Should().ContainSingle(app => app.FolderName == "jakanddexter");
            Directory.Exists(Path.Combine(store.Current.AppsPath, "jakanddexter")).Should().BeFalse();
            handler.Calls.Should().Be(0);
        }
        finally
        {
            await view.ShutdownAsync();
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            TestFixtures.CleanupDirectory(root);
        }
    }

    private sealed class EntryNetworkProbe : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }

    [AvaloniaFact]
    public async Task Manual_add_waits_for_notice_dismissal_before_revealing_library_card()
    {
        var main = CreateView(new Store());
        await using var session = new LauncherSession();
        var editor = new AppEntryEditorView();
        var dismissed = new TaskCompletionSource();
        var revealed = new TaskCompletionSource<string>();
        var reloaded = false;
        editor.Configure(session, main, new NotifyingEntrySave(),
            () => { reloaded = true; return Task.CompletedTask; }, (_, _) => dismissed.Task, _ => { }, () => { });
        editor.EntryCreated += folder => revealed.TrySetResult(folder);
        editor.Model.Open();
        editor.Model.Name = "Example";
        editor.Model.FolderName = "Example";
        editor.Model.Repository = "https://github.com/fixture/example";
        try
        {
            editor.CreateNewEntry_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            reloaded.Should().BeFalse();
            revealed.Task.IsCompleted.Should().BeFalse();
            dismissed.SetResult();
            (await revealed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("Example");
            reloaded.Should().BeTrue();
        }
        finally { dismissed.TrySetResult(); await main.ShutdownAsync(); }
    }

    private sealed class NotifyingEntrySave : IAppEntryService
    {
        public Task<bool> SaveAsync(QuiverLauncher.ViewModels.AppEntryDraft draft,
            Action<QuiverLauncher.ViewModels.EntryNotice> notice, Action<QuiverLauncher.Models.GameInfo> openFolder,
            CancellationToken token)
        {
            notice(new("Place your app files in the folder.", "App Added"));
            return Task.FromResult(true);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manual_add_reveals_the_saved_card_with_input_appropriate_focus(bool keyboard)
    {
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        QuiverLauncherPaths.OverrideUserDataRoot = Path.Combine(Path.GetTempPath(), "quiver-manual-focus", Guid.NewGuid().ToString("N"));
        var store = new Store();
        store.Current.UseGridView = false;
        using var manager = new GameManager(store);
        var view = new MainView(new() { SettingsStore = store, GameManager = manager,
            InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1200, Height = 600 };
        try
        {
            window.Show();
            await manager.CatalogService.SaveLocalAppsAsync(Enumerable.Range(0, 40)
                .Select(i => new QuiverLauncher.Models.GameInfo { Name = $"App {i:00}", FolderName = $"app{i}" }).ToList());
            await manager.ReloadLibraryFromDiskAsync(allowNetwork: false);
            var added = manager.Games.Last();
            GamepadFocusChrome.SetKeyboardNavigationActive(keyboard);
            view.RevealManuallyAddedApp(added.FolderName);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            added.IsGamepadFocused.Should().Be(keyboard);
            manager.Games.Where(g => g != added).Should().OnlyContain(g => !g.IsGamepadFocused);
            var card = view.FindControl<LibraryView>("LibraryPanel")!.FindGameCardRoot(added);
            card.Should().NotBeNull();
            var position = card!.TranslatePoint(default, window)!.Value;
            position.Y.Should().BeLessThan(window.Bounds.Height);
            (position.Y + card.Bounds.Height).Should().BeGreaterThan(0);
        }
        finally
        {
            window.Close(); await view.ShutdownAsync();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
        }
    }

    [AvaloniaTheory]
    [InlineData("github", false, false, true)]
    [InlineData("github", true, false, false)]
    [InlineData("gitlab", false, false, true)]
    [InlineData("github", false, true, false)]
    public async Task Actual_anonymous_github_limit_shows_dismissible_token_banner(string provider, bool authenticated, bool dismissed, bool expected)
    {
        var store = new Store();
        store.Current.GitHubTokenBannerPermanentlyDismissed = dismissed;
        using var http = new HttpClient(new RateLimitedResponse());
        using var manager = new GameManager(store, http);
        var view = new MainView(new() { SettingsStore = store, GameManager = manager,
            InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        try
        {
            window.Show();
            var banners = view.FindControl<LauncherBannerView>("Banners")!;
            banners.ApplyTopBanner();
            var banner = banners.FindControl<Border>("GitHubTokenBanner")!;
            banner.IsVisible.Should().BeFalse("missing credentials alone should not interrupt browsing");
            await QuiverLauncher.Core.Services.ReleaseRequestCoordinator.For(http).FetchAsync(http,
                new Uri($"https://{(provider == "github" ? "api.github.com" : "gitlab.com")}/fixture"),
                provider, authenticated ? "synthetic-test-credential" : null, _ => []);
            Dispatcher.UIThread.RunJobs();
            banner.IsVisible.Should().Be(expected);
            if (expected)
            {
                banners.FindControl<TextBlock>("GitHubTokenBannerText")!.Text.Should().Contain("retry after");
                if (provider == "github") store.Current.GitHubApiToken = "synthetic-saved-credential";
                else store.Current.GitLabApiToken = "synthetic-saved-credential";
                banners.ApplyTopBanner();
                banner.IsVisible.Should().BeFalse();
            }
        }
        finally { window.Close(); await view.ShutdownAsync(); }
    }

    private sealed class RateLimitedResponse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
                { Content = new StringContent("{\"message\":\"API rate limit exceeded\"}") };
            response.Headers.TryAddWithoutValidation("Retry-After", "60");
            return Task.FromResult(response);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Token_draft_is_saved_only_by_explicit_keyboard_or_gamepad_action(bool gamepad)
    {
        var store = new Store();
        var view = CreateView(store);
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        try
        {
            window.Show(); view.OpenGitHubApiTokenSettings();
            Dispatcher.UIThread.RunJobs();
            var settings = view.FindControl<SettingsView>("SettingsPanel")!;
            var changed = 0;
            settings.Model.CredentialsChanged += _ => changed++;
            settings.FindControl<TextBox>("GitHubTokenTextBox")!.Text = " synthetic-context ";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", store.Current.GitHubApiToken);
            Assert.Equal(0, changed);
            GamepadTextInput.TryEndEdit();
            var button = settings.FindControl<Button>("SaveGitHubTokenButton")!;
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            if (gamepad)
            {
                settings.Navigation.ApplySettingsGamepadSelection(settings.Navigation.CollectSettingsFocusableControls().IndexOf(button));
                settings.Navigation.Confirm();
            }
            else
            {
                button.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("synthetic-context", store.Current.GitHubApiToken);
            Assert.Equal(1, changed);
            Assert.True(view.Shell.SettingsOpen);
        }
        finally { window.Close(); await view.ShutdownAsync(); GamepadTextInput.Reset(); GamepadFocusChrome.SetKeyboardNavigationActive(false); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Banner_token_shortcut_moves_navigation_into_the_editing_field(bool gamepad)
    {
        var view = CreateView(new Store());
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            var banner = view.FindControl<LauncherBannerView>("Banners")!;
            banner.FindControl<Border>("GitHubTokenBanner")!.IsVisible = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var button = banner.FindControl<Button>("GitHubTokenBannerSettingsButton")!;
            if (gamepad)
            {
                banner.ApplyAnnouncementBannerGamepadSelection(0);
                banner.ActivateTopBannerGamepadSelection();
            }
            else
            {
                button.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            view.Shell.SettingsOpen.Should().BeTrue();
            var settings = view.FindControl<SettingsView>("SettingsPanel")!;
            var token = settings.FindControl<TextBox>("GitHubTokenTextBox")!;
            settings.Navigation.GetSettingsFocusedControl(settings.Navigation.CollectSettingsFocusableControls())
                .Should().BeSameAs(token);
            settings.FindControl<Button>("CloseSettingsButton")!.Classes.Should().NotContain("gamepad-focused");
            button.Classes.Should().NotContain("gamepad-focused");
            GamepadTextInput.IsEditing.Should().BeTrue();
            token.IsFocused.Should().BeTrue();
            window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            window.KeyTextInput("x");
            window.KeyRelease(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
            Dispatcher.UIThread.RunJobs();
            view.Shell.SettingsOpen.Should().BeTrue();
            token.Text.Should().Be("x");
            token.IsFocused.Should().BeTrue();
        }
        finally
        {
            window.Close(); await view.ShutdownAsync();
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_library_actions_work_from_keyboard_and_shell_gamepad_navigation(bool gamepad)
    {
        var store = new Store();
        // Browse loads the catalog when it opens; keep that request off the network.
        using var http = new HttpClient(new Offline());
        using var manager = new GameManager(store, http);
        var view = new MainView(new() { SettingsStore = store, GameManager = manager,
            InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var library = view.FindControl<LibraryView>("LibraryPanel")!;
            library.UpdateEmptyState(true);
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            if (gamepad)
            {
                ((IFeatureNavigationHost)view).ApplyTransition(new(GamepadNavigationZone.Library, 0));
                library.Navigation.Navigate(QuiverLauncher.Services.NavigationDirection.Right).Should().BeTrue();
                library.EmptyLibraryBrowseButton.Classes.Should().Contain("gamepad-focused");
                library.Navigation.Navigate(QuiverLauncher.Services.NavigationDirection.Up).Should().BeTrue();
                library.EmptyLibraryAddButton.Classes.Should().NotContain("gamepad-focused");
                library.EmptyLibraryBrowseButton.Classes.Should().NotContain("gamepad-focused");
                ((IFeatureNavigationHost)view).ApplyTransition(new(GamepadNavigationZone.Library, 0));
                library.Navigation.Navigate(QuiverLauncher.Services.NavigationDirection.Left).Should().BeTrue();
                library.EmptyLibraryAddButton.Classes.Should().NotContain("gamepad-focused");
                library.EmptyLibraryBrowseButton.Classes.Should().NotContain("gamepad-focused");
                ((IFeatureNavigationHost)view).ApplyTransition(new(GamepadNavigationZone.Library, 1));
                library.EmptyLibraryBrowseButton.Classes.Should().Contain("gamepad-focused");
                library.Navigation.Confirm().Should().BeTrue();
                Dispatcher.UIThread.RunJobs();
                view.Shell.Mode.Should().Be(MainViewMode.Browse);
            }
            else
            {
                library.EmptyLibraryAddButton.Focus().Should().BeTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
                view.Shell.EntryEditorOpen.Should().BeTrue();
            }
        }
        finally
        {
            window.Close(); await view.ShutdownAsync();
            GamepadTextInput.Reset(); GamepadFocusChrome.SetKeyboardNavigationActive(false);
        }
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
    }

    [AvaloniaFact]
    public async Task GitHub_token_remains_editable_after_tab_switches_and_reopening_settings()
    {
        var store = new Store();
        var view = CreateView(store);
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            GamepadTextInput.SkipNativeFocusOverride = () => false;
            view.OpenGitHubApiTokenSettings();
            Dispatcher.UIThread.RunJobs();
            var settings = view.FindControl<SettingsView>("SettingsPanel")!;
            var tabs = settings.FindControl<TabControl>("SettingsTabControl")!;
            var token = settings.FindControl<TextBox>("GitHubTokenTextBox")!;
            GamepadTextInput.GetEngageOnConfirm(token).Should().BeTrue();

            for (var cycle = 0; cycle < 3; cycle++)
            {
                tabs.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                token.IsAttachedToVisualTree().Should().BeFalse();
                tabs.SelectedIndex = 4;
                Dispatcher.UIThread.RunJobs();
                settings.FindControl<TextBox>("GitHubTokenTextBox").Should().BeSameAs(token);
                token.IsAttachedToVisualTree().Should().BeTrue();
                var index = settings.Navigation.CollectSettingsFocusableControls().IndexOf(token);
                index.Should().BeGreaterThanOrEqualTo(0);
                settings.Navigation.ApplySettingsGamepadSelection(index);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                token.IsReadOnly.Should().BeTrue();
                var point = token.TranslatePoint(new Point(15, 15), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                GamepadTextInput.IsEditing.Should().BeTrue();
                token.IsReadOnly.Should().BeFalse();
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                await window.Clipboard!.SetTextAsync("dummy-token");
                window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, "v");
                Dispatcher.UIThread.RunJobs();
                token.Text.Should().Be("dummy-token");
                if (cycle == 0) store.Current.GitHubApiToken.Should().BeEmpty("typing edits a draft only");
                settings.SaveGitHubToken_Click(settings.FindControl<Button>("SaveGitHubTokenButton"), new Avalonia.Interactivity.RoutedEventArgs());
                store.Current.GitHubApiToken.Should().Be("dummy-token");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                GamepadTextInput.IsEditing.Should().BeFalse();
                view.Shell.SettingsOpen.Should().BeTrue();
                settings.Navigation.Confirm();
                GamepadTextInput.IsEditing.Should().BeTrue();
                token.CaretBrush.Should().NotBe(Avalonia.Media.Brushes.Transparent);
                ((ISettingsFeatureHost)view).CloseSettings();
                view.Shell.SettingsOpen.Should().BeFalse();
                view.OpenGitHubApiTokenSettings();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Controller_connection_changes_preserve_active_edit_and_selection(bool connected)
    {
        var view = CreateView(new Store());
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            view.OpenGitHubApiTokenSettings();
            Dispatcher.UIThread.RunJobs();
            var settings = view.FindControl<SettingsView>("SettingsPanel")!;
            var token = settings.FindControl<TextBox>("GitHubTokenTextBox")!;
            token.Text = "dummy-token";
            token.SelectionStart = 1;
            token.SelectionEnd = 5;
            // The navigation index may still refer to a tab after native mouse focus.
            settings.Navigation.FocusIndex = 0;
            typeof(MainView).GetMethod("HandleGamepadConnectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(view, [connected]);
            Dispatcher.UIThread.RunJobs();
            GamepadTextInput.Active.Should().BeSameAs(token);
            GamepadTextInput.IsEditing.Should().BeTrue();
            token.IsFocused.Should().BeTrue();
            token.IsReadOnly.Should().BeFalse();
            token.SelectedText.Should().Be("ummy");
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            GamepadTextInput.Reset();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Reopening_settings_focuses_the_remembered_tab(int tabIndex)
    {
        var view = CreateView(new Store());
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            GamepadFocusChrome.SetKeyboardNavigationActive(true);
            var settings = view.FindControl<SettingsView>("SettingsPanel")!;
            var toggle = typeof(MainView).GetMethod("SettingsButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Toggle() => toggle.Invoke(view, [view, new Avalonia.Interactivity.RoutedEventArgs()]);
            Toggle();
            Dispatcher.UIThread.RunJobs();
            settings.FindControl<TabControl>("SettingsTabControl")!.SelectedIndex = tabIndex;
            Toggle();
            Toggle();
            Dispatcher.UIThread.RunJobs();
            settings.Navigation.FocusIndex.Should().Be(tabIndex);
            var tabs = settings.Navigation.CollectSettingsTabItems();
            tabs[tabIndex].IsFocused.Should().BeTrue();
            tabs.Where((_, i) => i != tabIndex).Should().OnlyContain(t => !t.Classes.Contains("gamepad-focused"));
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false);
        }
    }

    private static MainView CreateView(Store store) => new(new()
    {
        SettingsStore = store, EnableInput = false, EnableMusic = false, InitializeOnOpen = false
    });

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new()
        {
            FirstStartup = false, EnableGamepadInput = true,
            AppsPath = Path.Combine(Path.GetTempPath(), "quiver-input-tests", Guid.NewGuid().ToString("N"))
        };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
