using System.Diagnostics;

namespace QuiverLauncher.Core.Services;

/// <summary>Opt-in aggregate timings. Never records repositories, filenames, URLs or credentials.</summary>
public static class CatalogPerformance
{
    public static bool Enabled { get; set; }
    public static IDisposable Measure(string operation, int count = 0) => Enabled
        ? new Measurement(operation, count) : Empty.Instance;
    public static void Report(string operation, double milliseconds, int count = 0)
    {
        if (Enabled) Console.WriteLine($"CATALOG_PERF {operation} ms={milliseconds:F2} count={count} thread={Environment.CurrentManagedThreadId}");
    }
    private sealed class Measurement(string operation, int count) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        public void Dispose() => Report(operation, Stopwatch.GetElapsedTime(_start).TotalMilliseconds, count);
    }
    private sealed class Empty : IDisposable { public static readonly Empty Instance = new(); public void Dispose() { } }
}
