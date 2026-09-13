using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;

namespace QuiverLauncher.Views;
public partial class ModsView : UserControl
{
    private ModsFeatureContext _context = null!;
    internal IModsFeatureHost Host { get; private set; } = null!;
    private GameManager _gameManager => _context.Games;
    private AppSettings _settings => _context.Settings();
    private QuiverLauncher.ViewModels.SettingsViewModel _settingsViewModel => _context.SettingsModel;
    private LauncherSession _session => _context.Session;
    private QuiverLauncher.ViewModels.ShellViewModel Shell => _context.Shell;
    private GamepadNavigationService _gamepadNavigation => Host.Navigation;
    private bool IsGamepadFocusActive => Host.IsFocusActive;
    public ModDetailsView Details { get; } = new()
    {
        IsVisible = false
    };
    public QuiverLauncher.ViewModels.ModsViewModel Model { get; } = new();
    public ModsCatalogWorkspace Workspace { get; private set; } = null!;
    public ModsActions Actions { get; private set; } = null!;
    public System.Collections.ObjectModel.ObservableCollection<ModListItem> ModListRows => Model.Rows;
    private Control ModsPanel => this;
    public ModsNavigation Navigation { get; private set; } = null!;

    public ModsView()
    {
        InitializeComponent();
        DataContext = Model;
        GamepadComboBoxNavigation.Attach(ModsSortByComboBox);
    }

    public void Configure(ModsFeatureContext context, IModsFeatureHost host)
    {
        _context = context;
        Host = host;
        Workspace = new ModsCatalogWorkspace(context.Games, context.Session, Model, action => Dispatcher.UIThread.InvokeAsync(action).GetTask());
        Actions = new ModsActions(context.Games, context.Session, Model, Workspace, ShowModDownloadFilePickerAsync, host.ShowErrorAsync, host.ChooseAsync);
        Workspace.RowsChanging += CaptureListFocus;
        Workspace.RowsChanged += RestoreListFocus;
        Model.PropertyChanged += (_, e) =>
        {
            if (!context.Session.IsClosed && e.PropertyName == nameof(Model.ListIsLoading))
                UpdateModsListPlaceholder();
        };
        Navigation = new ModsNavigation(this, context.Session, host);
        Details.Configure(context, host);
        Details.Closed += () =>
        {
            if (_session.IsClosed)
                return;
            if (Shell.ModsOpen)
            {
                _gamepadNavigation.ActiveZone = GamepadNavigationZone.ModsOverlayList;
                if (IsGamepadFocusActive)
                    Navigation.ApplyModsListSelection(Math.Max(0, Navigation._modsGamepadListIndex));
            }
            else
                Host.RestoreCurrentFocus();
        };
    }

    private void ResetGamepadNavigationIndices() => Host.ResetNavigation();
    private void UpdateMainViewUi() => Host.RefreshShell();
    private void SelectInitialLibraryGamepadItem() => Host.RestoreLibraryFocus();
    private void SelectInitialGamepadItemForCurrentView() => Host.RestoreCurrentFocus();
    private void ClearGamepadFocus() => Host.ClearFocus();
    private GamepadNavigationZone GetMainContentGamepadZone() => Host.MainContentZone;
    private bool TryApplyGamepadZoneTransition(GamepadZoneTransition transition) => Host.ApplyTransition(transition);
    private void OpenUrl(string url) => Host.OpenUrl(url);
    private Task ShowMessageBoxAsync(string message, string title) => Host.ShowErrorAsync(message, title);
    private Task<MessagePromptResult> ShowChoicePromptAsync(string message, string title) => Host.ChooseAsync(message, title);
    private void OnPropertyChanged(string name) => Host.NotifyHints();
    private const string GamepadHintsVisible = "GamepadHintsVisible";
    // null = All
    private ModInstallService ModInstaller => Workspace.ModInstaller;
    private ModCatalogLoader ModCatalog => Workspace.ModCatalog;

