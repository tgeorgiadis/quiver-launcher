namespace QuiverLauncher.Services;

[Flags]
public enum SettingsChange
{
    Presentation = 0,
    Layout = 1,
    LibraryDisplay = 2,
    Sorting = 4,
    Tray = 8,
    Input = 16,
    Badges = 32,
}

public enum CardLayoutPreset { Landscape, Portrait, Square, SquareCompact, List }
