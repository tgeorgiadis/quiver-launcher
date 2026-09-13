namespace QuiverLauncher.Services;

/// <summary>Optional profiler hook. No app identities, credentials, or logging by default.</summary>
public static class CatalogAddDiagnostics
{
    public sealed record Timing(double FeedbackMs, double PersistenceMs, double LibraryMs, double ReconciliationMs, double TotalMs);
    public static event Action<Timing>? Completed;
    internal static void Report(Timing timing)
    {
        try { Completed?.Invoke(timing); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Catalog Add profiler failed: {ex.Message}"); }
    }
}
