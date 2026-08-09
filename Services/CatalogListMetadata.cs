namespace QuiverLauncher.Services;

public static class CatalogListMetadata
{
    public static string DeriveDisplayNameFromLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return "";

        var trimmed = location.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var path = uri.AbsolutePath;
            if (!string.IsNullOrWhiteSpace(path))
                trimmed = path;
        }

        var fileName = Path.GetFileNameWithoutExtension(trimmed);
        if (string.IsNullOrWhiteSpace(fileName))
            return "";

        return fileName.Replace('-', ' ');
    }
}
