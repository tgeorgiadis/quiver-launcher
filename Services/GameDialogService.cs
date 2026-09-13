using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using QuiverLauncher;

namespace QuiverLauncher.Services;

public static class GameDialogService
{
    public static bool IsGitHubRateLimitError(Exception ex) => IsRateLimitError(ex);

    public static bool IsRateLimitError(Exception ex)
    {
        if (ex is QuiverLauncher.Core.Services.ReleaseFetchException releaseError)
            return releaseError.Result.IsRateLimited;

        var message = ex.Message;
        return message.Contains("403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private static Window? TryGetDesktopMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;

        return null;
    }

    private static bool HasInteractiveUi() =>
        TryGetDesktopMainWindow() is not null ||
        Application.Current?.ApplicationLifetime is IActivityApplicationLifetime ||
        Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime;

    private static MainView? TryGetMainView()
    {
        return Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop when desktop.MainWindow is MainWindow window =>
                window.View,
            IActivityApplicationLifetime => App.TryGetHostedMainView(),
            ISingleViewApplicationLifetime single when single.MainView is MainView view => view,
            _ => App.TryGetHostedMainView(),
        };
    }

    public static async Task ShowWindowAsync(Window dialog, CancellationToken cancellationToken = default)
    {
        if (!cancellationToken.CanBeCanceled) cancellationToken = LauncherSession.OperationCancellation;
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellation = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
        if (TryGetDesktopMainWindow() is Window owner)
        {
            DesktopInterfaceScaling.PrepareDialog(dialog, owner);
            await dialog.ShowDialog(owner);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.Show();
        await closed.Task;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void WriteConsoleError(string title, string message)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {title}");
        Console.ResetColor();
        Console.WriteLine(message);
        Console.WriteLine();
    }

    public static async Task ShowMessageBoxAsync(string message, string title)
    {
        if (!HasInteractiveUi())
        {
            WriteConsoleError(title, message);
            return;
        }

        var operationToken = LauncherSession.OperationCancellation;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            operationToken.ThrowIfCancellationRequested();
            if (TryGetDesktopMainWindow() is null && TryGetMainView() is MainView view)
            {
                await view.ShowOverlayPromptAsync(message, title, isQuestion: false);
                return;
            }

            var messageBox = new Window
            {
                Title = title,
                MinWidth = 420,
                MaxWidth = 520,
                MaxHeight = 520,
                CanResize = true,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Padding = new Thickness(12, 6),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            okButton.Click += (_, _) => messageBox.Close();

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { okButton },
            };

            messageBox.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new ScrollViewer
                    {
                        MaxHeight = 360,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 13,
                        },
                    },
                    buttonRow,
                },
            };

            GamepadModalDialogNavigation.Attach(messageBox);

            await ShowWindowAsync(messageBox, operationToken);
        });
    }

    public static async Task<bool> ShowWineNotFoundWarningAsync()
    {
        if (TryGetDesktopMainWindow() is not Window mainWindow)
            return true;

        var userChoice = false;

        var operationToken = LauncherSession.OperationCancellation;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var messageBox = new Window
            {
                Title = "Windows Runner Not Found",
                Width = 500,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "This game requires a Linux Windows-runner to launch, but none was detected.\n\n" +
                                   "Install Wine/Proton, or after install open this app’s menu (⋯) → Launch Options → Windows Runner to pick a runner or custom command.\n\n" +
                                   "Do you want to download anyway? The game will not launch without a configured runner.",
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 0, 0, 20),
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Spacing = 10,
                            Children =
                            {
                                new Button { Content = "Download Anyway", Width = 140 },
                                new Button { Content = "Cancel", Width = 100 },
                            },
                        },
                    },
                },
            };

            if (((StackPanel)messageBox.Content).Children[1] is StackPanel buttonPanel &&
                buttonPanel.Children[0] is Button yesButton &&
                buttonPanel.Children[1] is Button noButton)
            {
                messageBox.Tag = false;
                yesButton.Click += (_, _) =>
                {
                    userChoice = true;
                    messageBox.Tag = true;
                    messageBox.Close();
                };
                noButton.Click += (_, _) =>
                {
                    userChoice = false;
                    messageBox.Tag = false;
                    messageBox.Close();
                };
            }

            GamepadModalDialogNavigation.Attach(messageBox, accepted =>
            {
                userChoice = accepted;
                messageBox.Tag = accepted;
            });

            await ShowWindowAsync(messageBox, operationToken);
            if (messageBox.Tag is bool tagResult)
                userChoice = tagResult;
        });

        return userChoice;
    }

    /// <summary>
    /// Lets the user pick Wine/Proton/Custom and a prefix path for a Windows app on Linux.
    /// Returns null if cancelled.
    /// </summary>
    public static async Task<LinuxWindowsRunnerConfig?> ShowLinuxWindowsRunnerDialogAsync(
        string gamePath,
        LinuxWindowsRunnerConfig? existing = null,
        bool isInstall = true, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetDesktopMainWindow() is not Window mainWindow)
        {
            var kind = existing?.Kind ?? WindowsRunnerService.GetPreferredDefaultKind();
            return new LinuxWindowsRunnerConfig
            {
                Kind = kind,
                PrefixPath = existing?.PrefixPath ?? WindowsRunnerService.GetDefaultPrefixPathForKind(kind, gamePath),
                ProtonPath = existing?.ProtonPath
                             ?? WindowsRunnerService.ListDetectedProtonInstallations().FirstOrDefault()?.ProtonExecutable,
                CustomLaunchCommand = existing?.CustomLaunchCommand,
            };
        }

        LinuxWindowsRunnerConfig? result = null;

        var operationToken = LauncherSession.OperationCancellation;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var protons = WindowsRunnerService.ListDetectedProtonInstallations();
            var wineAvailable = WindowsRunnerService.IsWineAvailable();
            var initial = existing ?? new LinuxWindowsRunnerConfig
            {
                Kind = WindowsRunnerService.GetPreferredDefaultKind(),
            };

            if (string.IsNullOrWhiteSpace(initial.PrefixPath))
            {
                initial.PrefixPath = WindowsRunnerService.GetDefaultPrefixPathForKind(
                    initial.Kind == LinuxWindowsRunnerKind.Auto
                        ? WindowsRunnerService.GetPreferredDefaultKind()
                        : initial.Kind,
                    gamePath);
            }

            if (string.IsNullOrWhiteSpace(initial.ProtonPath))
                initial.ProtonPath = protons.FirstOrDefault()?.ProtonExecutable;

            var runnerCombo = new ComboBox
            {
                MinWidth = 360,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Focusable = true,
            };
            GamepadComboBoxNavigation.Attach(runnerCombo);

            void AddRunnerItem(string label, LinuxWindowsRunnerKind kind, string? protonPath = null)
            {
                runnerCombo.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = (kind, protonPath),
                });
            }

            AddRunnerItem("Auto (prefer Proton, then Wine)", LinuxWindowsRunnerKind.Auto);
            if (wineAvailable)
                AddRunnerItem("System Wine", LinuxWindowsRunnerKind.Wine);
            foreach (var proton in protons)
                AddRunnerItem($"Proton — {proton.DisplayName}", LinuxWindowsRunnerKind.Proton, proton.ProtonExecutable);
            AddRunnerItem("Custom command", LinuxWindowsRunnerKind.Custom);

            ComboBoxItem? preferredItem = null;
            foreach (var itemObj in runnerCombo.Items)
            {
                if (itemObj is not ComboBoxItem { Tag: ValueTuple<LinuxWindowsRunnerKind, string?> tag } item)
                    continue;

                var (kind, protonPath) = tag;
                if (kind != initial.Kind)
                    continue;

                if (kind == LinuxWindowsRunnerKind.Proton &&
                    !string.IsNullOrWhiteSpace(initial.ProtonPath) &&
                    !string.Equals(protonPath, initial.ProtonPath, StringComparison.OrdinalIgnoreCase))
                {
                    preferredItem ??= item;
                    continue;
                }

                preferredItem = item;
                if (kind != LinuxWindowsRunnerKind.Proton ||
                    string.Equals(protonPath, initial.ProtonPath, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(initial.ProtonPath))
                {
                    break;
                }
            }

            runnerCombo.SelectedItem = preferredItem ?? runnerCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();

            var prefixBox = new TextBox
            {
                Text = initial.PrefixPath ?? string.Empty,
                Watermark = "Prefix / compatdata folder",
                MinWidth = 360,
            };

            var customBox = new TextBox
            {
                Text = initial.CustomLaunchCommand ?? string.Empty,
                Watermark = "Example: flatpak run com.usebottles.bottles -e {exe}",
                MinWidth = 360,
                IsVisible = initial.Kind == LinuxWindowsRunnerKind.Custom,
            };

            var customLabel = new TextBlock
            {
                Text = "Custom launch command",
                FontSize = 12,
                Opacity = 0.8,
                IsVisible = customBox.IsVisible,
            };

            var lastPrefixByKind = new Dictionary<LinuxWindowsRunnerKind, string>
            {
                [LinuxWindowsRunnerKind.Wine] = WindowsRunnerService.GetDefaultWinePrefixPath(gamePath),
                [LinuxWindowsRunnerKind.Proton] = WindowsRunnerService.GetDefaultProtonCompatDataPath(gamePath),
                [LinuxWindowsRunnerKind.Auto] = WindowsRunnerService.GetDefaultPrefixPathForKind(
                    WindowsRunnerService.GetPreferredDefaultKind(), gamePath),
                [LinuxWindowsRunnerKind.Custom] = initial.PrefixPath ?? string.Empty,
            };
            if (!string.IsNullOrWhiteSpace(initial.PrefixPath))
                lastPrefixByKind[initial.Kind] = initial.PrefixPath;

            runnerCombo.SelectionChanged += (_, _) =>
            {
                if (runnerCombo.SelectedItem is not ComboBoxItem { Tag: ValueTuple<LinuxWindowsRunnerKind, string?> tag })
                    return;

                var kind = tag.Item1;
                var customWasVisible = customBox.IsVisible;
                customBox.IsVisible = kind == LinuxWindowsRunnerKind.Custom;
                customLabel.IsVisible = customBox.IsVisible;
                if (customWasVisible != customBox.IsVisible)
                    GamepadModalDialogNavigation.Instance.RefreshDialogButtons();

                var effectiveKind = kind == LinuxWindowsRunnerKind.Auto
                    ? WindowsRunnerService.GetPreferredDefaultKind()
                    : kind;
                if (kind is LinuxWindowsRunnerKind.Wine or LinuxWindowsRunnerKind.Proton or LinuxWindowsRunnerKind.Auto)
                {
                    var current = prefixBox.Text?.Trim() ?? string.Empty;
                    var looksLikeDefault =
                        string.IsNullOrWhiteSpace(current) ||
                        lastPrefixByKind.Values.Any(v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase));
                    if (looksLikeDefault)
                    {
                        prefixBox.Text = WindowsRunnerService.GetDefaultPrefixPathForKind(effectiveKind, gamePath);
                        lastPrefixByKind[kind] = prefixBox.Text;
                    }
                }
            };

            var confirmLabel = isInstall ? "Save & Download" : "Save";
            var confirmButton = new Button
            {
                Content = confirmLabel,
                Padding = new Thickness(16, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(16, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };

            var messageBox = new Window
            {
                Title = "Windows Runner",
                Width = 520,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = isInstall
                                ? "This Windows app needs Wine or Proton on Linux. Choose a runner and an isolated prefix folder for this app."
                                : "Choose the Wine/Proton runner and prefix used when launching this Windows app.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new TextBlock { Text = "Runner", FontSize = 12, Opacity = 0.8 },
                        runnerCombo,
                        new TextBlock
                        {
                            Text = "Prefix / compatdata path",
                            FontSize = 12,
                            Opacity = 0.8,
                        },
                        prefixBox,
                        customLabel,
                        customBox,
                        new TextBlock
                        {
                            Text = "A prefix is a private fake Windows environment for this app (registry, DLLs). Keeping one per app avoids conflicts.",
                            FontSize = 11,
                            Opacity = 0.7,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 4, 0, 0),
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Spacing = 10,
                            Margin = new Thickness(0, 12, 0, 0),
                            Children = { confirmButton, cancelButton },
                        },
                    },
                },
            };

            confirmButton.Click += (_, _) =>
            {
                if (runnerCombo.SelectedItem is not ComboBoxItem { Tag: ValueTuple<LinuxWindowsRunnerKind, string?> selected })
                {
                    messageBox.Close();
                    return;
                }

                var (kind, protonPath) = selected;
                result = new LinuxWindowsRunnerConfig
                {
                    Kind = kind,
                    PrefixPath = string.IsNullOrWhiteSpace(prefixBox.Text) ? null : prefixBox.Text.Trim(),
                    ProtonPath = kind == LinuxWindowsRunnerKind.Proton ? protonPath : null,
                    CustomLaunchCommand = kind == LinuxWindowsRunnerKind.Custom
                        ? (string.IsNullOrWhiteSpace(customBox.Text) ? null : customBox.Text.Trim())
                        : null,
                };
                messageBox.Tag = true;
                messageBox.Close();
            };

            cancelButton.Click += (_, _) =>
            {
                result = null;
                messageBox.Tag = false;
                messageBox.Close();
            };

            GamepadModalDialogNavigation.Attach(messageBox, accepted =>
            {
                if (!accepted)
                {
                    result = null;
                    messageBox.Tag = false;
                    return;
                }

                if (runnerCombo.SelectedItem is ComboBoxItem { Tag: ValueTuple<LinuxWindowsRunnerKind, string?> selected })
                {
                    var (kind, protonPath) = selected;
                    result = new LinuxWindowsRunnerConfig
                    {
                        Kind = kind,
                        PrefixPath = string.IsNullOrWhiteSpace(prefixBox.Text) ? null : prefixBox.Text.Trim(),
                        ProtonPath = kind == LinuxWindowsRunnerKind.Proton ? protonPath : null,
                        CustomLaunchCommand = kind == LinuxWindowsRunnerKind.Custom
                            ? (string.IsNullOrWhiteSpace(customBox.Text) ? null : customBox.Text.Trim())
                            : null,
                    };
                    messageBox.Tag = true;
                }
            });

            using var cancellation = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => { result = null; messageBox.Close(); }));
            cancellationToken.ThrowIfCancellationRequested();
            await ShowWindowAsync(messageBox, operationToken);
        });

        return result;
    }

    public static async Task ShowRateLimitErrorAsync()
    {
        if (!HasInteractiveUi())
            return;

        var operationToken = LauncherSession.OperationCancellation;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var messageBox = new Views.GitHubRateLimitDialog(
                () => TryGetMainView()?.OpenGitHubApiTokenSettings(),
                () =>
                {
                    try { UrlLauncher.Open(GitHubTokenSetupGuide.CreateTokenUrl); }
                    catch (Exception ex) { Debug.WriteLine($"Failed to open GitHub token page: {ex.Message}"); }
                });
            await ShowWindowAsync(messageBox, operationToken);
        });
    }
    public static async Task ShowGitLabRateLimitErrorAsync()
    {
        if (!HasInteractiveUi())
        {
            WriteConsoleError(
                "Rate Limit Exceeded",
                "GitLab API rate limit exceeded. Add a GitLab API token in Settings → Advanced.");
            return;
        }

        var operationToken = LauncherSession.OperationCancellation;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var hyperlinkText = new TextBlock
            {
                Text = "https://gitlab.com/-/user_settings/personal_access_tokens",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255)),
                Cursor = new Cursor(StandardCursorType.Hand),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 0, 0, 0),
            };

            hyperlinkText.PointerPressed += (_, _) =>
            {
                try
                {
                    UrlLauncher.Open("https://gitlab.com/-/user_settings/personal_access_tokens");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open URL: {ex.Message}");
                }
            };

            var openSettingsButton = new Button
            {
                Content = "Open Settings",
                MinWidth = 120,
            };

            var closeButton = new Button
            {
                Content = "Close",
                MinWidth = 100,
            };

            var messageBox = new Window
            {
                Title = "Rate Limit Exceeded",
                Width = 600,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Spacing = 15,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "GitLab API rate limit exceeded.",
                                FontWeight = FontWeight.Bold,
                                FontSize = 16,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new TextBlock
                            {
                                Text = "GitLab limits unauthenticated API requests. Adding a personal access token raises the limit and enables private projects.",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new TextBlock
                            {
                                Text = "To avoid this, add a GitLab API token in Settings → Advanced:",
                                FontWeight = FontWeight.SemiBold,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 10, 0, 0),
                            },
                            new TextBlock
                            {
                                Text = "1. Click the link below to create a token:",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            hyperlinkText,
                            new TextBlock { Text = "2. Create a personal access token (read_api is enough for public releases)", TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = "3. Copy the token and paste it in Settings → Advanced → GitLab API Token", TextWrapping = TextWrapping.Wrap },
                            new TextBlock
                            {
                                Text = "Do not share your token with anyone!",
                                Foreground = new SolidColorBrush(Color.FromRgb(255, 149, 0)),
                                FontWeight = FontWeight.Bold,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 10, 0, 0),
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Spacing = 10,
                                Margin = new Thickness(0, 10, 0, 0),
                                Children = { openSettingsButton, closeButton },
                            },
                        },
                    },
                },
            };

            openSettingsButton.Click += (_, _) =>
            {
                messageBox.Close();
                TryGetMainView()?.OpenGitLabApiTokenSettings();
            };
            closeButton.Click += (_, _) => messageBox.Close();

            GamepadModalDialogNavigation.Attach(messageBox);



            await ShowWindowAsync(messageBox, operationToken);
        });
    }
}
