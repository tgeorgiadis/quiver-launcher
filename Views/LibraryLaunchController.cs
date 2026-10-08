using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;
/// <summary>Owns the launch, version and asset selection interaction for library apps.</summary>
public sealed class LibraryLaunchController
{
    private readonly GameManager _gameManager;
    private readonly SettingsViewModel _settingsModel;
    private AppSettings _settings => _settingsModel.Current;

    private readonly LauncherSession _session;
    private readonly LibraryPersistenceService _persistence;
    private readonly Func<GameInfo, Control?, Control?> _resolveAnchor;
    private readonly Action<Control, ContextMenu> _openMenu;
    private readonly Func<string, string, Task> _message;
    private readonly Action<GameInfo> _openFolder;
    private readonly Action _changed;
    private readonly Action<bool> _closeAfterLaunch;
    private readonly Func<double> _width;
    public Action? FocusRestorationRequested { get; set; }

    private void RestoreFocus()
    {
        if (!_session.IsClosed)
            FocusRestorationRequested?.Invoke();
    }

    public Task PerformSelectedGameActionAsync(GameInfo game) => _session.RunAsync(() => PerformSelectedGameActionCoreAsync(game));
    public Task ContinueAsync() => _session.RunAsync(async () =>
    {
        var game = _gameManager.GetLatestPlayedInstalledGame();
        if (game == null)
        {
            await ShowMessageBoxAsync("No installed apps found to continue.", "No App Found");
            return;
        }

        var launched = false;
        try
        {
            launched = await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
            Changed();
        }
        catch (Exception ex)
        {
            await ShowMessageBoxAsync($"Failed to launch {game.Name}: {ex.Message}", "Launch Error");
        }

        if (!_session.IsClosed)
            _closeAfterLaunch(launched);
    });
    public LibraryLaunchController(GameManager manager, SettingsViewModel settings, LauncherSession session, LibraryPersistenceService persistence, Func<GameInfo, Control?, Control?> resolveAnchor, Action<Control, ContextMenu> openMenu, Func<string, string, Task> message, Action<GameInfo> openFolder, Action changed, Action<bool> closeAfterLaunch, Func<double> width)
    {
        _gameManager = manager;
        _settingsModel = settings;
        _session = session;
        _persistence = persistence;
        _resolveAnchor = (game, anchor) => session.IsClosed ? null : resolveAnchor(game, anchor);
        _openMenu = openMenu;
        _message = message;
        _openFolder = openFolder;
        _changed = changed;
        _closeAfterLaunch = launched =>
        {
            if (!session.IsClosed)
                closeAfterLaunch(launched);
        };
        _width = width;
    }

    public Task PerformGamePrimaryActionAsync(GameInfo game, Control anchor) => _session.RunAsync(() => PerformGamePrimaryActionCoreAsync(game, anchor));
    public Task<bool> HandleUpdateNowAsync(Control anchor, GameInfo game, bool preferAutoPlatform = false, bool allowAssetPicker = true, bool interactive = true) => _session.RunAsync(() => HandleUpdateNowCoreAsync(anchor, game, preferAutoPlatform, allowAssetPicker, interactive));
    public Task HandleSkipUpdateAsync(GameInfo game) => _session.RunAsync(() => HandleSkipUpdateCoreAsync(game));
    public Task HandleChangeVersionAsync(Control anchor, GameInfo game) => _session.RunAsync(() => HandleChangeVersionCoreAsync(anchor, game));
    /// <summary>
    /// Force Update: downloads the app's release again and installs it over the installed one. Nothing changes until the
    /// new files are in, so a cancelled or failed download leaves the app as it was.
    /// </summary>
    public Task HandleReinstallAsync(Control anchor, GameInfo game) => _session.RunAsync(async () =>
    {
        await _persistence.SaveVersionPreferencesAsync(game, null, null);
        await HandleUpdateNowCoreAsync(anchor, game, reinstall: true);
    });
    /// <summary>Installs the newer release Quiver hasn't verified yet, as picking it in Change Version does (it asks first).</summary>
    public Task HandleInstallUnverifiedAsync(Control anchor, GameInfo game) => _session.RunAsync(() => HandleInstallUnverifiedCoreAsync(anchor, game));
    public Task ShowReleaseDownloadSelectionMenuAsync(Control anchor, GameInfo game, GitHubRelease release, string? preferredVersion, string? skippedUpdateVersion) => _session.RunAsync(() => ShowReleaseDownloadSelectionMenuCoreAsync(anchor, game, release, preferredVersion, skippedUpdateVersion));
    private void OpenContextMenu(Control anchor, ContextMenu menu)
    {
        if (!_session.IsClosed)
            _openMenu(anchor, menu);
    }

