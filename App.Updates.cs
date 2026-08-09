using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using QuiverLauncher.Services;
using Velopack;

namespace QuiverLauncher;

public partial class App
{
    private class ProgressWindow : Window
    {
        private readonly TextBlock _statusText;
        private readonly ProgressBar _progressBar;
        private readonly TextBlock _percentText;

        public ProgressWindow()
        {
            Title = "Updating Launcher";
            Width = 450;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            CanResize = false;

            var panel = new StackPanel
            {
                Margin = new Thickness(30),
                Spacing = 15
            };

            _statusText = new TextBlock
            {
                Text = "Preparing download...",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                TextAlignment = TextAlignment.Center
            };

            _progressBar = new ProgressBar
            {
                Height = 24,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            _percentText = new TextBlock
            {
                Text = "0%",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.LightGray),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -5, 0, 0)
            };

            panel.Children.Add(_statusText);
            panel.Children.Add(_progressBar);
            panel.Children.Add(_percentText);

            Content = panel;
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1a));
        }

        public void UpdateProgress(double percentage, string status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _progressBar.Value = percentage;
                _percentText.Text = $"{percentage:F1}%";
                _statusText.Text = status;
            });
        }
    }

    public async Task<ManualLauncherCheckResult> CheckForAppUpdatesManually()
    {
        await _updateCheckSemaphore.WaitAsync();
        try
        {
            return await CheckForUpdatesAndApplyCoreAsync(isManualCheck: true)
                   ?? BuildManualLauncherResult(
                       _velopackUpdates.CurrentVersion
                       ?? LauncherVersionService.ReadInstalledVersion(AppDomain.CurrentDomain.BaseDirectory),
                       checkSucceeded: false,
                       errorMessage: "Update check did not complete.");
        }
        finally
        {
            _updateCheckSemaphore.Release();
        }
    }

    public async Task PromptForPendingLauncherUpdateAsync()
    {
        var result = await CheckVelopackUpdatesAsync();
        if (!result.UpdateAvailable || result.UpdateInfo == null)
            return;

        await PromptAndApplyVelopackUpdateAsync(result.UpdateInfo, result.AvailableVersion ?? "new version", result.IncludedPrerelease);
    }

    public async Task ApplyPendingLauncherUpdateAsync()
    {
        var result = await CheckVelopackUpdatesAsync();
        if (!result.UpdateAvailable || result.UpdateInfo == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await DownloadAndApplyVelopackUpdateAsync(result.UpdateInfo, result.IncludedPrerelease);
        });
    }

    public bool IsLauncherUpdatePending() =>
        _velopackUpdates.IsUpdatePendingRestart || (_velopackUpdates.LastUpdateInfo != null);

    private static ManualLauncherCheckResult BuildManualLauncherResult(
        string installedVersion,
        bool checkSucceeded = true,
        string? errorMessage = null,
        bool launcherUpdatePending = false,
        string? availableLauncherVersion = null) =>
        new()
        {
            CheckSucceeded = checkSucceeded,
            ErrorMessage = errorMessage,
            InstalledVersion = installedVersion,
            LauncherUpdatePending = launcherUpdatePending,
            AvailableLauncherVersion = availableLauncherVersion,
        };

    private async Task CheckForUpdatesAndApplyAsync(bool isManualCheck = false)
    {
        if (!isManualCheck && VelopackUpdateService.ShouldSkipAutomaticSelfUpdate())
        {
            Trace.WriteLine("Skipping launcher self-update check (DEBUG build or QuiverLauncher_SKIP_UPDATES is set).");
            return;
        }

        await _updateCheckSemaphore.WaitAsync();
        try
        {
            _ = await CheckForUpdatesAndApplyCoreAsync(isManualCheck: false);
        }
        finally
        {
            _updateCheckSemaphore.Release();
        }
    }

    private async Task<VelopackCheckResult> CheckVelopackUpdatesAsync()
    {
        var settings = AppSettings.Load();
        return await _velopackUpdates.CheckForUpdatesAsync(
            GetGitHubToken(),
            allowPrereleaseLauncherUpdates: settings.AllowPrereleaseLauncherUpdates);
    }

    private async Task<ManualLauncherCheckResult?> CheckForUpdatesAndApplyCoreAsync(bool isManualCheck)
    {
        var result = await CheckVelopackUpdatesAsync();

        if (result.IsNotInstalled)
        {
            if (isManualCheck)
            {
                return BuildManualLauncherResult(
                    result.InstalledVersion,
                    checkSucceeded: true,
                    errorMessage: "This build is not a Velopack install, so self-update is unavailable.");
            }

            return null;
        }

        if (!result.CheckSucceeded)
        {
            if (isManualCheck)
            {
                return BuildManualLauncherResult(
                    result.InstalledVersion,
                    checkSucceeded: false,
                    errorMessage: result.ErrorMessage ?? "Update check failed.");
            }

            return null;
        }

        if (!result.UpdateAvailable || result.UpdateInfo == null)
        {
            if (isManualCheck)
            {
                return BuildManualLauncherResult(
                    result.InstalledVersion,
                    launcherUpdatePending: false);
            }

            return null;
        }

        if (isManualCheck)
        {
            return BuildManualLauncherResult(
                result.InstalledVersion,
                launcherUpdatePending: true,
                availableLauncherVersion: result.AvailableVersion);
        }

        await PromptAndApplyVelopackUpdateAsync(
            result.UpdateInfo,
            result.AvailableVersion ?? "new version",
            result.IncludedPrerelease);
        return null;
    }

    private async Task PromptAndApplyVelopackUpdateAsync(
        UpdateInfo updateInfo,
        string versionLabel,
        bool includePrerelease)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var message = AppUpdateReviewMessages.FormatQuiverOnlyUpdateMessage(versionLabel);
            bool accepted = await ShowMessageBoxWithChoiceAsync(
                message,
                "Update Available",
                confirmText: "Update Quiver Launcher",
                dismissText: "Not now");

            if (!accepted)
                return;

            await DownloadAndApplyVelopackUpdateAsync(updateInfo, includePrerelease);
        });
    }

    private async Task DownloadAndApplyVelopackUpdateAsync(UpdateInfo updateInfo, bool includePrerelease)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    progressWindow = new ProgressWindow();
                    _ = progressWindow.ShowDialog(desktop.MainWindow);
                }
            });

            progressWindow?.UpdateProgress(0, "Downloading update...");
            var token = GetGitHubToken();

            await _velopackUpdates.DownloadUpdatesAsync(
                updateInfo,
                progress: p => progressWindow?.UpdateProgress(p, $"Downloading update... {p}%"),
                gitHubToken: token,
                includePrerelease: includePrerelease);

            progressWindow?.UpdateProgress(100, "Installing update...");
            await Task.Delay(300);
            await Dispatcher.UIThread.InvokeAsync(() => progressWindow?.Close());

            _velopackUpdates.ApplyUpdatesAndRestart(updateInfo, includePrerelease);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => progressWindow?.Close());
            await ShowMessageBoxAsync($"Failed to update Quiver Launcher: {ex.Message}", "Update Error");
        }
    }

    private static string? GetGitHubToken()
    {
        try
        {
            return AppSettings.Load()?.GitHubApiToken;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> ShowMessageBoxWithChoiceAsync(
        string message,
        string title,
        string confirmText = "Yes",
        string dismissText = "No")
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            bool result = false;
            var messageBox = new Window
            {
                Title = title,
                Width = 450,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Tag = false,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 20)
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Children =
                            {
                                new Button
                                {
                                    Content = confirmText,
                                    Margin = new Thickness(0, 0, 10, 0),
                                    MinWidth = 80
                                },
                                new Button
                                {
                                    Content = dismissText,
                                    MinWidth = 80
                                }
                            }
                        }
                    }
                }
            };

            if (((StackPanel)messageBox.Content).Children[1] is StackPanel buttonPanel &&
                buttonPanel.Children[0] is Button yesButton &&
                buttonPanel.Children[1] is Button noButton)
            {
                yesButton.Click += (_, _) =>
                {
                    result = true;
                    messageBox.Tag = true;
                    messageBox.Close();
                };

                noButton.Click += (_, _) =>
                {
                    result = false;
                    messageBox.Tag = false;
                    messageBox.Close();
                };
            }

            GamepadModalDialogNavigation.Attach(messageBox, accepted =>
            {
                result = accepted;
                messageBox.Tag = accepted;
            });

            await messageBox.ShowDialog(desktop.MainWindow);
            if (messageBox.Tag is bool tagResult)
                return tagResult;
            return result;
        }

        return false;
    }

    private async Task ShowMessageBoxAsync(string message, string title)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            var messageBox = new Window
            {
                Title = title,
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) },
                        new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };

            if (((StackPanel)messageBox.Content).Children[1] is Button okButton)
                okButton.Click += (_, _) => messageBox.Close();

            GamepadModalDialogNavigation.Attach(messageBox);
            await messageBox.ShowDialog(desktop.MainWindow);
        }
    }
}
