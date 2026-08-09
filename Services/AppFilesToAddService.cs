using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public static class AppFilesToAddService
{
    public static List<string> Normalize(IEnumerable<string>? fileNames)
    {
        if (fileNames == null)
            return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in fileNames)
        {
            if (!TryNormalizeFileName(raw, out var fileName))
                continue;

            if (seen.Add(fileName))
                result.Add(fileName);
        }

        return result;
    }

    public static List<string> ParseCommaSeparated(string? input) =>
        Normalize(input?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string FormatForDisplay(IEnumerable<string>? fileNames)
    {
        var normalized = Normalize(fileNames);
        return normalized.Count == 0 ? string.Empty : string.Join(", ", normalized);
    }

    public static bool AreEquivalent(IEnumerable<string>? a, IEnumerable<string>? b) =>
        string.Equals(FormatForDisplay(a), FormatForDisplay(b), StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizeFileName(string? raw, out string fileName)
    {
        fileName = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed is "." or ".." ||
            trimmed.Contains('/') ||
            trimmed.Contains('\\') ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        fileName = trimmed;
        return true;
    }

    public static void Sync(string? installPath, IEnumerable<string>? previous, IEnumerable<string>? next)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return;

        var previousNames = Normalize(previous);
        var nextNames = Normalize(next);
        var nextSet = new HashSet<string>(nextNames, StringComparer.OrdinalIgnoreCase);

        foreach (var removed in previousNames.Where(name => !nextSet.Contains(name)))
        {
            var path = Path.Combine(installPath, removed);
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup; leave the file if locked or inaccessible.
            }
        }

        foreach (var fileName in nextNames)
        {
            var path = Path.Combine(installPath, fileName);
            try
            {
                if (!File.Exists(path))
                    File.WriteAllText(path, string.Empty);
            }
            catch
            {
                // Best-effort create; skip if the path cannot be written.
            }
        }
    }

    public static void SyncForGame(GameInfo game, string appsFolder, IEnumerable<string>? previous = null)
    {
        if (string.IsNullOrWhiteSpace(appsFolder) && string.IsNullOrWhiteSpace(game.InstallPath))
            return;

        var installPath = game.GetInstallPath(appsFolder);
        Sync(installPath, previous, game.FilesToAdd);
    }
}
