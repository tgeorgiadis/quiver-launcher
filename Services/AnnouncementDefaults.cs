namespace QuiverLauncher.Services;

/// <summary>
/// Remote announcement banner. Edit announcement.json on the Quiver Launcher repo main branch
/// and push to publish without shipping a Quiver Launcher release. Change <c>id</c> to re-show
/// the banner for users who dismissed a previous notice.
/// </summary>
public static class AnnouncementDefaults
{
    public const string RemoteUrl =
        "https://raw.githubusercontent.com/tgeorgiadis/quiver-launcher/main/announcement.json";
}