    internal void OpenMods_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        var game = (sender as MenuItem)?.CommandParameter as GameInfo ?? (sender as Control)?.DataContext as GameInfo;
        if (game == null || !game.CanOpenMods)
            return;
        _ = _session.RunAsync(() => OpenModsOverlayAsync(game));
    }

    internal async Task OpenModsOverlayAsync(GameInfo game)
    {
        Workspace.Cancel();
        var version = Model.Open(game, _settings.ModsIncludeNsfw);
        Shell.ModsOpen = true;
        Shell.AppUpdatesOpen = false;
        Shell.Mode = MainViewMode.Library;
        if (ModsSearchTextBox != null)
            ModsSearchTextBox.Text = string.Empty;
        ApplyModsSortSelection(_settings.ModsSortBy);
        UpdateModsTabButtons();
        BuildModsSourceFilterButtons();
        ResetGamepadNavigationIndices();
        UpdateMainViewUi();
        if (ModsHeaderText != null)
            ModsHeaderText.Text = $"Mods — {game.Name}";
        SetModsStatus(string.Empty);
        await Workspace.RefreshAsync(forceRefresh: false).ConfigureAwait(true);
        if (_session.IsClosed || version != Model.OpenVersion || !ReferenceEquals(game, Model.Game))
            return;
        if (IsGamepadFocusActive)
            Navigation.SelectInitialModsGamepadItem();
        else
            ClearGamepadFocus();
    }

    internal void CloseModsOverlay()
    {
        if (Shell.ModDetailsOpen)
            Details.Close();
        Workspace.Cancel();
        Model.Close();
        Shell.ModsOpen = false;
        Navigation.ClearModsGamepadFocus();
        UpdateMainViewUi();
        if (_gamepadNavigation.ActiveZone is GamepadNavigationZone.ModsOverlayToolbar or GamepadNavigationZone.ModsOverlayFilters or GamepadNavigationZone.ModsOverlaySourceFilters or GamepadNavigationZone.ModsOverlayList or GamepadNavigationZone.ModsOverlayRowActions)
        {
            _gamepadNavigation.ActiveZone = GamepadNavigationZone.Library;
            if (IsGamepadFocusActive)
                SelectInitialLibraryGamepadItem();
        }

        OnPropertyChanged(nameof(GamepadHintsVisible));
    }

    internal void ModsClose_Click(object? sender, RoutedEventArgs e) => CloseModsOverlay();
    internal async void ModsRefresh_Click(object? sender, RoutedEventArgs e) => await Workspace.RefreshAsync(forceRefresh: true);
    internal async void ModsUpdateAll_Click(object? sender, RoutedEventArgs e) => await Actions.UpdateAllVisibleModsAsync();
    internal void ModsOpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        if (Model.Game == null)
            return;
        try
        {
            var installRoot = Model.Game.GetInstallPath(_gameManager.GamesFolder);
            var modsPath = GameModsConfig.NormalizePath(Model.Game.ModsPath);
            if (string.IsNullOrWhiteSpace(installRoot) || modsPath.Length == 0)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("Mods folder is not configured for this app.", "Mods"));
                return;
            }

            var modsDir = ModInstaller.GetModsDirectory(installRoot, modsPath);
            Directory.CreateDirectory(modsDir);
            OpenUrl(modsDir);
        }
        catch (Exception ex)
        {
            _ = _session.RunAsync(() => ShowMessageBoxAsync($"Failed to open mod folder: {ex.Message}", "Mods"));
        }
    }

    internal void ModsTabFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        if (sender is not Button button || button.Tag is not string tab)
            return;
        Model.Tab = tab;
        UpdateModsTabButtons();
        // Orphan install inclusion depends on Browse+search vs Installed — rebuild rows.
        Workspace.SyncModListItemsFromCatalog();
        Workspace.ApplyFilters();
    }

    internal void ModsSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_context is null)
            return;
        Model.SearchText = ModsSearchTextBox?.Text?.Trim() ?? string.Empty;
        _ = _session.RunAsync(() => Workspace.SearchAsync());
    }

    internal async void ModsSortByComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_context is null)
            return;
        await _session.RunAsync(async () =>
        {
            if (_context is null)
                return;
            if (ModsSortByComboBox?.SelectedItem is not ComboBoxItem item || item.Tag is not string sortMode)
                return;
            var normalized = ModListSorter.Normalize(sortMode);
            if (string.Equals(Model.SortBy, normalized, StringComparison.OrdinalIgnoreCase))
                return;
            Model.SortBy = normalized;
            _settings.ModsSortBy = normalized;
            _settingsViewModel.Save(_settings);
            if (ModListSorter.IsRemoteSort(normalized))
                await Workspace.RefreshAsync(forceRefresh: false).ConfigureAwait(true);
            else
                Workspace.ApplyFilters();
        });
    }

    internal void ApplyModsSortSelection(string? sortBy)
    {
        Model.SortBy = ModListSorter.Normalize(sortBy);
        _settings.ModsSortBy = Model.SortBy;
        if (ModsSortByComboBox == null)
            return;
        foreach (var entry in ModsSortByComboBox.Items)
        {
            if (entry is ComboBoxItem item && item.Tag is string tag && string.Equals(tag, Model.SortBy, StringComparison.OrdinalIgnoreCase))
            {
                ModsSortByComboBox.SelectedItem = item;
                return;
            }
        }

        if (ModsSortByComboBox.Items.Count > 0)
            ModsSortByComboBox.SelectedIndex = 0;
    }

    internal async void ModsSourceFilter_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        await _session.RunAsync(async () =>
        {
            if (_context is null)
                return;
            if (sender is not Button button)
                return;
            var tag = button.Tag as string;
            if (tag is "nsfw")
            {
                Model.IncludeNsfw = !Model.IncludeNsfw;
                _settings.ModsIncludeNsfw = Model.IncludeNsfw;
                _settingsViewModel.Save(_settings);
                UpdateModsSourceFilterButtons();
                await Workspace.RefreshAsync(forceRefresh: false).ConfigureAwait(true);
                return;
            }

            Model.SourceFilterKey = tag; // null tag = All
            if (tag is { Length: 0 })
                Model.SourceFilterKey = null;
            UpdateModsSourceFilterButtons();
            // Source filter changes reset GameBanana paging.
            await Workspace.RefreshAsync(forceRefresh: false).ConfigureAwait(true);
        });
    }

    internal async void ModsListScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_context is null)
            return;
        await _session.RunAsync(async () =>
        {
            if (_context is null)
                return;
            if (!Workspace.CanLoadMore || !IsModsListNearBottomForPrefetch())
                return;
            await LoadMoreModsAsync().ConfigureAwait(true);
        });
    }

    /// <summary>
    /// True when the mods list is scrolled near the bottom, or content does not overflow the
    /// viewport (so mouse users can still page when the last incomplete row fills the view).
    /// </summary>
    internal bool IsModsListNearBottomForPrefetch()
    {
        if (ModsListScrollViewer == null)
            return true;
        var extent = ModsListScrollViewer.Extent.Height;
        var viewport = ModsListScrollViewer.Viewport.Height;
        var offset = ModsListScrollViewer.Offset.Y;
        if (extent <= viewport)
            return true;
        return offset + viewport >= extent - 120;
    }

    internal bool ShouldPrefetchMoreModsForGamepad(int focusedIndex) => Workspace.CanLoadMore && ModListRows.Count > 0 && (focusedIndex >= Math.Max(0, ModListRows.Count - 6) || IsModsListNearBottomForPrefetch());
    internal async void ModRowInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        await _session.RunAsync(async () =>
        {
            if (_context is null)
                return;
            if (GetModListItem(sender)is not { } item)
                return;
            await Actions.InstallModAsync(item, updateInstalledFilesOnly: false);
        });
    }

    internal async void ModRowUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        await _session.RunAsync(async () =>
        {
            if (_context is null)
                return;
            if (GetModListItem(sender)is not { } item)
                return;
            await Actions.InstallModAsync(item, updateInstalledFilesOnly: true);
        });
    }

    internal async void ModRowUninstall_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        await _session.RunAsync(async () =>
        {
            if (_context is null)
                return;
            if (GetModListItem(sender)is not { } item)
                return;
            await Actions.UninstallModAsync(item);
        });
    }

    internal void ModRowOpenPage_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        if (GetModListItem(sender)is not { } item)
            return;
        var url = item.Package.PackagePageUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            UrlLauncher.Open(url);
        }
        catch (Exception ex)
        {
            _ = _session.RunAsync(() => ShowMessageBoxAsync($"Could not open page: {ex.Message}", "Mods"));
        }
    }

    internal static ModListItem? GetModListItem(object? sender)
    {
        if (sender is Button button && button.CommandParameter is ModListItem fromCommand)
            return fromCommand;
        if (sender is MenuItem menuItem && menuItem.CommandParameter is ModListItem fromMenu)
            return fromMenu;
        if (sender is Control control && control.DataContext is ModListItem fromContext)
            return fromContext;
        return null;
    }

    internal async Task LoadMoreModsAsync()
    {
        while (Workspace.CanLoadMore)
        {
            var previous = Model.BrowseSession;
            await Workspace.LoadMoreAsync();
            if (ReferenceEquals(previous, Model.BrowseSession) || !IsModsListNearBottomForPrefetch())
                break;
        }
    }

    internal void UpdateModsListPlaceholder()
    {
        var showLoading = ModsCatalogWorkspace.ShouldShowModsListLoading(Model.ListIsLoading, ModListRows.Count);
        if (ModsListLoadingPanel != null)
            ModsListLoadingPanel.IsVisible = showLoading;
        if (ModsEmptyText != null)
            ModsEmptyText.IsVisible = !showLoading && ModListRows.Count == 0;
    }

    private bool _restoreListFocus;
    private ModPackage? _focusedPackage;
    private int _focusFallbackIndex;
    private void CaptureListFocus()
    {
        _restoreListFocus = IsGamepadFocusActive && _gamepadNavigation.ActiveZone is GamepadNavigationZone.ModsOverlayList or GamepadNavigationZone.ModsOverlayRowActions;
        _focusedPackage = null;
        _focusFallbackIndex = Navigation._modsGamepadListIndex;
        if (_restoreListFocus)
        {
            var focusedRow = ModListRows.FirstOrDefault(r => r.IsGamepadFocused);
            if (focusedRow != null)
                _focusedPackage = focusedRow.Package;
            else if (_focusFallbackIndex >= 0 && _focusFallbackIndex < ModListRows.Count)
                _focusedPackage = ModListRows[_focusFallbackIndex].Package;
        }
    }

    private void RestoreListFocus()
    {
        UpdateModsListPlaceholder();
        if (ModsUpdateAllButton != null)
            ModsUpdateAllButton.IsEnabled = Model.Rows.Any(r => r.CanUpdate);
        if (!_restoreListFocus || ModListRows.Count == 0)
            return;
        var index = ModCatalogListBuilder.FindListIndexByPackage(ModListRows, _focusedPackage, _focusFallbackIndex);
        if (index >= 0)
            Navigation.ApplyModsListSelection(index);
    }

    internal void BuildModsSourceFilterButtons()
    {
        if (ModsSourceFilterPanel == null || Model.Game == null)
            return;
        ModsSourceFilterPanel.Children.Clear();
        var allButton = CreateModsFilterButton("All", tag: "");
        allButton.Classes.Set("selected", Model.SourceFilterKey == null);
        ModsSourceFilterPanel.Children.Add(allButton);
        var resolved = ModCatalog.ResolveSources(Model.Game.ModsSources);
        foreach (var(_, parsed, _)in resolved)
        {
            var tag = $"{parsed.ProviderId}|{parsed.SourceKey}";
            var button = CreateModsFilterButton(parsed.DisplayLabel, tag);
            button.Classes.Set("selected", string.Equals(Model.SourceFilterKey, tag, StringComparison.OrdinalIgnoreCase));
            ModsSourceFilterPanel.Children.Add(button);
        }

        var nsfwButton = CreateModsFilterButton("Include NSFW", tag: "nsfw");
        nsfwButton.Classes.Set("selected", Model.IncludeNsfw);
        ModsSourceFilterPanel.Children.Add(nsfwButton);
    }

    internal Button CreateModsFilterButton(string content, string tag)
    {
        var button = new Button
        {
            Classes =
            {
                "catalog-filter"
            },
            Content = content,
            Tag = tag,
            FontSize = 11,
            Margin = new Thickness(0, 0, 6, 6),
        };
        button.Click += ModsSourceFilter_Click;
        return button;
    }

    internal void UpdateModsSourceFilterButtons()
    {
        if (ModsSourceFilterPanel == null)
            return;
        foreach (var child in ModsSourceFilterPanel.Children.OfType<Button>())
        {
            var tag = child.Tag as string ?? "";
            bool selected;
            if (tag == "nsfw")
                selected = Model.IncludeNsfw;
            else if (string.IsNullOrEmpty(tag))
                selected = Model.SourceFilterKey == null;
            else
                selected = string.Equals(Model.SourceFilterKey, tag, StringComparison.OrdinalIgnoreCase);
            child.Classes.Set("selected", selected);
        }
    }

    internal void UpdateModsTabButtons()
    {
        ModsTabBrowseButton?.Classes.Set("selected", Model.Tab == "Browse");
        ModsTabInstalledButton?.Classes.Set("selected", Model.Tab == "Installed");
    }

    internal void SetModsStatus(string text)
    {
        Model.Status = text;
    }

    internal async Task<IReadOnlyList<ModDownloadFile>> ShowModDownloadFilePickerAsync(string title, string prompt, string confirmLabel, IReadOnlyList<ModDownloadFile> files, IReadOnlyCollection<string>? preselectedFileIds)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
                return (IReadOnlyList<ModDownloadFile>)[];
            if (_session.IsClosed)
                return (IReadOnlyList<ModDownloadFile>)[];
            IReadOnlyList<ModDownloadFile> chosen = [];
            var listBox = new ListBox
            {
                MinHeight = 200,
                MaxHeight = 360,
                Focusable = true,
                SelectionMode = SelectionMode.Multiple,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            var checkBoxes = new List<CheckBox>();
            foreach (var file in files)
            {
                var sizeMb = file.FileSize > 0 ? $"{file.FileSize / (1024d * 1024d):0.##} MB" : "Unknown size";
                var desc = string.IsNullOrWhiteSpace(file.Description) ? "" : $" — {file.Description}";
                var isPreselected = preselectedFileIds != null && preselectedFileIds.Contains(file.Id, StringComparer.OrdinalIgnoreCase);
                var checkBox = new CheckBox
                {
                    Content = new TextBlock
                    {
                        Text = $"{file.FileName} ({sizeMb}){desc}",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    IsChecked = isPreselected,
                    Tag = file,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                };
                checkBoxes.Add(checkBox);
                var item = new ListBoxItem
                {
                    Content = checkBox,
                    Tag = file,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                };
                listBox.Items.Add(item);
                if (isPreselected)
                    listBox.SelectedItems?.Add(item);
            }

            if (listBox.SelectedItem == null && listBox.Items.Count > 0)
                listBox.SelectedIndex = 0;
            var confirmButton = new Button
            {
                Content = confirmLabel,
                MinWidth = 100,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true,
            };
            var cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 100,
                IsCancel = true,
            };
            void RefreshConfirmEnabled() => confirmButton.IsEnabled = checkBoxes.Any(c => c.IsChecked == true);
            foreach (var checkBox in checkBoxes)
                checkBox.IsCheckedChanged += (_, _) => RefreshConfirmEnabled();
            RefreshConfirmEnabled();
            var dialog = new Window
            {
                Title = title,
                Width = 720,
                MinWidth = 560,
                Height = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = prompt,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        listBox,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                            {
                                confirmButton,
                                cancelButton
                            },
                        },
                    },
                },
            };
            confirmButton.Click += (_, _) =>
            {
                chosen = checkBoxes.Where(c => c.IsChecked == true && c.Tag is ModDownloadFile).Select(c => (ModDownloadFile)c.Tag!).ToList();
                dialog.Close();
            };
            cancelButton.Click += (_, _) => dialog.Close();
            using var cancellation = _session.Token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
            if (_session.IsClosed)
                return (IReadOnlyList<ModDownloadFile>)[];
            GamepadModalDialogNavigation.Attach(dialog);
            DesktopInterfaceScaling.PrepareDialog(dialog, desktop.MainWindow);
            await dialog.ShowDialog(desktop.MainWindow);
            return chosen;
        });
    }

    internal async void ModRowDetails_Click(object? sender, RoutedEventArgs e)
    {
        if (_context is null)
            return;
        await _session.RunAsync(async () =>
        {
            if (_context is null)
                return;
            if (GetModListItem(sender)is not { } item)
                return;
            await Details.OpenAsync(item);
        });
    }
}
