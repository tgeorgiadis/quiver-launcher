using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using QuiverLauncher.Models;
using QuiverLauncher.Views;

namespace QuiverLauncher.Services;
public enum CombinedUpdateChoice
{
    Dismiss,
    UpdateQuiver,
    UpdateApps
}

/// <summary>Preserves desktop dialogs and in-view prompts under one session lifetime.</summary>
public sealed class LauncherPromptService(LauncherSession session, LauncherDialogLifetime dialogs, MessagePromptView overlay)
{
    private LauncherSession _session => session;
    private LauncherDialogLifetime _dialogs => dialogs;
    private MessagePromptView _overlay => overlay;

    public async Task<CombinedUpdateChoice> PromptCombinedUpdatesAsync(string? launcherVersion, IReadOnlyList<GameInfo> pendingApps)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_session.IsClosed) return CombinedUpdateChoice.Dismiss;
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null)
            {
                return CombinedUpdateChoice.Dismiss;
            }

            var message = AppUpdateReviewMessages.FormatCombinedUpdatesMessage(launcherVersion, pendingApps);
            var choice = CombinedUpdateChoice.Dismiss;
            var messageBox = new Window
            {
                Title = "Updates Available",
                MinWidth = 420,
                MaxWidth = 520,
                MaxHeight = 520,
                CanResize = true,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            _dialogs.Track(messageBox);
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 360,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                },
            };
            var updateQuiverButton = new Button
            {
                Content = "Update Quiver Launcher",
                MinWidth = 110,
                Margin = new Thickness(0, 0, 8, 0),
            };
            var updateAppsButton = new Button
            {
                Content = "Update Apps",
                MinWidth = 110,
                Margin = new Thickness(0, 0, 8, 0),
            };
            var dismissButton = new Button
            {
                Content = "Not now",
                MinWidth = 80,
            };
            updateQuiverButton.Click += (_, _) =>
            {
                choice = CombinedUpdateChoice.UpdateQuiver;
                messageBox.Close();
            };
            updateAppsButton.Click += (_, _) =>
            {
                choice = CombinedUpdateChoice.UpdateApps;
                messageBox.Close();
            };
            dismissButton.Click += (_, _) =>
            {
                choice = CombinedUpdateChoice.Dismiss;
                messageBox.Close();
            };
            messageBox.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    scrollViewer,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            updateQuiverButton,
                            updateAppsButton,
                            dismissButton
                        },
                    },
                },
            };
            GamepadModalDialogNavigation.Attach(messageBox);
            DesktopInterfaceScaling.PrepareDialog(messageBox, desktop.MainWindow);
            await messageBox.ShowDialog(desktop.MainWindow);
            return choice;
        });
    }

    public async Task ShowWelcomeMessageBoxAsync(string message, string title)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_session.IsClosed) return;
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var messageBox = CreateWelcomeMessageBoxWindow(message, title);
                _dialogs.Track(messageBox);
                GamepadModalDialogNavigation.Attach(messageBox);
                DesktopInterfaceScaling.PrepareDialog(messageBox, desktop.MainWindow);
                await messageBox.ShowDialog(desktop.MainWindow);
            }
        });
    }

    public static Window CreateWelcomeMessageBoxWindow(string message, string title)
    {
        var browseButton = new Button
        {
            Content = "Browse app catalog",
            MinWidth = 190,
            MinHeight = 44,
            Padding = new Thickness(20, 10),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        browseButton.Classes.Add("accent");
        var window = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#18181b")),
            Content = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 22,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 14,
                        LineHeight = 22,
                        Foreground = new SolidColorBrush(Color.Parse("#D4D4D8")),
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                    },
                    browseButton,
                },
            },
        };
        browseButton.Click += (_, _) => window.Close();
        return window;
    }

    public static Window CreateScrollableMessageBoxWindow(string message, string title, bool isQuestion, Action<bool>? onQuestionResult = null, bool preferCancelDefault = false, bool includeCancel = false)
    {
        var messageBox = new Window
        {
            Title = title,
            MinWidth = 420,
            MaxWidth = 560,
            MaxHeight = 520,
            CanResize = true,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 360,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
            },
        };
        Control buttonRow;
        if (isQuestion)
        {
            messageBox.Tag = includeCancel ? MessagePromptResult.Cancel : false;
            var yesButton = new Button
            {
                Content = "Yes",
                Margin = new Thickness(0, 0, 10, 0),
                MinWidth = 80,
            };
            var noButton = new Button
            {
                Content = "No",
                MinWidth = 80,
            };
            Button? cancelButton = null;
            if (includeCancel)
            {
                noButton.Margin = new Thickness(0, 0, 10, 0);
                cancelButton = new Button
                {
                    Content = "Cancel",
                    MinWidth = 80,
                };
            }

            if (includeCancel)
            {
                yesButton.IsDefault = true;
                cancelButton!.IsCancel = true;
            }
            else if (preferCancelDefault)
            {
                noButton.IsDefault = true;
                noButton.IsCancel = true;
            }
            else
            {
                yesButton.IsDefault = true;
                noButton.IsCancel = true;
            }

            yesButton.Click += (_, _) =>
            {
                messageBox.Tag = includeCancel ? MessagePromptResult.Yes : true;
                onQuestionResult?.Invoke(true);
                messageBox.Close();
            };
            noButton.Click += (_, _) =>
            {
                messageBox.Tag = includeCancel ? MessagePromptResult.No : false;
                onQuestionResult?.Invoke(false);
                messageBox.Close();
            };
            if (cancelButton != null)
            {
                cancelButton.Click += (_, _) =>
                {
                    messageBox.Tag = MessagePromptResult.Cancel;
                    messageBox.Close();
                };
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            buttons.Children.Add(yesButton);
            buttons.Children.Add(noButton);
            if (cancelButton != null)
                buttons.Children.Add(cancelButton);
            buttonRow = buttons;
        }
        else
        {
            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Padding = new Thickness(12, 6),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            okButton.Click += (_, _) => messageBox.Close();
            buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    okButton
                },
            };
        }

        messageBox.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                scrollViewer,
                buttonRow
            },
        };
        if (isQuestion)
        {
            if (includeCancel)
            {
                GamepadModalDialogNavigation.Attach(messageBox);
            }
            else
            {
                GamepadModalDialogNavigation.Attach(messageBox, accepted =>
                {
                    messageBox.Tag = accepted;
                    onQuestionResult?.Invoke(accepted);
                });
            }
        }
        else
        {
            GamepadModalDialogNavigation.Attach(messageBox);
        }

        return messageBox;
    }

    private static bool ShouldUseOverlayPrompt()
    {
        if (PlatformCapabilities.IsMobile)
            return true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow is null;
        return true;
    }

    public async Task ShowMessageBoxAsync(string message, string title)
    {
        if (_session.IsClosed) return;
        if (ShouldUseOverlayPrompt())
        {
            await _overlay.ShowAsync(message, title, isQuestion: false);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_session.IsClosed) return;
            var messageBox = CreateScrollableMessageBoxWindow(message, title, isQuestion: false);
            _dialogs.Track(messageBox);
            await GameDialogService.ShowWindowAsync(messageBox);
        });
    }

    public async Task<bool> ShowMessageBoxAsync(string message, string title, bool isQuestion = false, bool preferCancelDefault = false)
    {
        if (_session.IsClosed) return false;
        if (!isQuestion)
        {
            await ShowMessageBoxAsync(message, title);
            return true;
        }

        if (ShouldUseOverlayPrompt())
            return await _overlay.ShowAsync(message, title, isQuestion: true, preferCancelDefault) == MessagePromptResult.Yes;
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_session.IsClosed) return false;
            var result = false;
            var messageBox = CreateScrollableMessageBoxWindow(message, title, isQuestion: true, onQuestionResult: value => result = value, preferCancelDefault: preferCancelDefault);
            _dialogs.Track(messageBox);
            await GameDialogService.ShowWindowAsync(messageBox);
            return ReadMessagePromptBool(messageBox.Tag, result);
        });
    }

    public async Task<MessagePromptResult> ShowChoicePromptAsync(string message, string title)
    {
        if (_session.IsClosed) return MessagePromptResult.Cancel;
        if (ShouldUseOverlayPrompt())
        {
            return await _overlay.ShowAsync(message, title, isQuestion: true, includeCancel: true).ConfigureAwait(true);
        }

        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_session.IsClosed) return MessagePromptResult.Cancel;
            var messageBox = CreateScrollableMessageBoxWindow(message, title, isQuestion: true, includeCancel: true);
            _dialogs.Track(messageBox);
            await GameDialogService.ShowWindowAsync(messageBox);
            return ReadMessagePromptResult(messageBox.Tag);
        });
    }

    static bool ReadMessagePromptBool(object? tag, bool fallback)
    {
        if (tag is MessagePromptResult choice)
            return choice == MessagePromptResult.Yes;
        if (tag is bool flag)
            return flag;
        return fallback;
    }

    static MessagePromptResult ReadMessagePromptResult(object? tag) => tag switch
    {
        MessagePromptResult choice => choice,
        true => MessagePromptResult.Yes,
        false => MessagePromptResult.No,
        _ => MessagePromptResult.Cancel,
    };
}
