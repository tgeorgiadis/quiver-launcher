namespace QuiverLauncher.Services.Mods;

/// <summary>Helpers for normalizing and comparing an app's mods catalog config.</summary>
public static class GameModsConfig
{
    public const string LayoutFlat = "flat";
    public const string LayoutFolderPerMod = "folderPerMod";

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Length == 0)
            return string.Empty;

        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(s => s is "." or ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return string.Empty;

        return string.Join('/', segments);
    }

    public static string NormalizeLayout(string? layout)
    {
        if (string.IsNullOrWhiteSpace(layout))
            return LayoutFlat;

        var trimmed = layout.Trim();
        if (string.Equals(trimmed, LayoutFolderPerMod, StringComparison.OrdinalIgnoreCase))
            return LayoutFolderPerMod;

        return LayoutFlat;
    }

    public static bool IsFolderPerMod(string? layout) =>
        string.Equals(NormalizeLayout(layout), LayoutFolderPerMod, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a filesystem-safe folder name for wrapping a flat mod archive.
    /// Priority: download filename (no extension), package name, package id, then "mod".
    /// </summary>
    public static string ResolveWrapFolderName(
        string? downloadFileName,
        string? packageName,
        string? packageId)
    {
        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            var fromFile = SanitizeFolderName(Path.GetFileNameWithoutExtension(downloadFileName.Trim()));
            if (fromFile.Length > 0)
                return fromFile;
        }

        var fromName = SanitizeFolderName(packageName);
        if (fromName.Length > 0)
            return fromName;

        var fromId = SanitizeFolderName(packageId);
        return fromId.Length > 0 ? fromId : "mod";
    }

    public static string SanitizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var trimmed = name.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var chars = trimmed.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim('.', ' ');
        if (sanitized is "." or "..")
            return string.Empty;

        return sanitized;
    }

    public static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return ModProviderIds.Thunderstore;

        return provider.Trim().ToLowerInvariant();
    }

    public static List<GameModSource> NormalizeSources(IEnumerable<GameModSource>? sources)
    {
        if (sources == null)
            return [];

        var result = new List<GameModSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source == null)
                continue;

            var url = source.SourceUrl?.Trim() ?? string.Empty;
            if (url.Length == 0)
                continue;

            var provider = NormalizeProvider(source.Provider);
            var key = $"{provider}|{url}";
            if (!seen.Add(key))
                continue;

            result.Add(new GameModSource
            {
                Provider = provider,
                SourceUrl = url,
            });
        }

        return result;
    }

    public static bool AreEquivalent(
        string? pathA,
        IEnumerable<GameModSource>? sourcesA,
        string? pathB,
        IEnumerable<GameModSource>? sourcesB) =>
        AreEquivalent(pathA, sourcesA, null, pathB, sourcesB, null);

    public static bool AreEquivalent(
        string? pathA,
        IEnumerable<GameModSource>? sourcesA,
        string? layoutA,
        string? pathB,
        IEnumerable<GameModSource>? sourcesB,
        string? layoutB)
    {
        if (!string.Equals(NormalizePath(pathA), NormalizePath(pathB), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(NormalizeLayout(layoutA), NormalizeLayout(layoutB), StringComparison.OrdinalIgnoreCase))
            return false;

        var a = NormalizeSources(sourcesA)
            .Select(s => $"{s.Provider}|{s.SourceUrl}")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var b = NormalizeSources(sourcesB)
            .Select(s => $"{s.Provider}|{s.SourceUrl}")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
    }

    public static string FormatForDisplay(string? path, IEnumerable<GameModSource>? sources, string? layout = null)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedSources = NormalizeSources(sources);
        var normalizedLayout = NormalizeLayout(layout);
        if (normalizedPath.Length == 0 && normalizedSources.Count == 0 && normalizedLayout == LayoutFlat)
            return string.Empty;

        var parts = new List<string>();
        if (normalizedPath.Length > 0)
            parts.Add($"path={normalizedPath}");
        if (normalizedLayout != LayoutFlat)
            parts.Add($"layout={normalizedLayout}");

        var sourceText = string.Join("; ", normalizedSources.Select(s => $"{s.Provider}: {s.SourceUrl}"));
        if (sourceText.Length > 0)
            parts.Add(sourceText);

        return string.Join("; ", parts);
    }

    public static bool HasUsableConfig(string? path, IEnumerable<GameModSource>? sources, ModProviderRegistry? registry = null)
    {
        if (NormalizePath(path).Length == 0)
            return false;

        var normalized = NormalizeSources(sources);
        if (normalized.Count == 0)
            return false;

        if (registry == null)
            return true;

        return normalized.Any(s => registry.TryGet(s.Provider, out _));
    }
}
