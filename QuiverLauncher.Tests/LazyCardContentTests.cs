using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class LazyCardContentTests
{
    [AvaloniaFact]
    public void Cards_are_built_near_the_screen_and_dropped_far_from_it_without_moving()
    {
        var cards = Enumerable.Range(0, 30).Select(i => new LazyCardContent
        {
            Group = nameof(Cards_are_built_near_the_screen_and_dropped_far_from_it_without_moving),
            PlaceholderHeight = 100,
            DataContext = $"app {i}",
            ContentTemplate = new FuncDataTemplate<string>((_, _) => new Border { Height = 100 }),
        }).ToList();
        var stack = new StackPanel();
        stack.Children.AddRange(cards);
        var scroller = new ScrollViewer { Content = stack };
        var window = new Window { Content = scroller, Width = 400, Height = 300 };
        window.Show();
        void Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }
        Settle();

        // On screen (0-300) and within half a screen below it are built; the rest are not.
        cards.Take(4).Should().OnlyContain(c => c.IsBuilt);
        cards.Skip(6).Should().OnlyContain(c => !c.IsBuilt);
        stack.Bounds.Height.Should().Be(3000, "cards not built yet still take their place");

        scroller.Offset = new Vector(0, 2000);
        Settle();
        cards.Skip(20).Take(3).Should().OnlyContain(c => c.IsBuilt);
        cards.Take(10).Should().OnlyContain(c => !c.IsBuilt, "cards well above the screen are let go");
        stack.Bounds.Height.Should().Be(3000);
    }
}
