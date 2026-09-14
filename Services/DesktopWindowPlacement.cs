using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;

namespace QuiverLauncher.Services;

/// <summary>Local desktop geometry: logical client size and physical screen position.</summary>
[JsonConverter(typeof(DesktopWindowPlacementConverter))]
public sealed record DesktopWindowPlacement(double Width, double Height, int X, int Y, bool Maximized)
{
    internal bool IsValid => double.IsFinite(Width) && double.IsFinite(Height) &&
        Width > 0 && Height > 0 && Width <= 100000 && Height <= 100000;
}

// A damaged optional placement must not make FileSettingsStore discard all preferences.
public sealed class DesktopWindowPlacementConverter : JsonConverter<DesktopWindowPlacement>
{
    public override DesktopWindowPlacement? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (!Number(value, "Width", out var width) || !Number(value, "Height", out var height) ||
            !Integer(value, "X", out var x) || !Integer(value, "Y", out var y) ||
            !value.TryGetProperty("Maximized", out var maximized) ||
            maximized.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        var placement = new DesktopWindowPlacement(width, height, x, y, maximized.GetBoolean());
        return placement.IsValid ? placement : null;
    }

    private static bool Number(JsonElement value, string key, out double result)
    {
        result = 0;
        return value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetDouble(out result);
    }

    private static bool Integer(JsonElement value, string key, out int result)
    {
        result = 0;
        return value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out result);
    }

    public override void Write(Utf8JsonWriter writer, DesktopWindowPlacement value, JsonSerializerOptions options)
    {
        if (!value.IsValid) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteNumber("Width", value.Width);
        writer.WriteNumber("Height", value.Height);
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteBoolean("Maximized", value.Maximized);
        writer.WriteEndObject();
    }
}

internal readonly record struct PlacementScreen(PixelRect Bounds, PixelRect WorkArea, double Scaling, bool Primary);

internal static class DesktopPlacementGeometry
{
    internal static DesktopWindowPlacement? Restore(DesktopWindowPlacement? saved,
        IReadOnlyList<PlacementScreen> screens, int interfaceScale, Size frameExtra)
    {
        if (saved is not { IsValid: true } || screens.Count == 0) return null;
        var screen = screens.FirstOrDefault(s => s.Primary);
        if (screen == default) screen = screens[0];
        double bestOverlap = 0;
        foreach (var candidate in screens)
        {
            // The saved size is logical; evaluate it at each monitor's current DPI.
            var right = saved.X + (saved.Width + frameExtra.Width) * candidate.Scaling;
            var bottom = saved.Y + (saved.Height + frameExtra.Height) * candidate.Scaling;
            var overlap = Math.Max(0, Math.Min(right, candidate.Bounds.Right) - Math.Max(saved.X, candidate.Bounds.X)) *
                Math.Max(0, Math.Min(bottom, candidate.Bounds.Bottom) - Math.Max(saved.Y, candidate.Bounds.Y));
            if (overlap > bestOverlap) { screen = candidate; bestOverlap = overlap; }
        }
        var area = new Size(Math.Max(1, screen.WorkArea.Width / screen.Scaling - frameExtra.Width),
            Math.Max(1, screen.WorkArea.Height / screen.Scaling - frameExtra.Height));
        var scale = InterfaceScale.Fit(interfaceScale, area) / 100d;
        var width = Math.Min(area.Width, Math.Max(750 * scale, saved.Width));
        var height = Math.Min(area.Height, Math.Max(490 * scale, saved.Height));
        var frameWidth = (int)Math.Ceiling((width + frameExtra.Width) * screen.Scaling);
        var frameHeight = (int)Math.Ceiling((height + frameExtra.Height) * screen.Scaling);
        var work = screen.WorkArea;
        var x = bestOverlap > 0 ? saved.X : work.X + (work.Width - frameWidth) / 2;
        var y = bestOverlap > 0 ? saved.Y : work.Y + (work.Height - frameHeight) / 2;
        return saved with { Width = width, Height = height,
            X = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - frameWidth)),
            Y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - frameHeight)) };
    }
}
