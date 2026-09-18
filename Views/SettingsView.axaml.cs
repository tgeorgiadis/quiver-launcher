using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
public partial class SettingsView : UserControl
{
    private SettingsFeatureContext? _context;
    private ISettingsFeatureHost _host = null!;
    public SettingsViewModel Model { get; private set; } = new();
    public SettingsNavigationController Navigation { get; private set; } = null!;
    public InputBindingsViewModel Bindings { get; private set; } = null!;
    private AppSettings _settings => _context!.Settings();
    private SettingsViewModel _settingsViewModel => Model;
    private LauncherSession _session => _context!.Session;
    internal bool IsClosed => _context == null || _session.IsClosed;
    private LauncherMusicService _music => _context!.Music;
    private GameManager _gameManager => _context!.GameManager;
    private InputService? _inputService => _context?.Input();
    private GamepadNavigationService _gamepadNavigation => _host.Navigation;
    private IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider ?? throw new InvalidOperationException("Storage provider is not available.");

    public SettingsView()
    {
        InitializeComponent();
        GamepadComboBoxNavigation.Attach(BackgroundUpdateIntervalComboBox);
        GamepadComboBoxNavigation.Attach(MouseWheelScrollSpeedComboBox);
        GamepadComboBoxNavigation.Attach(InterfaceScaleComboBox);
    }

    public void Configure(SettingsFeatureContext context, ISettingsFeatureHost host)
    {
        _context = context;
        _host = host;
        Navigation = new SettingsNavigationController(this, host.Navigation);
        Model = context.Model;
        Bindings = new InputBindingsViewModel(Model, () => context.Input(), action => Dispatcher.UIThread.Post(action));
        Bindings.BindingsChanged += host.UpdateGamepadHintsBar;
        Bindings.Error += message => _ = _session.RunAsync(() => host.ShowMessageBoxAsync(message, "Controls"));
        BindingsView.DataContext = Bindings;
        Model.Changed += ApplyChange;
        Model.SaveFailed += ReportSaveFailure;
        context.Session.OnShutdown(() =>
        {
            Model.Changed -= ApplyChange;
            Model.SaveFailed -= ReportSaveFailure;
        });
        Model.ConfigurePathChanges(async (path, token) =>
        {
            if (string.IsNullOrEmpty(path) || Directory.Exists(path))
                return true;
            var confirmed = await _host.ShowMessageBoxAsync($"The directory '{path}' does not exist. Create it?", "Directory Not Found", true);
            return confirmed && !token.IsCancellationRequested;
        }, async (path, token) =>
        {
            await context.GameManager.UpdateGamesFolderAsync(path);
            token.ThrowIfCancellationRequested();
            await context.GameManager.LoadGamesAsync();
        });
        DataContext = Model;
    }

    private void ReportSaveFailure(Exception ex) => _ = _session.RunAsync(() => _host.ShowMessageBoxAsync($"Failed to save settings: {ex.Message}", "Save Error"));
    private void ApplyChange(SettingsChange change)
    {
        if (_context == null || _session.IsClosed)
            return;
        _host.NotifyPresentationChanged();
        if (change.HasFlag(SettingsChange.Layout))
        {
            _host.UpdateGridLayoutVisibility();
            if (PlatformCapabilities.IsMobile)
                _host.FitMobileLibraryCardWidth();
        }

        if (change.HasFlag(SettingsChange.LibraryDisplay))
            _host.ApplyLibraryDisplaySettingsToGames();
        if (change.HasFlag(SettingsChange.Sorting))
            _host.SetIgnoreArticlesWhenSorting(_settings.IgnoreArticlesWhenSorting);
        if (change.HasFlag(SettingsChange.Tray))
            _host.ApplyTrayAndBackgroundUpdateSettings();
        if (change.HasFlag(SettingsChange.Badges) && _settings.ShowLibraryAppUpdateBadges)
            _ = _session.RunAsync(_host.ApplyLibraryCatalogPendingBadgesAsync);
        if (change.HasFlag(SettingsChange.Input))
        {
            if (!_settings.EnableGamepadInput)
            {
                Bindings.Cancel();
                GamepadFocusChrome.SetKeyboardNavigationActive(false);
                _host.ClearGamepadFocus();
            }

            _inputService?.SetGamepadEnabled(_settings.EnableGamepadInput);
            _host.UpdateGamepadChromeClass();
            if (_settings.EnableGamepadInput)
                _host.SelectInitialGamepadItemForCurrentView();
            _host.NotifyGamepadUiChanged();
            _host.UpdateGamepadHintsBar();
        }
    }

