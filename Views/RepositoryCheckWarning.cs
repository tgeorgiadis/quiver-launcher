using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using QuiverLauncher.Models;

namespace QuiverLauncher.Views;

/// <summary>A repository warning available by hover, keyboard, or touch.</summary>
public sealed class RepositoryCheckWarning : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public RepositoryCheckWarning()
    {
        Classes.Add("options");
        Width = 28;
        Height = 28;
        Padding = new Thickness(0);
        Margin = new Thickness(4);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        ZIndex = 11;
        Content = new TextBlock { Text = "⚠", FontSize = 18, Foreground = Brushes.Goldenrod };
        AutomationProperties.SetName(this, "Repository check failed — show details");
        Bind(IsVisibleProperty, new Binding(nameof(GameInfo.HasRepositoryCheckError)));
        Bind(ToolTip.TipProperty, new Binding(nameof(GameInfo.RepositoryCheckError)));
        Bind(AutomationProperties.HelpTextProperty, new Binding(nameof(GameInfo.RepositoryCheckError)));
    }

    protected override void OnClick()
    {
        if (DataContext is not GameInfo { HasRepositoryCheckError: true } game) return;
        var text = new SelectableTextBlock
        {
            Text = $"{game.DisplayName}\n\n{game.RepositoryCheckError}",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 280,
        };
        new Flyout { Content = text }.ShowAt(this);
    }
}
