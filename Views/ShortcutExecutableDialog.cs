using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using QuiverLauncher.Services;

namespace QuiverLauncher.Views;

internal sealed class ShortcutExecutableDialog : Window
{
    internal string? SelectedExecutable { get; private set; }

    internal ShortcutExecutableDialog(string gameName, IReadOnlyList<string> candidates)
    {
        Title = "Choose shortcut executable";
        Width = 540;
        Height = 360;
        MinWidth = 300;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetValue(DesktopInterfaceScaling.ManagesScrollingProperty, true);
        var list = new ListBox
        {
            Focusable = true,
            ItemsSource = candidates.Select(path => new ListBoxItem
            {
                Tag = path,
                Content = new TextBlock { Text = Path.GetFileName(path), TextWrapping = TextWrapping.Wrap },
            }).ToList(),
            SelectedIndex = 0,
        };
        var choice = candidates.FirstOrDefault();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: string path }) choice = path;
        };
        var use = new Button { Content = "Use executable", IsDefault = true, Padding = new Thickness(14, 8), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(14, 8) };
        use.Click += (_, _) => { SelectedExecutable = choice; Close(); };
        cancel.Click += (_, _) => Close();
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock
        {
            Text = $"Choose the executable for {gameName}. This also saves your choice for launching it in Quiver.",
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(list, 1);
        layout.Children.Add(list);
        var buttons = new WrapPanel { Children = { use, cancel } };
        Grid.SetRow(buttons, 2);
        layout.Children.Add(buttons);
        Content = layout;
        GamepadModalDialogNavigation.Attach(this);
    }
}
