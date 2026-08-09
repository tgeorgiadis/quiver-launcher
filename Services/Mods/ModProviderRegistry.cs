using QuiverLauncher.Services.Mods.Providers.GameBanana;
using QuiverLauncher.Services.Mods.Providers.Thunderstore;

namespace QuiverLauncher.Services.Mods;

public sealed class ModProviderRegistry
{
    private readonly Dictionary<string, IModProvider> _providers;

    public ModProviderRegistry(HttpClient httpClient, string cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        var thunderstore = new ThunderstoreModProvider(httpClient, cacheDirectory);
        var gameBanana = new GameBananaModProvider(httpClient, cacheDirectory);
        _providers = new Dictionary<string, IModProvider>(StringComparer.OrdinalIgnoreCase)
        {
            [thunderstore.Id] = thunderstore,
            [gameBanana.Id] = gameBanana,
        };
    }

    public ModProviderRegistry(IEnumerable<IModProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IModProvider> All => _providers.Values.OrderBy(p => p.DisplayName).ToList();

    public bool TryGet(string? providerId, out IModProvider provider)
    {
        provider = null!;
        if (string.IsNullOrWhiteSpace(providerId))
            return false;

        return _providers.TryGetValue(providerId.Trim(), out provider!);
    }

    public IModProvider? GetOrNull(string? providerId) =>
        TryGet(providerId, out var provider) ? provider : null;
}
