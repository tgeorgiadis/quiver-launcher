namespace QuiverLauncher.Core.Services
{
    public class LauncherProfile
    {
        public static LauncherProfile Default { get; } = new();

        public virtual string DisplayName => "GitHub Launcher";
        public virtual string ApplicationId => "GitHubLauncher";
        public virtual string Repository => "SirDiabo/GitHubLauncher";
        public virtual string ExecutableName => "GitHubLauncher";
        public virtual string DefaultInstallFolderName => "Apps";
        public virtual string UserAgent => "GitHubLauncher/1.0";
        public virtual string CliUserAgent => "GitHubLauncher-CLI";
        public virtual string UpdaterUserAgent => "GitHubLauncher-Updater";
        public virtual string SteamTag => DisplayName;
        public virtual void ConfigureInstalledApp(string appPath)
        {
        }
    }
}