    private Task ShowMessageBoxAsync(string message, string title) => _session.IsClosed ? Task.CompletedTask : _message(message, title);
    private void Changed()
    {
        if (!_session.IsClosed)
            _changed();
    }

    private async Task PerformGamePrimaryActionCoreAsync(GameInfo game, Control anchor)
    {
        using var priority = ReleaseRequestCoordinator.PrioritizeInteractiveChecks();
        var launched = false;
        try
        {
            if (game.IsManuallyManaged && game.Status == GameStatus.NotInstalled)
            {
                _openFolder(game);
                return;
            }

            if (game.Status == GameStatus.UpdateAvailable)
            {
                ShowUpdateActionMenu(_resolveAnchor(game, anchor) ?? anchor, game);
                return;
            }

            if (game.Status == GameStatus.NotInstalled)
                game.ClearDownloadSelection();
            launched = await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
            // Re-resolve after async: the action button can be recycled when Status
            // briefly becomes Downloading, which breaks Gamescope popup parenting.
            var resolvedAnchor = _resolveAnchor(game, anchor);
            if (resolvedAnchor != null && TryShowPendingSelectionMenus(resolvedAnchor, game))
                return;
            // Installing successfully returns false: it did not perform a launch.
            // Launch services report actual failures by throwing; do not infer one
            // from the card's status changing to Installed during this action.
        }
        catch (Exception ex)
        {
            var title = game.Status == GameStatus.Installed ? "Launch Error" : "Action Error";
            await ShowMessageBoxAsync($"Failed to perform action for {game.Name}: {ex.Message}", title);
        }

        Changed();
        _closeAfterLaunch(launched);
    }

    public bool TryShowPendingSelectionMenus(Control anchor, GameInfo game)
    {
        if ((game.Status == GameStatus.NotInstalled || game.Status == GameStatus.UpdateAvailable ||
             game.IsWindowsInstaller && game.AvailableDownloads != null) && game.HasMultipleDownloads && game.SelectedDownload == null)
        {
            ShowDownloadSelectionMenu(anchor, game);
            return true;
        }

        if (game.Status == GameStatus.Installed && game.HasMultipleExecutables && string.IsNullOrEmpty(game.SelectedExecutable))
        {
            ShowExecutableSelectionMenu(anchor, game);
            return true;
        }

        return false;
    }

    public void ShowUpdateActionMenu(Control anchor, GameInfo game)
    {
        game.PrefetchCatalogReleases();
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem { Header = $"Update options for {game.Name}:", IsEnabled = false, FontWeight = FontWeight.Bold });
        contextMenu.Items.Add(new Separator());
        var updateNowItem = new MenuItem
        {
            Header = "Update Now"
        };
        updateNowItem.Click += async (_, _) =>
        {
            await _session.RunAsync(async () =>
            {
                await HandleUpdateNowAsync(anchor, game);
            });
        };
        contextMenu.Items.Add(updateNowItem);
        var skipItem = new MenuItem
        {
            Header = "Skip Update"
        };
        skipItem.Click += async (_, _) =>
        {
            await _session.RunAsync(async () =>
            {
                await HandleSkipUpdateAsync(game);
            });
        };
        contextMenu.Items.Add(skipItem);
        if (game.ShowUnverifiedRelease && game.CanChangeVersion)
        {
            var unverifiedItem = new MenuItem { Header = game.InstallUnverifiedReleaseLabel };
            unverifiedItem.Click += async (_, _) => await HandleInstallUnverifiedAsync(anchor, game);
            contextMenu.Items.Add(unverifiedItem);
        }
        var changeVersionItem = new MenuItem
        {
            Header = "Change Version"
        };
        changeVersionItem.Click += async (_, _) =>
        {
            await _session.RunAsync(async () =>
            {
                await HandleChangeVersionAsync(anchor, game);
            });
        };
        if (!game.IsFlatpak) contextMenu.Items.Add(changeVersionItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(new MenuItem { Header = "Cancel" });
        OpenContextMenu(anchor, contextMenu);
    }

