using Android.Graphics;
using Android.Views;

namespace QuiverLauncher.Android;

/// <summary>Reserves a thumb-height section of the left edge for the drawer on gesture-navigation devices.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
internal sealed class SidebarGestureExclusion(View view, Func<bool> canOpen)
    : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
{
    private (int Width, int Height, bool Enabled)? _last;

    public bool OnPreDraw()
    {
        var state = (view.Width, view.Height, canOpen());
        if (_last == state) return true;
        _last = state;
        var density = view.Resources?.DisplayMetrics?.Density ?? 1;
        // Android allows at most 200dp of edge exclusion. Keep the rest, and the
        // entire right edge, available for Back. Use the lower-middle thumb area.
        var height = Math.Min(view.Height, (int)(200 * density));
        var top = Math.Clamp((int)(view.Height * 0.65) - height / 2, 0, Math.Max(0, view.Height - height));
        view.SystemGestureExclusionRects = state.Item3
            ? new[] { new Rect(0, top, (int)(24 * density), top + height) }
            : Array.Empty<Rect>();
        return true;
    }
}
