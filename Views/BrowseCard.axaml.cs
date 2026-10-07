using Avalonia.Controls;
using Avalonia.Input;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Views;

/// <summary>An app's card, drawn like the website's catalog card: in the App Catalog and on a game's page.</summary>
public partial class BrowseCard : UserControl
{
    public BrowseCard() => InitializeComponent();

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
