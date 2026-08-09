using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Services
{
    public sealed class QuiverLauncherProfile : LauncherProfile
    {
        public static QuiverLauncherProfile Instance { get; } = new();

        public override string DisplayName => "Quiver Launcher";
        public override string ApplicationId => "QuiverLauncher";
        public override string Repository => "tgeorgiadis/quiver-launcher";
        public override string ExecutableName => "QuiverLauncher";
        public override string DefaultInstallFolderName => "Apps";
        public override string UserAgent => "QuiverLauncher/1.0";
        public override string CliUserAgent => "QuiverLauncher-CLI";
        public override string UpdaterUserAgent => "QuiverLauncher-Updater";
        public override string SteamTag => "QuiverLauncher";
    }
}