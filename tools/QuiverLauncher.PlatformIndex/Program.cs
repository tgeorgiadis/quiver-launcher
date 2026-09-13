using System.Text.Json;
using QuiverLauncher.Core.Services;

// Tokens are accepted only through the environment, never command-line arguments or output.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: QuiverLauncher.PlatformIndex <catalog-directory|--validate> <platform-index.json>");
    return 2;
}
try
{
    if (args[0] == "--validate")
    {
        var valid = PublishedPlatformDocument.Parse(await File.ReadAllTextAsync(args[1]));
        Console.WriteLine($"Validated {valid.Entries.Count} platform entries.");
        return 0;
    }
    var targets = new List<PublishedPlatformTarget>();
    foreach (var path in Directory.EnumerateFiles(args[0], "*.json").Order())
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        foreach (var app in json.RootElement.GetProperty("apps").EnumerateArray())
        {
            if (!app.TryGetProperty("repository", out var repository) || string.IsNullOrWhiteSpace(repository.GetString())) continue;
            var provider = app.TryGetProperty("repositorySource", out var source) ? source.GetString() : "github";
            if (provider is not (null or "github" or "gitlab")) throw new JsonException();
            var preferred = app.TryGetProperty("preferredVersion", out var version) ? version.GetString() : null;
            targets.Add(new(provider ?? "github", repository.GetString()!, preferred));
        }
    }
    if (targets.Count == 0) throw new JsonException();
    var previous = File.Exists(args[1]) ? PublishedPlatformDocument.Parse(await File.ReadAllTextAsync(args[1])) : null;
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("QuiverLauncher-PlatformIndex/1.0");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var result = await PlatformMetadataPublisher.GenerateAsync(client, targets, previous,
        provider => Environment.GetEnvironmentVariable(provider == "gitlab" ? "GITLAB_TOKEN" : "GITHUB_TOKEN"), cancellation.Token);
    var summary = $"Platform metadata: {result.Successful} validated, {result.Failures.Count} failed, {result.Document.Entries.Count} available.\n";
    Console.WriteLine(summary.Trim());
    if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summaryPath)
        await File.AppendAllTextAsync(summaryPath, summary + string.Join("\n", result.Failures.Select(f => "- " + f.Replace("\n", " ").Replace("\r", " "))) + "\n");
    // A complete outage is a failed generation, not a new successful publication.
    if (result.Successful == 0) return 1;
    result.Document.WriteAtomically(args[1]);
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Generation cancelled; previous index retained."); return 1; }
catch (Exception) { Console.Error.WriteLine("Index generation or validation failed; previous index retained."); return 1; }
