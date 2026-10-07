using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class KioskLockTests
{
    [Fact]
    public void Pin_round_trip_rejects_a_mismatch_and_clearing_removes_it()
    {
        var settings = new AppSettings();
        KioskLock.HasPin(settings).Should().BeFalse();
        KioskLock.VerifyPin(settings, "anything").Should().BeTrue();

        KioskLock.SetPin(settings, "2468");
        settings.KioskPinHash.Should().NotBe("2468");
        settings.KioskPinSalt.Should().NotBeNullOrEmpty();
        KioskLock.HasPin(settings).Should().BeTrue();
        KioskLock.VerifyPin(settings, "2468").Should().BeTrue();
        KioskLock.VerifyPin(settings, "0000").Should().BeFalse();
        KioskLock.VerifyPin(settings, "").Should().BeFalse();

        KioskLock.SetPin(settings, null);
        KioskLock.HasPin(settings).Should().BeFalse();
        settings.KioskPinHash.Should().BeEmpty();
        settings.KioskPinSalt.Should().BeEmpty();
    }

    [Fact]
    public void Session_stays_locked_until_unlock_and_checkbox_changes_do_not_arm_a_running_session()
    {
        var locked = new KioskSession(armed: true);
        locked.IsLocked.Should().BeTrue();
        locked.Unlock();
        locked.IsLocked.Should().BeFalse();
        locked.Lock();
        locked.IsLocked.Should().BeTrue();
        var idle = new KioskSession(armed: false);
        idle.Lock();
        idle.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void Parse_strips_kiosk_without_arming_the_process()
    {
        var parsed = KioskLaunch.Parse(["--kiosk", "--list"]);
        parsed.Kiosk.Should().BeTrue();
        parsed.Args.Should().Equal("--list");
        KioskLaunch.Parse(["--List"]).Kiosk.Should().BeFalse();
        KioskLaunch.IsProcessArmed.Should().BeFalse();
    }

    [Fact]
    public void Lock_suppresses_update_prompts()
    {
        var settings = new AppSettings { PromptAppUpdateReviews = true };

        UpdatePromptPolicy.ShouldPromptAppUpdateReviews(settings, kioskLocked: true).Should().BeFalse();
        UpdatePromptPolicy.ShouldPromptAppUpdateReviews(settings, kioskLocked: false).Should().BeTrue();
        UpdatePromptPolicy.ShouldPromptLauncherSelfUpdate(kioskLocked: true).Should().BeFalse();
        UpdatePromptPolicy.ShouldPromptLauncherSelfUpdate(kioskLocked: false).Should().BeTrue();
    }

    [AvaloniaFact]
    public void Card_menu_keeps_only_launch_while_locked()
    {
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "Launch" },
                new MenuItem { Header = "Download" },
                new MenuItem
                {
                    Header = "Customize",
                    Items = { new MenuItem { Header = "Edit Entry" } },
                },
            },
        };

        LibraryKioskMenu.Apply(menu, locked: true);
        GamepadContextMenuNavigation.CollectNavigableMenuItems(menu)
            .Select(item => item.Header)
            .Should().Equal("Launch");

        LibraryKioskMenu.Apply(menu, locked: false);
        GamepadContextMenuNavigation.CollectNavigableMenuItems(menu)
            .Select(item => item.Header)
            .Should().Equal("Launch", "Download", "Customize");
    }

    [AvaloniaFact]
    public async Task Chord_unlocks_before_any_keyboard_or_gamepad_focus()
    {
        var store = new Store();
        store.Current.KioskMode = true;
        var view = new MainView(new() { SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 800, Height = 600 };
        using var host = new DesktopHostController(window, view);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            view.FindControl<Button>("SettingsButton")!.IsVisible.Should().BeFalse();

            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Source = window,
                Key = Key.K,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt,
            });
            Dispatcher.UIThread.RunJobs();

            view.SettingsModel.KioskLocked.Should().BeFalse();
            view.FindControl<Button>("SettingsButton")!.IsVisible.Should().BeTrue();
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public void Pin_prompt_focuses_the_text_field_instead_of_unlock()
    {
        GamepadFocusChrome.SetKeyboardNavigationActive(true);
        var pinBox = new TextBox { PasswordChar = '•', MinWidth = 240 };
        var unlock = new Button { Content = "Unlock", MinWidth = 100, IsDefault = true };
        var window = new Window
        {
            Width = 420,
            SizeToContent = SizeToContent.Height,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 12,
                Children = { pinBox, unlock },
            },
        };
        try
        {
            GamepadFocusChrome.SetActive(true, window);
            LauncherPromptService.FocusKioskPinWhenOpened(window, pinBox);
            GamepadModalDialogNavigation.Attach(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(pinBox);
        }
        finally
        {
            GamepadFocusChrome.SetKeyboardNavigationActive(false);
            GamepadFocusChrome.SetActive(false, window);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Locked_chrome_blocks_exit_until_the_session_is_unlocked()
    {
        var store = new Store();
        store.Current.KioskMode = true;
        store.Current.ShowOSTopBar = true;
        store.Current.CloseAfterLaunch = true;
        var view = new MainView(new() { SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1200, Height = 720 };
        using var host = new DesktopHostController(window, view);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.WindowState.Should().Be(SteamDeckEnvironment.DesktopFullscreenWindowState());
            window.WindowDecorations.Should().Be(WindowDecorations.BorderOnly);
            view.FindControl<Button>("SettingsButton")!.IsVisible.Should().BeFalse();
            view.FindControl<Button>("CloseLauncherButton")!.IsVisible.Should().BeFalse();
            view.FindControl<Button>("ToggleMaximizeButton")!.IsVisible.Should().BeFalse();
            view.FindControl<Button>("CheckForUpdatesButton")!.IsVisible.Should().BeFalse();
            view.FindControl<Button>("BrowseNavButton")!.IsVisible.Should().BeFalse();
            view.FindControl<StackPanel>("ExternalLinksHost")!.IsVisible.Should().BeFalse();
            view.FindControl<LibraryToolbarView>("LibraryToolbar")!
                .FindControl<Button>("AddNewEntryButton")!.IsVisible.Should().BeFalse();
            view.FindControl<SplitView>("MainSplitView")!.IsPaneOpen.Should().BeFalse();
            store.Current.DesktopSidebarCollapsed.Should().BeFalse();
            view.FindControl<StackPanel>("HeaderFixedActions")!.Margin.Right.Should().Be(16);

            window.Close();
            Dispatcher.UIThread.RunJobs();
            window.IsVisible.Should().BeTrue();
            host.CloseAfterLaunch(true);
            window.IsVisible.Should().BeTrue();

            view.SettingsModel.UnlockKioskSession();
            Dispatcher.UIThread.RunJobs();
            view.FindControl<Button>("SettingsButton")!.IsVisible.Should().BeTrue();
            view.FindControl<Button>("CloseLauncherButton")!.IsVisible.Should().BeTrue();
            view.FindControl<Button>("BrowseNavButton")!.IsVisible.Should().BeTrue();
            view.FindControl<StackPanel>("ExternalLinksHost")!.IsVisible.Should().BeTrue();
            view.FindControl<LibraryToolbarView>("LibraryToolbar")!
                .FindControl<Button>("AddNewEntryButton")!.IsVisible.Should().BeTrue();
            window.WindowDecorations.Should().Be(WindowDecorations.Full);
            view.FindControl<SplitView>("MainSplitView")!.IsPaneOpen.Should().BeTrue();
            view.FindControl<StackPanel>("HeaderFixedActions")!.Margin.Right.Should().Be(0);
            store.Current.KioskMode.Should().BeTrue();
            window.WindowState = WindowState.Normal;

            view.SettingsModel.LockKioskSession();
            Dispatcher.UIThread.RunJobs();
            window.WindowState.Should().Be(SteamDeckEnvironment.DesktopFullscreenWindowState());
            window.WindowDecorations.Should().Be(WindowDecorations.BorderOnly);
            view.FindControl<Button>("SettingsButton")!.IsVisible.Should().BeFalse();
            view.FindControl<Button>("CloseLauncherButton")!.IsVisible.Should().BeFalse();
            view.FindControl<Button>("BrowseNavButton")!.IsVisible.Should().BeFalse();
            view.FindControl<StackPanel>("ExternalLinksHost")!.IsVisible.Should().BeFalse();
            view.FindControl<StackPanel>("HeaderFixedActions")!.Margin.Right.Should().Be(16);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            window.IsVisible.Should().BeTrue();

            view.SettingsModel.UnlockKioskSession();
            host.RequestExit();
            window.IsVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
            await view.ShutdownAsync();
        }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new()
        {
            FirstStartup = false,
            EnableGamepadInput = false,
            AppsPath = Path.Combine(Path.GetTempPath(), "quiver-kiosk-tests", Guid.NewGuid().ToString("N")),
        };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
}