    public void ShowDownloadSelectionMenu(Control anchor, GameInfo game)
    {
        var release = game.GetLatestRelease() ?? new GitHubRelease { assets = game.AvailableDownloads?.ToArray() ?? [] };
        var choices = GameDownloadService.Prepare(game, release, _settings);
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem { Header = choices.EmptyReason ?? "Select download file:", IsEnabled = false });
        foreach (var item in CreateDownloadChoices(choices, async asset =>
        {
            GameDownloadService.SelectExplicit(game, release, _settings, asset);
            await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
        })) contextMenu.Items.Add(item);

        contextMenu.Items.Add(new Separator());
        // Add cancel option
        var cancelItem = new MenuItem
        {
            Header = "Cancel"
        };
        cancelItem.Click += (s, e) =>
        {
            game.ClearDownloadSelection();
        };
        contextMenu.Items.Add(cancelItem);
        ApplyDownloadSelectionMenuWidth(contextMenu);
        OpenContextMenu(anchor, contextMenu);
    }

    public void ApplyDownloadSelectionMenuWidth(ContextMenu contextMenu)
    {
        var availableWidth = _width() > 0 ? _width() - 80 : 720;
        var minWidth = Math.Min(720, availableWidth);
        contextMenu.MinWidth = minWidth;
        contextMenu.MaxWidth = Math.Max(minWidth, availableWidth);
    }

    public static Grid CreateDownloadAssetMenuHeader(string displayName, string? iconPath)
    {
        var contentGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        if (!string.IsNullOrEmpty(iconPath))
        {
            var icon = new Avalonia.Controls.Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri(iconPath))),
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);
            contentGrid.Children.Add(icon);
        }

        var textBlock = new TextBlock
        {
            Text = displayName,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(textBlock, 1);
        contentGrid.Children.Add(textBlock);
        return contentGrid;
    }

    /// <returns>True when an install was started/completed; false when cancelled or deferred to a picker.</returns>
    private async Task<bool> HandleUpdateNowCoreAsync(Control anchor, GameInfo game, bool preferAutoPlatform = false, bool allowAssetPicker = true, bool interactive = true,
        bool reinstall = false)
    {
        using var priority = interactive ? ReleaseRequestCoordinator.PrioritizeInteractiveChecks() : null;
        using var revalidate = ReleaseRequestCoordinator.AllowCachedMetadata(TimeSpan.Zero);
        try
        {
            game.IsLoading = true;
            await _gameManager.CatalogReleases.RefreshAsync([game], _session.Token);
            var releaseResult = await game.FetchReleasesAsync(_gameManager.HttpClient, _session.Token);
            _session.Token.ThrowIfCancellationRequested();
            var latestRelease = GameInfo.SelectLatestRelease(releaseResult.Releases, game.ReleaseTarget, game.InstalledVersion, releaseResult.LatestTag);
            if (latestRelease == null)
            {
                if (allowAssetPicker)
                    await ShowMessageBoxAsync($"No downloadable releases were found for {game.Name}.", "No Releases");
                return false;
            }

            game.ApplyCachedRelease(latestRelease.tag_name, latestRelease);
            if (!string.IsNullOrWhiteSpace(game.Repository))
            {
                GitHubApiCache.SetCache(game.RepositorySource, game.Repository, latestRelease.tag_name, GitHubApiCache.GetETag(game.RepositorySource, game.Repository), latestRelease);
            }

            game.RefreshInstalledStatus();
            if (!reinstall && game.TryAcknowledgeAlreadyInstalledRelease(latestRelease.tag_name))
                return false;
            var choices = GameDownloadService.Prepare(game, latestRelease, _settings);
            if (choices.Automatic is { } automatic)
            {
                // Install the release just revalidated above. Resolving again after
                // applying it can lose the pin that selected this exact release.
                await game.InstallReleaseAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings, latestRelease, automatic, automatic: !interactive);
                await _persistence.SaveVersionPreferencesAsync(game, game.PreferredVersion, null);
                Changed();
                return true;
            }

            if (!allowAssetPicker)
                return false;
            await ShowReleaseDownloadSelectionMenuAsync(_resolveAnchor(game, anchor) ?? anchor, game, latestRelease, game.PreferredVersion, null);
            Changed();
            return false;
        }
        catch (Exception ex)
        {
            if (allowAssetPicker)
                await ShowMessageBoxAsync($"Failed to update {game.Name}: {ex.Message}", "Update Error");
            else
                throw;
            return false;
        }
        finally
        {
            game.IsLoading = false;
        }
    }

    private async Task HandleSkipUpdateCoreAsync(GameInfo game)
    {
        game.SkipLatestUpdate();
        await _persistence.SaveVersionPreferencesAsync(game, game.PreferredVersion, game.SkippedUpdateVersion);
        Changed();
    }

    private async Task HandleInstallUnverifiedCoreAsync(Control anchor, GameInfo game)
    {
        using var priority = ReleaseRequestCoordinator.PrioritizeInteractiveChecks();
        if (game.CatalogUnverifiedVersion is not { Length: > 0 } version) return;
        GitHubRelease? release;
        try
        {
            game.IsLoading = true;
            var releaseResult = await game.FetchReleasesAsync(_gameManager.HttpClient, _session.Token);
            _session.Token.ThrowIfCancellationRequested();
            release = releaseResult.Releases.FirstOrDefault(r => ReleaseVersionIdentity.AreVersionsEquivalent(r.tag_name, version));
        }
        catch (Exception ex)
        {
            await ShowMessageBoxAsync($"Failed to load {version} of {game.Name}: {ex.Message}", "Install Error");
            return;
        }
        finally
        {
            game.IsLoading = false;
        }
        if (release == null)
        {
            await ShowMessageBoxAsync($"{version} of {game.Name} has no downloads yet. Try Change Version.", "Not Available");
            return;
        }
        var acknowledgedVersion = string.IsNullOrWhiteSpace(game.LatestVersion) ? release.tag_name : game.LatestVersion;
        await ShowReleaseDownloadSelectionMenuAsync(_resolveAnchor(game, anchor) ?? anchor, game, release, release.tag_name, acknowledgedVersion);
    }

    /// <summary>
    /// Change Version: the menu opens at once, with the releases read earlier this session (its card menu starts reading
    /// them) or "Fetching releases…", and fills in as soon as the app's current releases arrive.
    /// </summary>
    private async Task HandleChangeVersionCoreAsync(Control anchor, GameInfo game)
    {
        using var priority = ReleaseRequestCoordinator.PrioritizeInteractiveChecks();
        if (game.IsFlatpak) return;
        anchor = _resolveAnchor(game, anchor) ?? anchor;
        var contextMenu = new ContextMenu();
        if (_gameManager.CatalogReleases.Cached(game) is { } cached)
            FillVersionMenu(contextMenu, anchor, game, cached.Releases, cached.States);
        else
            FillVersionMenuNote(contextMenu, game, "Fetching releases…");
        OpenContextMenu(anchor, contextMenu);
        try
        {
            game.IsLoading = true;
            var releaseResult = await game.FetchReleasesAsync(_gameManager.HttpClient, _session.Token);
            _session.Token.ThrowIfCancellationRequested();
            if (releaseResult.Releases.Count == 0)
            {
                if (contextMenu.IsOpen) RefillVersionMenu(contextMenu, () => FillVersionMenuNote(contextMenu, game, "No downloadable releases were found."));
                return;
            }

            // Mark which releases Quiver verified, so the player can pick one it checked.
            var states = new Dictionary<string, ReleaseCheckState>(StringComparer.OrdinalIgnoreCase);
            foreach (var release in releaseResult.Releases)
                if (await _gameManager.CatalogReleases.CheckAsync(game, release.tag_name, _session.Token) is { } check)
                    states[release.tag_name] = check.State;
            _session.Token.ThrowIfCancellationRequested();
            // Closed meanwhile: the player didn't want it after all.
            if (contextMenu.IsOpen)
                RefillVersionMenu(contextMenu, () => FillVersionMenu(contextMenu, anchor, game, releaseResult.Releases, states));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (contextMenu.IsOpen)
                RefillVersionMenu(contextMenu, () => FillVersionMenuNote(contextMenu, game, $"Couldn't load versions: {ex.Message}"));
            else
                await ShowMessageBoxAsync($"Failed to load versions for {game.Name}: {ex.Message}", "Version Selection Error");
        }
        finally
        {
            game.IsLoading = false;
        }
    }

    // Replaces an open menu's items; the controller then moves through the new ones.
    private static void RefillVersionMenu(ContextMenu menu, Action fill)
    {
        fill();
        GamepadContextMenuNavigation.Instance.RefreshContextMenu(menu);
    }

    private static void FillVersionMenuNote(ContextMenu menu, GameInfo game, string note)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = $"Choose a version for {game.Name}:", IsEnabled = false, FontWeight = FontWeight.Bold });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = note, IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Cancel" });
    }

    public void ShowVersionSelectionMenu(Control anchor, GameInfo game, IReadOnlyList<GitHubRelease> releases,
        IReadOnlyDictionary<string, ReleaseCheckState>? states = null)
    {
        if (game.IsFlatpak) return;
        var contextMenu = new ContextMenu();
        FillVersionMenu(contextMenu, anchor, game, releases, states);
        OpenContextMenu(anchor, contextMenu);
    }

    private void FillVersionMenu(ContextMenu contextMenu, Control anchor, GameInfo game, IReadOnlyList<GitHubRelease> releases,
        IReadOnlyDictionary<string, ReleaseCheckState>? states)
    {
        contextMenu.Items.Clear();
        contextMenu.Items.Add(new MenuItem { Header = $"Choose a version for {game.Name}:", IsEnabled = false, FontWeight = FontWeight.Bold });
        contextMenu.Items.Add(new Separator());
        foreach (var release in releases)
        {
            var tags = new List<string>();
            if (states != null && states.TryGetValue(release.tag_name, out var state))
                tags.Add(state.ToString());
            if (!string.IsNullOrWhiteSpace(game.LatestVersion) && release.tag_name.Equals(game.LatestVersion, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add("Latest");
            }

            if (!string.IsNullOrWhiteSpace(game.InstalledVersion) && release.tag_name.Equals(game.InstalledVersion, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add("Installed");
            }

            if (!string.IsNullOrWhiteSpace(game.PreferredVersion) && release.tag_name.Equals(game.PreferredVersion, StringComparison.OrdinalIgnoreCase))
            {
                tags.Add("Preferred");
            }

            if (release.prerelease)
            {
                tags.Add("Pre-release");
            }

            var header = release.tag_name;
            if (tags.Count > 0)
            {
                header += $" ({string.Join(", ", tags)})";
            }

            var versionItem = new MenuItem
            {
                Header = header
            };
            versionItem.Click += (_, _) =>
            {
                var acknowledgedVersion = string.IsNullOrWhiteSpace(game.LatestVersion) ? release.tag_name : game.LatestVersion;
                ShowReleaseDownloadSelectionMenu(anchor, game, release, release.tag_name, acknowledgedVersion);
            };
            if (!string.IsNullOrWhiteSpace(game.LatestVersion) && release.tag_name.Equals(game.LatestVersion, StringComparison.OrdinalIgnoreCase))
            {
                versionItem.Classes.Add("accent");
            }

            contextMenu.Items.Add(versionItem);
        }

        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(new MenuItem { Header = "Cancel" });
    }

    public void ShowReleaseDownloadSelectionMenu(Control anchor, GameInfo game, GitHubRelease release, string? preferredVersion, string? skippedUpdateVersion) => _ = _session.RunAsync(() => ShowReleaseDownloadSelectionMenuAsync(anchor, game, release, preferredVersion, skippedUpdateVersion));
    private Task ShowReleaseDownloadSelectionMenuCoreAsync(Control anchor, GameInfo game, GitHubRelease release, string? preferredVersion, string? skippedUpdateVersion)
    {
        var choices = GameDownloadService.Prepare(game, release, _settings);
        if (choices.Automatic is { } automatic)
            return InstallChosenReleaseAsync(game, release, automatic, preferredVersion, skippedUpdateVersion);
        if (!choices.NeedsChoice)
            return ShowMessageBoxAsync(choices.EmptyReason ?? "No eligible downloads.", "No Matching Assets");

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem { Header = $"Choose a download for {release.tag_name}:", IsEnabled = false, FontWeight = FontWeight.Bold });
        contextMenu.Items.Add(new Separator());
        if (choices.EmptyReason != null)
            contextMenu.Items.Add(new MenuItem { Header = choices.EmptyReason, IsEnabled = false });
        foreach (var item in CreateDownloadChoices(choices, asset =>
            InstallChosenReleaseAsync(game, release, asset, preferredVersion, skippedUpdateVersion)))
            contextMenu.Items.Add(item);

        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(new MenuItem { Header = "Cancel" });
        ApplyDownloadSelectionMenuWidth(contextMenu);
        void OnClosed(object? sender, EventArgs e)
        {
            contextMenu.Closed -= OnClosed;
            tcs.TrySetResult();
        }

        var cancellation = _session.Token.Register(() => Dispatcher.UIThread.Post(() =>
        {
            contextMenu.Close();
            tcs.TrySetResult();
        }));
        contextMenu.Closed += (_, _) => cancellation.Dispose();
        contextMenu.Closed += OnClosed;
        OpenContextMenu(anchor, contextMenu);
        return tcs.Task;
    }

    private async Task InstallChosenReleaseAsync(GameInfo game, GitHubRelease release, GitHubAsset asset,
        string? preferredVersion, string? skippedUpdateVersion)
    {
        var latestBefore = game.LatestVersion;
        await game.InstallReleaseAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings, release, asset);
        // Saying no to an unverified or blocked release (or a download that failed) leaves the app as it was. Pinning it to
        // that release anyway left it showing an update it would never install, until Skip Update undid the pin.
        if (!ReleaseVersionIdentity.AreVersionsEquivalent(game.InstalledVersion, release.tag_name))
        {
            game.LatestVersion = latestBefore;
            game.RefreshInstalledStatus();
            Changed();
            return;
        }
        if (!string.IsNullOrWhiteSpace(skippedUpdateVersion)) game.LatestVersion = skippedUpdateVersion;
        await _persistence.SaveVersionPreferencesAsync(game, preferredVersion, skippedUpdateVersion);
        Changed();
    }

    internal IEnumerable<MenuItem> CreateDownloadChoices(DownloadAssetSelection choices, Func<GitHubAsset, Task> install)
    {
        MenuItem Create(GitHubAsset asset, bool uncertain)
        {
            var native = GameInfo.MatchesPlatform(asset.name, GameInfo.GetPlatformIdentifier(_settings));
            var item = new MenuItem
            {
                Header = CreateDownloadAssetMenuHeader(asset.name + (uncertain ? " (Unrecognized platform)" : native ? " (Recommended)" : ""), GameInfo.GetPlatformIcon(asset.name)),
                Tag = asset,
            };
            if (native && !uncertain) item.Classes.Add("accent");
            item.Click += async (_, _) => await _session.RunAsync(async () =>
            {
                try { await install(asset); }
                catch (Exception ex) { await ShowMessageBoxAsync($"Failed to download: {ex.Message}", "Download Error"); }
            });
            return item;
        }
        foreach (var asset in choices.Eligible) yield return Create(asset, false);
        if (choices.Uncertain.Count > 0)
        {
            var other = new MenuItem { Header = "Show other downloads" };
            foreach (var asset in choices.Uncertain) other.Items.Add(Create(asset, true));
            yield return other;
        }
    }

    public void ShowExecutableSelectionMenu(Control anchor, GameInfo game)
    {
        if (game.AvailableExecutables == null || game.AvailableExecutables.Count == 0)
            return;
        var contextMenu = new ContextMenu();
        // Add header
        var headerItem = new MenuItem
        {
            Header = "Select executable to launch:",
            IsEnabled = false,
            FontWeight = FontWeight.Bold
        };
        contextMenu.Items.Add(headerItem);
        contextMenu.Items.Add(new Separator());
        // Add executable options
        foreach (var exe in game.AvailableExecutables)
        {
            var displayName = Path.GetFileName(exe);
            var menuItem = new MenuItem
            {
                Header = displayName,
                Tag = exe
            };
            menuItem.Click += async (s, e) =>
            {
                await _session.RunAsync(async () =>
                {
                    var selectedExe = (s as MenuItem)?.Tag as string;
                    game.SelectedExecutable = selectedExe;
                    // Save the selection
                    if (!string.IsNullOrEmpty(selectedExe))
                    {
                        game.SaveSelectedExecutable(selectedExe, _gameManager.GamesFolder);
                    }

                    try
                    {
                        await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
                    }
                    catch (Exception ex)
                    {
                        await ShowMessageBoxAsync($"Failed to launch {game.Name}: {ex.Message}", "Launch Error");
                    }
                });
            };
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.Items.Add(new Separator());
        // Add cancel option
        var cancelItem = new MenuItem
        {
            Header = "Cancel"
        };
        cancelItem.Click += (s, e) =>
        {
            game.SelectedExecutable = null;
        };
        contextMenu.Items.Add(cancelItem);
        // Focus first executable item when opened
        contextMenu.Opened += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var firstExecutableItem = contextMenu.Items.OfType<MenuItem>().Skip(1).FirstOrDefault(item => item is MenuItem mi && mi.IsEnabled);
                firstExecutableItem?.Focus();
            }, DispatcherPriority.Loaded);
        };
        OpenContextMenu(anchor, contextMenu);
    }

    private async Task PerformSelectedGameActionCoreAsync(GameInfo game)
    {
        using var priority = ReleaseRequestCoordinator.PrioritizeInteractiveChecks();
        var launched = false;
        try
        {
            if (game.Status == GameStatus.UpdateAvailable)
            {
                var updateAnchor = _resolveAnchor(game, null);
                if (updateAnchor == null)
                    return;
                ShowUpdateActionMenu(updateAnchor, game);
                return;
            }

            if (game.Status == GameStatus.NotInstalled)
                game.ClearDownloadSelection();
            launched = await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
            var anchor = _resolveAnchor(game, null);
            if (anchor != null && TryShowPendingSelectionMenus(anchor, game))
                return;
            Changed();
            RestoreFocus();
        }
        catch (Exception ex)
        {
            await ShowMessageBoxAsync($"Failed to perform action for {game.Name}: {ex.Message}", "Action Error");
            RestoreFocus();
        }

        _closeAfterLaunch(launched);
    }

    public async Task SelectExecutableAsync(GameInfo? game, Control? anchor)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("Unable to identify the selected app.", "Error"));
                return;
            }

            if (WindowsInstallerService.HasReceipt(game.GetInstallPath(_gameManager.GamesFolder)))
            {
                await WindowsInstallerService.SelectExecutableAsync(game, game.GetInstallPath(_gameManager.GamesFolder), AvaloniaGameDownloadDialogs.Instance);
                Changed();
                return;
            }

            anchor ??= _resolveAnchor(game, null);
            if (anchor == null)
            {
                _ = _session.RunAsync(() => ShowMessageBoxAsync("Unable to open the executable menu for this game.", "Error"));
                return;
            }

            game.ClearSelectedExecutable(_gameManager.GamesFolder);
            game.AvailableExecutables = null;
            try
            {
                if (string.IsNullOrWhiteSpace(game.FolderName))
                {
                    await ShowMessageBoxAsync("This game is missing its install folder information.", "Executable Error");
                    return;
                }

                var gamePath = game.GetInstallPath(_gameManager.GamesFolder);
                if (!Directory.Exists(gamePath))
                {
                    await ShowMessageBoxAsync($"Could not find the install folder for {game.Name}.", "Executable Error");
                    return;
                }

                var executables = GameInfo.GetExecutableCandidates(gamePath, SearchOption.TopDirectoryOnly, out _);
                if (executables.Count == 0)
                {
                    executables = GameInfo.GetExecutableCandidates(gamePath, SearchOption.AllDirectories, out _);
                }

                if (executables.Count == 0)
                {
                    await ShowMessageBoxAsync($"No executable files were found for {game.Name}.", "Executable Not Found");
                    return;
                }

                game.AvailableExecutables = executables;
                if (executables.Count == 1)
                {
                    await ShowMessageBoxAsync($"Only one executable was found for {game.Name}, so there is nothing else to choose.", "Single Executable");
                    return;
                }

                var menuAnchor = _resolveAnchor(game, anchor) ?? anchor;
                ShowExecutableSelectionMenu(menuAnchor, game);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to load executables for {game.Name}: {ex.Message}", "Executable Error");
            }
        });
    }

    public async Task LaunchFromMenuAsync(GameInfo? game, Control? anchor)
    {
        await _session.RunAsync(async () =>
        {
            if (game == null)
            {
                await ShowMessageBoxAsync("Unable to identify the selected app.", "Error");
                return;
            }

            try
            {
                if (game.Status == GameStatus.NotInstalled)
                    game.ClearDownloadSelection();
                var launched = await game.PerformActionAsync(_gameManager.HttpClient, _gameManager.GamesFolder, _settings);
                anchor = _resolveAnchor(game, anchor);
                if (anchor != null && TryShowPendingSelectionMenus(anchor, game))
                    return;
                Changed();
                _closeAfterLaunch(launched);
            }
            catch (Exception ex)
            {
                await ShowMessageBoxAsync($"Failed to launch {game.Name}: {ex.Message}", "Launch Error");
            }
        });
    }
}
