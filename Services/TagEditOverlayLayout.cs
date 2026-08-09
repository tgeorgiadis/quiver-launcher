using Avalonia;
using Avalonia.Layout;

namespace QuiverLauncher.Services;

/// <summary>
/// Layout/focus helpers for Edit Tags so Steam Deck Gaming Mode OSK
/// does not cover Cancel/Save.
/// </summary>
internal static class TagEditOverlayLayout
{
    public static readonly Thickness GamingModeMargin = new(24, 48, 24, 24);

    public static int GetInitialFocusIndex(bool isGamingMode) => isGamingMode ? 1 : 0;

    public static void ApplyDialogPlacement(Avalonia.Controls.Control? dialogPanel, bool isGamingMode)
    {
        if (dialogPanel == null)
            return;

        if (isGamingMode)
        {
            dialogPanel.VerticalAlignment = VerticalAlignment.Top;
            dialogPanel.Margin = GamingModeMargin;
        }
        else
        {
            dialogPanel.VerticalAlignment = VerticalAlignment.Center;
            dialogPanel.Margin = default;
        }
    }
}
