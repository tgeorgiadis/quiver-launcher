using System.Text.Json;

namespace QuiverLauncher.Services;

public sealed class AnnouncementPayload
{
    public string Id { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public string Message { get; set; } = "";
}

public static class AnnouncementService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AnnouncementPayload? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<AnnouncementPayload>(json, JsonOptions);
            return IsDisplayable(payload) ? payload : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsDisplayable(AnnouncementPayload? payload)
    {
        if (payload == null || !payload.Enabled)
            return false;

        if (string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.Message))
            return false;

        return true;
    }

    public static bool ShouldShow(AnnouncementPayload? payload, IEnumerable<string>? dismissedIds)
    {
        if (!IsDisplayable(payload))
            return false;

        if (dismissedIds == null)
            return true;

        return !dismissedIds.Any(id =>
            string.Equals(id, payload!.Id, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<AnnouncementPayload?> TryFetchAsync(
        HttpClient httpClient,
        string? url = null,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(url) ? AnnouncementDefaults.RemoteUrl : url;
        try
        {
            using var response = await httpClient
                .GetAsync(target, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return TryParse(json);
        }
        catch
        {
            return null;
        }
    }
}
