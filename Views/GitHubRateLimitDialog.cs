using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using QuiverLauncher.Services;

namespace QuiverLauncher.Views;

internal sealed class GitHubRateLimitDialog : Window
{
    internal GitHubRateLimitDialog(Action openSettings, Action createToken)
    {
        Title = "GitHub rate limit reached";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 600;
        MinWidth = 300;
        MinHeight = 200;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
        var heading = Text("GitHub API rate limit reached");
        heading.FontSize = 18;
        heading.FontWeight = FontWeight.SemiBold;
        var guide = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var step in new[] { GitHubTokenSetupGuide.Introduction, GitHubTokenSetupGuide.CreateStep,
                     GitHubTokenSetupGuide.ExpirationStep, GitHubTokenSetupGuide.PermissionsStep,
                     GitHubTokenSetupGuide.GenerateStep, GitHubTokenSetupGuide.SaveStep,
                     GitHubTokenSetupGuide.Privacy })
            guide.Children.Add(Text(step));

        var body = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    heading,
                    Text("GitHub has temporarily blocked release checks. This does not mean the app has no releases."),
                    Text("Add a GitHub token for a higher request limit, save it in Settings, then retry the download."),
                    guide,
                },
            },
        };
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        Button ActionButton(string label, Action action)
        {
            var button = new Button
            {
                Content = label, MinHeight = 40, MinWidth = 88,
                Padding = new Thickness(14, 8), Margin = new Thickness(0, 0, 8, 8),
            };
            button.Click += (_, _) => action();
            actions.Children.Add(button);
            return button;
        }
        ActionButton("Open Settings", () => { Close(); openSettings(); });
        ActionButton("Create token", createToken).SetValue(GamepadModalDialogNavigation.KeepDialogOpenProperty, true);
        ActionButton("Close", Close);
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20) };
        layout.Children.Add(body);
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        Content = layout;
        GamepadModalDialogNavigation.Attach(this);
    }
}
