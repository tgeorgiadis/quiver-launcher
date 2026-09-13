using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Core.Services;
using System.Diagnostics;

namespace QuiverLauncher.Views;
public partial class LauncherBannerView : UserControl, IFeatureNavigationHandler
{
    private LauncherSession _session = null!;
    private SettingsViewModel _settingsViewModel = null!;
    private AppSettings _settings => _settingsViewModel.Current;

    private HttpClient _httpClient = null!;
    private IFeatureNavigationHost _host = null!;
    private Action _openSettings = null!;
    private GamepadNavigationService _gamepadNavigation => _host.Navigation;
    private bool IsGamepadFocusActive => _host.IsFocusActive;

    public LauncherBannerView() => InitializeComponent();
    public void Configure(LauncherSession session, SettingsViewModel settings, HttpClient client, IFeatureNavigationHost host, Action openSettings)
    {
        _session = session;
        _settingsViewModel = settings;
        _httpClient = client;
        _host = host;
        _openSettings = openSettings;
        var requests = ReleaseRequestCoordinator.For(client);
        requests.RequestCompleted += OnReleaseRequestCompleted;
        session.OnShutdown(() => requests.RequestCompleted -= OnReleaseRequestCompleted);
    }

    private void OnReleaseRequestCompleted(GitHubReleaseFetchResult result)
    {
        if (result.Provider != "github" || result.IsAuthenticated || !result.IsRateLimited)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_session.IsClosed || !string.IsNullOrWhiteSpace(_settings.GitHubApiToken)) return;
            AuthenticationRequired = true;
            var retry = result.RetryAt ?? result.ResetAt;
            GitHubTokenBannerText.Text = "GitHub's unauthenticated request limit was reached. Latest versions and some platform checks may be unavailable. Set a GitHub token to use the authenticated allowance."
                + (retry is { } time ? $" Without a token, retry after {time.ToLocalTime():g}." : "");
            ApplyTopBanner();
        });
    }

    public bool Navigate(NavigationDirection direction) => !_session.IsClosed && HandleAnnouncementBannerGamepadNavigation(direction);
    public bool Confirm()
    {
        if (_session.IsClosed)
            return false;
        ActivateTopBannerGamepadSelection();
        return true;
    }

    public bool Cancel() => _host.ApplyTransition(new(GamepadNavigationZone.TopBar, null));
    public bool Options() => false;
    public void RestoreFocus() => ApplyAnnouncementBannerGamepadSelection();
    public bool SynchronizePointer(object? source) => GamepadPointerFocusSync.Hit(_gamepadNavigation, CollectTopBannerGamepadControls(), GamepadNavigationZone.AnnouncementBanner, _topBannerGamepadIndex, index => ApplyAnnouncementBannerGamepadSelection(index), source);
    public bool EnterZone(GamepadZoneTransition transition)
    {
        ApplyAnnouncementBannerGamepadSelection(0);
        return true;
    }

    public void LeaveZone(GamepadNavigationZone zone)
    {
        if (zone != GamepadNavigationZone.AnnouncementBanner)
            ClearAnnouncementBannerGamepadFocus();
    }

    private void ClearFocusIfOnControls(IReadOnlyList<Control> controls)
    {
        var focus = TopLevel.GetTopLevel(this)?.FocusManager;
        if (focus?.GetFocusedElement()is Control control && controls.Contains(control))
            focus.Focus(null);
    }

    private string? _activeAnnouncementId;
    public async Task RefreshAnnouncementBannerAsync()
    {
        try
        {
            var payload = await AnnouncementService.TryFetchAsync(_httpClient, cancellationToken: _session.Token).ConfigureAwait(true);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_session.IsClosed)
                    return;
                _settings.EnsureInitialized();
                if (!AnnouncementService.ShouldShow(payload, _settings.DismissedAnnouncementIds))
                {
                    HideAnnouncementBanner();
                    ApplyTopBanner();
                    return;
                }

                ShowAnnouncementBanner(payload!);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Announcement banner fetch failed: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_session.IsClosed)
                    ApplyTopBanner();
            });
        }
    }

    private void ShowAnnouncementBanner(AnnouncementPayload payload)
    {
        var tokenHadGamepad = GitHubTokenBanner is { IsVisible: true } && _gamepadNavigation.ActiveZone == GamepadNavigationZone.AnnouncementBanner;
        if (GitHubTokenBanner != null)
            GitHubTokenBanner.IsVisible = false;
        _activeAnnouncementId = payload.Id;
        if (AnnouncementBannerText != null)
            AnnouncementBannerText.Text = payload.Message.Trim();
        if (AnnouncementBanner != null)
            AnnouncementBanner.IsVisible = true;
        if (tokenHadGamepad && IsGamepadFocusActive)
            ApplyAnnouncementBannerGamepadSelection(0);
    }

    private void HideAnnouncementBanner()
    {
        _activeAnnouncementId = null;
        if (AnnouncementBanner != null)
            AnnouncementBanner.IsVisible = false;
        if (AnnouncementBannerText != null)
            AnnouncementBannerText.Text = string.Empty;
    }

    public bool AuthenticationRequired { get; set; }
    public void ApplyTopBanner()
    {
        if (_session.IsClosed)
            return;
        _settings.EnsureInitialized();
        var announcementShowing = AnnouncementBanner is { IsVisible: true };
        var tokenWasVisible = GitHubTokenBanner is { IsVisible: true };
        var showToken = AuthenticationRequired && !announcementShowing && GitHubTokenBannerPolicy.ShouldShow(_settings.GitHubApiToken, _settings.GitHubTokenBannerPermanentlyDismissed, _settings.GitHubTokenBannerSnoozedUntilUtc, DateTimeOffset.UtcNow);
        if (GitHubTokenBanner != null)
            GitHubTokenBanner.IsVisible = showToken;
        if (tokenWasVisible && !showToken)
            RestoreGamepadAfterTopBannerDismiss();
    }

    private void AnnouncementBannerClose_Click(object? sender, RoutedEventArgs e)
    {
        DismissAnnouncementBanner();
    }

    private void GitHubTokenBannerSettings_Click(object? sender, RoutedEventArgs e)
    {
        _openSettings();
    }

    private void GitHubTokenBannerDontShowAgain_Click(object? sender, RoutedEventArgs e)
    {
        _settings.EnsureInitialized();
        _settings.GitHubTokenBannerPermanentlyDismissed = true;
        _settings.GitHubTokenBannerSnoozedUntilUtc = null;
        _settingsViewModel.Save(_settings);
        ApplyTopBanner();
    }

    private void GitHubTokenBannerClose_Click(object? sender, RoutedEventArgs e)
    {
        _settings.EnsureInitialized();
        _settings.GitHubTokenBannerSnoozedUntilUtc = GitHubTokenBannerPolicy.SnoozeUntil(DateTimeOffset.UtcNow);
        _settingsViewModel.Save(_settings);
        ApplyTopBanner();
    }

    private void DismissAnnouncementBanner()
    {
        var id = _activeAnnouncementId;
        HideAnnouncementBanner();
        if (!string.IsNullOrWhiteSpace(id))
        {
            _settings.EnsureInitialized();
            if (!_settings.DismissedAnnouncementIds.Any(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.DismissedAnnouncementIds.Add(id);
                _settingsViewModel.Save(_settings);
            }
        }

        ApplyTopBanner();
        RestoreGamepadAfterTopBannerDismiss();
    }

    private void RestoreGamepadAfterTopBannerDismiss()
    {
        ClearAnnouncementBannerGamepadFocus();
        if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.AnnouncementBanner && IsGamepadFocusActive)
        {
            ClearAnnouncementBannerGamepadFocus();
            if (IsAnnouncementBannerVisible)
                ApplyAnnouncementBannerGamepadSelection(0);
            else
                _host.ApplyTransition(new(GamepadNavigationZone.TopBar, Math.Max(0, _gamepadNavigation.TopBarSelectedIndex)));
        }
        else if (_gamepadNavigation.ActiveZone == GamepadNavigationZone.TopBar && IsGamepadFocusActive)
        {
            _host.ApplyTransition(new(GamepadNavigationZone.TopBar, Math.Max(0, _gamepadNavigation.TopBarSelectedIndex)));
        }
    }

    internal int _topBannerGamepadIndex;
    internal bool HandleAnnouncementBannerGamepadNavigation(NavigationDirection direction)
    {
        if (!IsAnnouncementBannerVisible)
        {
            return _host.ApplyTransition(new GamepadZoneTransition(_host.MainContentZone, 0));
        }

        var controls = CollectTopBannerGamepadControls();
        if (direction is NavigationDirection.Left or NavigationDirection.Right && controls.Count > 1)
        {
            var nextIndex = _gamepadNavigation.MoveHorizontalIndex(_topBannerGamepadIndex, direction, controls.Count);
            ApplyAnnouncementBannerGamepadSelection(nextIndex);
            return true;
        }

        var zoneTransition = _gamepadNavigation.TryGetZoneTransition(direction, GamepadNavigationZone.AnnouncementBanner, _host.MainContentZone, isListLayout: true, positions: null, currentIndex: _topBannerGamepadIndex, itemCount: Math.Max(1, controls.Count));
        if (zoneTransition.HasValue)
            return _host.ApplyTransition(zoneTransition.Value);
        return true;
    }

    internal bool IsAnnouncementBannerVisible => (AnnouncementBanner is { IsVisible: true } && AnnouncementBannerCloseButton is { IsVisible: true, IsEnabled: true }) || (GitHubTokenBanner is { IsVisible: true } && GitHubTokenBannerCloseButton is { IsVisible: true, IsEnabled: true });

    internal List<Control> CollectTopBannerGamepadControls()
    {
        var controls = new List<Control>();
        void Add(Control? control)
        {
            if (control != null && control.IsVisible && control.IsEnabled)
                controls.Add(control);
        }

        if (AnnouncementBanner is { IsVisible: true })
        {
            Add(AnnouncementBannerCloseButton);
            return controls;
        }

        if (GitHubTokenBanner is { IsVisible: true })
        {
            Add(GitHubTokenBannerSettingsButton);
            Add(GitHubTokenBannerDontShowAgainButton);
            Add(GitHubTokenBannerCloseButton);
        }

        return controls;
    }

    internal void ApplyAnnouncementBannerGamepadSelection(int? selectedIndex = null)
    {
        if (!IsAnnouncementBannerVisible)
            return;
        var controls = CollectTopBannerGamepadControls();
        if (controls.Count == 0)
            return;
        _host.ClearFocus();
        ClearAnnouncementBannerGamepadFocus();
        _gamepadNavigation.ActiveZone = GamepadNavigationZone.AnnouncementBanner;
        _topBannerGamepadIndex = _gamepadNavigation.ClampIndex(selectedIndex ?? _topBannerGamepadIndex, controls.Count);
        if (_topBannerGamepadIndex < 0)
            return;
        if (controls[_topBannerGamepadIndex] is StyledElement styled)
            styled.Classes.Set("gamepad-focused", true);
        GamepadControlActivation.ApplyGamepadHighlightFocus(controls[_topBannerGamepadIndex]);
    }

    internal void ActivateTopBannerGamepadSelection()
    {
        var controls = CollectTopBannerGamepadControls();
        var index = _gamepadNavigation.ClampIndex(_topBannerGamepadIndex, controls.Count);
        if (index < 0 || controls[index] is not Button button)
            return;
        GamepadControlActivation.ActivateButton(button);
    }

    internal void ClearAnnouncementBannerGamepadFocus()
    {
        foreach (var control in CollectTopBannerGamepadControls())
        {
            if (control is StyledElement styled)
                styled.Classes.Set("gamepad-focused", false);
        }

        if (AnnouncementBannerCloseButton is StyledElement announcementClose)
            announcementClose.Classes.Set("gamepad-focused", false);
        if (GitHubTokenBannerSettingsButton is StyledElement settingsLink)
            settingsLink.Classes.Set("gamepad-focused", false);
        if (GitHubTokenBannerDontShowAgainButton is StyledElement dismissLink)
            dismissLink.Classes.Set("gamepad-focused", false);
        if (GitHubTokenBannerCloseButton is StyledElement tokenClose)
            tokenClose.Classes.Set("gamepad-focused", false);
        var focused = new List<Control>();
        if (AnnouncementBannerCloseButton != null)
            focused.Add(AnnouncementBannerCloseButton);
        if (GitHubTokenBannerSettingsButton != null)
            focused.Add(GitHubTokenBannerSettingsButton);
        if (GitHubTokenBannerDontShowAgainButton != null)
            focused.Add(GitHubTokenBannerDontShowAgainButton);
        if (GitHubTokenBannerCloseButton != null)
            focused.Add(GitHubTokenBannerCloseButton);
        ClearFocusIfOnControls(focused);
    }
}
