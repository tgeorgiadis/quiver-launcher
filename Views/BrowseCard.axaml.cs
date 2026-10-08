using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>An app's card, drawn like the website's catalog card: in the App Catalog and on a game's page.</summary>
public partial class BrowseCard : UserControl
{
    /// <summary>
    /// The card's button was pressed: add its app to the library without opening its page, or, for an app already in the
    /// library, show it there.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> AddRequestedEvent =
        RoutedEvent.Register<BrowseCard, RoutedEventArgs>("AddRequested", RoutingStrategies.Bubble);

    public BrowseCard() => InitializeComponent();

    /// <summary>A tap on the Add button adds the app; it doesn't also open the app's page.</summary>
    public static bool IsOnAddButton(RoutedEventArgs e) =>
        e.Source is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true) is { Name: "BrowseCardAddButton" };

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(AddRequestedEvent, this));
    }

    // Cut-off text scrolls while the card is hovered.
    private void Card_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is BrowseItem item) item.IsHovered = true;
    }

    private void Card_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is BrowseItem item) item.IsHovered = false;
    }
}