    private void OnSettingChanged()
    {
        if (_context == null || _suppressSettingsUiEvents || _session.IsClosed)
            return;
        try
        {
            Model.Save(_settings);
            _host.NotifyPresentationChanged();
        }
        catch (Exception ex)
        {
            _ = _session.RunAsync(() => _host.ShowMessageBoxAsync($"Failed to save settings: {ex.Message}", "Save Error"));
        }
    }

    private static void DispatchCheckBoxChanged(object sender, RoutedEventArgs e, Action<object, RoutedEventArgs> whenChecked, Action<object, RoutedEventArgs> whenUnchecked)
    {
        if (sender is CheckBox { IsChecked: true })
            whenChecked(sender, e);
        else
            whenUnchecked(sender, e);
    }

    internal void RequestClose() => _host.CloseSettings();
    private void CloseSettingsPanel_Click(object? sender, RoutedEventArgs e) => RequestClose();
    private void ThemeColorPicker_Click(object? sender, RoutedEventArgs e) => _host.EditTheme(false);
    private void SecondaryColorPicker_Click(object? sender, RoutedEventArgs e) => _host.EditTheme(true);
    private void ApplyPreset(CardLayoutPreset preset)
    {
        Model.ApplyCardLayout(preset);
        UpdateSettingsUI();
    }

