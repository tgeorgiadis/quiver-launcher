namespace QuiverLauncher.Services;

public static class GitHubTokenSetupGuide
{
    public const string CreateTokenUrl = "https://github.com/settings/tokens/new?description=Github-Launcher+Token+for+increased+API+rate+limits";
    public const string Introduction = "For public apps:";
    public const string CreateStep = "1. Select Create token and sign in to GitHub.";
    public const string ExpirationStep = "2. Name the token and choose an expiration date.";
    public const string PermissionsStep = "3. Leave all scope checkboxes unchecked. Public releases need no additional permissions.";
    public const string GenerateStep = "4. Select Generate token and copy it.";
    public const string SaveStep = "5. Paste it in Settings → Advanced → GitHub API Token and select Save token.";
    public const string Privacy = "Keep the token private. When it expires, update with a new token.";
}
