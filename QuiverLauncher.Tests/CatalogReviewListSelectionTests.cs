using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CatalogReviewListSelectionTests
{
    [AvaloniaFact]
    public void Sync_selection_clear_does_not_hang_when_review_rows_are_replaced()
    {
        var rows = new ResettableObservableCollection<string>();
        var listBox = new ListBox
        {
            Width = 400,
            Height = 600,
            SelectionMode = SelectionMode.Single,
            AutoScrollToSelectedItem = false,
            ItemsSource = rows,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { CacheLength = 2 }),
        };

        var replacing = false;
        var clearing = false;
        listBox.SelectionChanged += (_, _) =>
        {
            if (replacing || clearing || listBox.SelectedIndex < 0)
                return;

            clearing = true;
            try
            {
                listBox.SelectedIndex = -1;
            }
            finally
            {
                clearing = false;
            }
        };

        var window = new Window
        {
            Width = 400,
            Height = 600,
            Content = listBox,
        };

        try
        {
            window.Show();
            replacing = true;
            try
            {
                rows.ReplaceWith(Enumerable.Range(0, 40).Select(i => $"App {i}"));
            }
            finally
            {
                replacing = false;
            }

            if (listBox.SelectedIndex >= 0)
                listBox.SelectedIndex = -1;

            Dispatcher.UIThread.RunJobs();
            listBox.SelectedIndex.Should().Be(-1);
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    }
}
