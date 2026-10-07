namespace QuiverLauncher.Services
{
    /// <summary>
    /// Centralized eligibility for app-update review modals.
    /// Quiver launcher self-update prompts are always allowed separately.
    /// </summary>
    public static class UpdatePromptPolicy
    {
        public static bool ShouldPromptAppUpdateReviews(AppSettings? settings, bool kioskLocked = false) =>
            !kioskLocked && settings?.PromptAppUpdateReviews == true;

        public static bool ShouldPromptLauncherSelfUpdate(bool kioskLocked) => !kioskLocked;
    }
}
