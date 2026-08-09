namespace QuiverLauncher.Services.Mods;

/// <summary>Catalog entry describing one remote mod source for an app.</summary>
public sealed class GameModSource
{
    public string Provider { get; set; } = ModProviderIds.Thunderstore;
    public string SourceUrl { get; set; } = string.Empty;
}