    public void LayoutPreset_Landscape_Click(object sender, RoutedEventArgs e) => ApplyPreset(CardLayoutPreset.Landscape);
    public void LayoutPreset_Portrait_Click(object sender, RoutedEventArgs e) => ApplyPreset(CardLayoutPreset.Portrait);
    public void LayoutPreset_Square_Click(object sender, RoutedEventArgs e) => ApplyPreset(CardLayoutPreset.Square);
    public void LayoutPreset_SquareAlt_Click(object sender, RoutedEventArgs e) => ApplyPreset(CardLayoutPreset.SquareCompact);
    public void LayoutPreset_List_Click(object sender, RoutedEventArgs e) => ApplyPreset(CardLayoutPreset.List);
    private void PlatformAuto_Click(object sender, RoutedEventArgs e) => Model.Platform = TargetOS.Auto;
    private void PlatformWindows_Click(object sender, RoutedEventArgs e) => Model.Platform = TargetOS.Windows;
    private void PlatformMacOS_Click(object sender, RoutedEventArgs e) => Model.Platform = TargetOS.MacOS;
    private void PlatformLinuxX64_Click(object sender, RoutedEventArgs e) => Model.Platform = TargetOS.LinuxX64;
    private void PlatformLinuxARM64_Click(object sender, RoutedEventArgs e) => Model.Platform = TargetOS.LinuxARM64;
    private void PlatformAndroid_Click(object sender, RoutedEventArgs e) => Model.Platform = TargetOS.Android;
    private void OptionsButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
            _host.OpenMenu(button, menu);
    }

    public void FocusApiToken(bool gitLab)
    {
        SettingsTabControl.SelectedIndex = 4;
        Dispatcher.UIThread.Post(() =>
        {
            if (_context == null || _session.IsClosed || !IsVisible)
                return;
            var target = gitLab ? GitLabTokenTextBox : GitHubTokenTextBox;
            var index = Navigation.CollectSettingsFocusableControls().IndexOf(target);
            Navigation.ApplySettingsGamepadSelection(index);
            target.BringIntoView();
            GamepadControlActivation.ActivateTextBox(target);
        }, DispatcherPriority.Loaded);
    }

    public void CancelPendingWork()
    {
        Model.CancelPathChange();
        Bindings.Dispose();
    }

    private async void GamePathTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_context == null || _suppressSettingsUiEvents)
            return;
        var path = GamePathTextBox.Text ?? string.Empty;
        await _session.RunAsync(() => ChangeAppsPathAsync(path, debounce: true));
    }

    private async Task ChangeAppsPathAsync(string path, bool debounce)
    {
        try
        {
            if (await Model.ChangeAppsPathAsync(path, _session.Token, debounce) && !_session.IsClosed)
                _host.ApplySorting();
        }
        catch (Exception ex)when (!_session.IsClosed)
        {
            await _host.ShowMessageBoxAsync($"Failed to update apps path: {ex.Message}", "Error");
        }
    }

    private async void ClearGamePath_Click(object? sender, RoutedEventArgs e)
    {
        if (_context == null)
            return;
        await _session.RunAsync(() => ChangeAppsPathAsync(string.Empty, debounce: false));
    }

    private async void BrowseGamePath_Click(object? sender, RoutedEventArgs e)
    {
        if (_context == null)
            return;
        await _session.RunAsync(async () =>
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Apps Folder", AllowMultiple = false, });
                if (_session.IsClosed || folders.Count == 0)
                    return;
                await ChangeAppsPathAsync(folders[0].Path.LocalPath, debounce: false);
            }
            catch (Exception ex)when (!_session.IsClosed)
            {
                await _host.ShowMessageBoxAsync($"Failed to select folder: {ex.Message}", "Error");
            }
        });
    }

    internal bool _suppressSettingsUiEvents;
    internal void UpdateSettingsUI()
    {
        if (_context == null)
            return;
        _suppressSettingsUiEvents = true;
        try
        {
            Model.Refresh();
            _host.UpdateGridLayoutVisibility();
            if (GitHubTokenTextBox != null)
                GitHubTokenTextBox.Text = _settings.GitHubApiToken;
            if (GitLabTokenTextBox != null)
                GitLabTokenTextBox.Text = _settings.GitLabApiToken;
            if (GamePathTextBox != null)
                GamePathTextBox.Text = _settings.AppsPath;
            if (LinuxWindowsLaunchCommandTextBox != null)
                LinuxWindowsLaunchCommandTextBox.Text = _settings.LinuxWindowsLaunchCommand;
            if (BackgroundUpdateIntervalComboBox != null)
            {
                var interval = BackgroundUpdateCheckIntervals.Normalize(_settings.BackgroundUpdateCheckIntervalMinutes);
                foreach (var entry in BackgroundUpdateIntervalComboBox.Items)
                {
                    if (entry is ComboBoxItem item && item.Tag as string == interval.ToString())
                    {
                        BackgroundUpdateIntervalComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            SelectLibraryNameStyleComboBox(_settings.LibraryNameStyle);
            SelectLibraryCardTagMaxLinesComboBox(_settings.LibraryCardTagMaxLines);
            Bindings.RefreshControllers();
            Bindings.Refresh();
        }
        finally
        {
            _suppressSettingsUiEvents = false;
        }
    }

    internal void BackgroundUpdateIntervalComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUiEvents || _settings == null)
            return;
        if (BackgroundUpdateIntervalComboBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string tag || !int.TryParse(tag, out var minutes))
        {
            return;
        }

        _settings.BackgroundUpdateCheckIntervalMinutes = BackgroundUpdateCheckIntervals.Normalize(minutes);
        OnSettingChanged();
        _host.ApplyTrayAndBackgroundUpdateSettings();
    }

    internal void LibraryNameStyleComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_context == null || LibraryNameStyleComboBox?.SelectedItem is not ComboBoxItem item)
            return;
        var style = (item.Tag as string) switch
        {
            "NameOnly" => LibraryNameStyle.NameOnly,
            "ProjectOnly" => LibraryNameStyle.ProjectOnly,
            "NameAndProjectInTitle" => LibraryNameStyle.NameAndProjectInTitle,
            _ => LibraryNameStyle.NameAndProject,
        };
        if (_settings.LibraryNameStyle == style)
            return;
        _settings.LibraryNameStyle = style;
        OnSettingChanged();
        _host.ApplyLibraryDisplaySettingsToGames();
        _host.ApplySorting();
    }

    internal void LibraryCardTagMaxLinesComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUiEvents || _settings == null || LibraryCardTagMaxLinesComboBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string tag || !int.TryParse(tag, out var maxLines))
        {
            return;
        }

        maxLines = TagChipHelper.NormalizeLibraryCardTagMaxLines(maxLines);
        if (_settings.LibraryCardTagMaxLines == maxLines)
            return;
        _settings.LibraryCardTagMaxLines = maxLines;
        OnSettingChanged();
        _host.ApplyLibraryDisplaySettingsToGames();
    }

    internal void SelectLibraryCardTagMaxLinesComboBox(int maxLines)
    {
        if (LibraryCardTagMaxLinesComboBox == null)
            return;
        var tag = TagChipHelper.NormalizeLibraryCardTagMaxLines(maxLines).ToString();
        foreach (var entry in LibraryCardTagMaxLinesComboBox.Items)
        {
            if (entry is ComboBoxItem item && item.Tag as string == tag)
            {
                LibraryCardTagMaxLinesComboBox.SelectedItem = item;
                return;
            }
        }

        foreach (var entry in LibraryCardTagMaxLinesComboBox.Items)
        {
            if (entry is ComboBoxItem item && item.Tag as string == TagChipHelper.DefaultLibraryCardTagMaxLines.ToString())
            {
                LibraryCardTagMaxLinesComboBox.SelectedItem = item;
                break;
            }
        }
    }

    internal void SelectLibraryNameStyleComboBox(LibraryNameStyle style)
    {
        if (LibraryNameStyleComboBox == null)
            return;
        var tag = style switch
        {
            LibraryNameStyle.NameOnly => "NameOnly",
            LibraryNameStyle.ProjectOnly => "ProjectOnly",
            LibraryNameStyle.NameAndProjectInTitle => "NameAndProjectInTitle",
            _ => "NameAndProject",
        };
        foreach (var entry in LibraryNameStyleComboBox.Items)
        {
            if (entry is ComboBoxItem item && item.Tag as string == tag)
            {
                LibraryNameStyleComboBox.SelectedItem = item;
                break;
            }
        }
    }

    // TextBox contents are drafts. Only explicit save/clear changes the credential context.
    internal void GitHubTokenTextBox_TextChanged(object sender, TextChangedEventArgs e) { }
    internal void GitLabTokenTextBox_TextChanged(object sender, TextChangedEventArgs e) { }
    internal void SaveGitHubToken_Click(object? sender, RoutedEventArgs e)
    {
        if (_context == null || _suppressSettingsUiEvents) return;
        Model.SaveApiToken("github", GitHubTokenTextBox.Text);
        _host.ApplyTopBanner();
    }
    internal void SaveGitLabToken_Click(object? sender, RoutedEventArgs e)
    {
        if (_context == null || _suppressSettingsUiEvents) return;
        Model.SaveApiToken("gitlab", GitLabTokenTextBox.Text);
    }

    internal void BackgroundPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_context != null && sender is TextBox textBox)
        {
            _settings.BackgroundImagePath = textBox.Text ?? string.Empty;
            OnSettingChanged();
        }
    }

    internal void LinuxWindowsLaunchCommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_context != null && sender is TextBox textBox)
        {
            _settings.LinuxWindowsLaunchCommand = textBox.Text?.Trim() ?? string.Empty;
            OnSettingChanged();
        }
    }

    internal void GitHubTokenHelp_Click(object? sender, RoutedEventArgs e)
    {
        GitHubTokenHelpText.IsVisible = !GitHubTokenHelpText.IsVisible;
        GitHubTokenHelpButton.Content = GitHubTokenHelpText.IsVisible
            ? "Hide guide" : "Setup guide";
    }

    internal void CreateGitHubToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string githubTokenUrl = GitHubTokenSetupGuide.CreateTokenUrl;
            _host.OpenUrl(githubTokenUrl);
        }
        catch (Exception ex)
        {
            _ = _session.RunAsync(() => _host.ShowMessageBoxAsync($"Failed to open GitHub token page: {ex.Message}", "Error"));
        }
    }

    internal void ClearGitHubToken_Click(object sender, RoutedEventArgs e)
    {
        if (_context != null)
        {
            Model.SaveApiToken("github", "");
            if (GitHubTokenTextBox != null)
                GitHubTokenTextBox.Text = string.Empty;
            OnSettingChanged();
            _host.ApplyTopBanner();
        }
    }

    internal void CreateGitLabToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = "https://gitlab.com/-/user_settings/personal_access_tokens";
            _host.OpenUrl(url);
        }
        catch (Exception ex)
        {
            _ = _session.RunAsync(() => _host.ShowMessageBoxAsync($"Failed to open GitLab token page: {ex.Message}", "Error"));
        }
    }

    internal void ClearGitLabToken_Click(object sender, RoutedEventArgs e)
    {
        if (_context != null)
        {
            Model.SaveApiToken("gitlab", "");
            if (GitLabTokenTextBox != null)
                GitLabTokenTextBox.Text = string.Empty;
            OnSettingChanged();
        }
    }

    internal async void ClearIconCache_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null || _suppressSettingsUiEvents)
            return;
        await _session.RunAsync(async () =>
        {
            try
            {
                await _gameManager.ClearIconCacheAsync();
            }
            catch (Exception ex)
            {
                await _host.ShowMessageBoxAsync($"Failed to clear icon cache: {ex.Message}", "Error");
            }
        });
    }

    internal async void SelectBackgroundImage_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null || _suppressSettingsUiEvents)
            return;
        await _session.RunAsync(async () =>
        {
            var storageProvider = StorageProvider;
            var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Background Image", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("Image Files") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" } } } });
            if (file.Count > 0)
            {
                var selectedFile = file[0];
                Model.BackgroundImagePath = selectedFile.Path.LocalPath;
                _settings.BackgroundImagePath = Model.BackgroundImagePath;
                OnSettingChanged();
            }
        });
    }

    internal void ClearBackgroundImage_Click(object sender, RoutedEventArgs e)
    {
        Model.BackgroundImagePath = string.Empty;
        _settings.BackgroundImagePath = string.Empty;
        OnSettingChanged();
    }

    internal void BackgroundOpacitySlider_ValueChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_suppressSettingsUiEvents && _context != null && sender is Slider slider)
        {
            Model.BackgroundOpacity = (float)slider.Value;
            _settings.BackgroundOpacity = Model.BackgroundOpacity;
            OnSettingChanged();
        }
    }

    internal async void SelectLauncherMusic_Click(object sender, RoutedEventArgs e)
    {
        if (_context == null || _suppressSettingsUiEvents)
            return;
        await _session.RunAsync(async () =>
        {
            var storageProvider = StorageProvider;
            var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Launcher Music", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("Audio Files") { Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a", "*.wma", "*.aac" } } } });
            if (file.Count > 0)
            {
                var selectedFile = file[0];
                Model.LauncherMusicPath = selectedFile.Path.LocalPath;
                _settings.LauncherMusicPath = Model.LauncherMusicPath;
                OnSettingChanged();
                _music.Play(Model.LauncherMusicPath);
            }
        });
    }

    internal void ClearLauncherMusic_Click(object sender, RoutedEventArgs e)
    {
        _music.Stop();
        Model.LauncherMusicPath = string.Empty;
        _settings.LauncherMusicPath = string.Empty;
        OnSettingChanged();
    }

    internal void MusicVolumeSlider_ValueChanged(object sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_suppressSettingsUiEvents && _context != null && sender is Slider slider)
        {
            Model.MusicVolume = (float)slider.Value;
            _settings.MusicVolume = Model.MusicVolume;
            OnSettingChanged();
        }
    }
}
