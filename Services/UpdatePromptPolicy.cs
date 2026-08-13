namespace QuiverLauncher.Services
{
    /// <summary>
    /// Centralized eligibility for catalog and app-update review modals.
    /// Quiver launcher self-update prompts are always allowed separately.
    /// </summary>
    public static class UpdatePromptPolicy
    {
        public static bool ShouldPromptCatalogUpdates(AppSettings? settings) =>
            settings?.PromptCatalogUpdates == true;

        public static bool ShouldPromptAppUpdateReviews(AppSettings? settings) =>
            settings?.PromptAppUpdateReviews == true;

        public static bool ShouldShowLibraryAppUpdateBadges(AppSettings? settings) =>
            settings?.ShowLibraryAppUpdateBadges != false;
    }
}
