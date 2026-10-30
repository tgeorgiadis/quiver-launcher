using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace QuiverLauncher.Core.Services;

public static class GameInstallationService
{
    public const string IncompleteInstallFileName = "install-incomplete.txt";

    /// <summary>
    /// True for single-file release binaries: .exe, .appimage, a Linux .run setup or .elf program, or extensionless
    /// (e.g. CrashBandicoot_Linux, P.T.PC.Port.Setup-linux, Game.x86_64).
    /// </summary>
    public static bool IsSingleFileExecutableAsset(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
            return false;

        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            assetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase) ||
            assetName.EndsWith(".run", StringComparison.OrdinalIgnoreCase) ||
            assetName.EndsWith(".elf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !HasFileTypeExtension(Path.GetFileName(assetName));
    }

    private static readonly string[] ArchitectureSuffixes = ["x86_64", "x86", "x64", "amd64", "i386", "i686", "arm64", "aarch64"];

    /// <summary>
    /// Whether the text after the last dot reads as a file type. Dots elsewhere in a binary's name
    /// ("P.T.PC.Port.Setup-linux") or an architecture suffix ("Game.x86_64") don't make it one.
    /// </summary>
    private static bool HasFileTypeExtension(string name)
    {
        var extension = Path.GetExtension(name).TrimStart('.');
        if (extension.Length == 0)
            return false;
        if (ArchitectureSuffixes.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return false;
        return extension.All(char.IsAsciiLetterOrDigit);
    }

    /// <summary>
    /// Prefers a Content-Disposition filename when the release asset display name lacks a usable extension
    /// (common for GitLab package links named like <c>LADXHD.Patcher-Lite-Windows</c>).
    /// </summary>
    public static string ResolveEffectiveAssetName(string? assetName, string? contentDispositionFileName)
    {
        var fromDisposition = NormalizeDownloadFileName(contentDispositionFileName);
        var fromAsset = NormalizeDownloadFileName(assetName);

        // Prefer disposition when it carries a usable extension and the release link name does not
        // (GitLab package links often use display names like "LADXHD.Patcher-Lite-Windows").
        if (HasRecognizedInstallExtension(fromDisposition) && !HasRecognizedInstallExtension(fromAsset))
            return fromDisposition;

        if (!string.IsNullOrEmpty(fromAsset))
            return fromAsset;

        if (!string.IsNullOrEmpty(fromDisposition))
            return fromDisposition;

        return "download.bin";
    }

    public static bool HasRecognizedInstallExtension(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
            return false;

        return assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
               IsTarballAsset(assetName) ||
               assetName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
               IsWindowsInstallerAsset(assetName) ||
               assetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".run", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".elf", StringComparison.OrdinalIgnoreCase) ||
               IsDebianPackageAsset(assetName) ||
               assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || IsFlatpakAsset(assetName) ||
               IsDiskImageAsset(assetName);
    }

    static readonly string[] TarballExtensions = [".tar", ".tar.gz", ".tgz", ".tar.xz", ".txz", ".tar.bz2", ".tbz2", ".tar.zst", ".tzst"];

    /// <summary>A tar archive, plain or compressed with gzip, xz, bzip2 or zstd (e.g. Game-linux-x64.tar.xz).</summary>
    public static bool IsTarballAsset(string? assetName) =>
        !string.IsNullOrWhiteSpace(assetName) &&
        TarballExtensions.Any(extension => assetName.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    static bool IsGzipTarball(string name) =>
        name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Debian/Ubuntu package (e.g. gzdoom_4.14.2_amd64.deb).</summary>
    public static bool IsDebianPackageAsset(string? assetName) =>
        !string.IsNullOrWhiteSpace(assetName) && assetName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase);

    public static bool IsDiskImageAsset(string? assetName) =>
        !string.IsNullOrWhiteSpace(assetName) && assetName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase);

    public static bool IsAndroidPackageAsset(string? assetName)
        => !string.IsNullOrWhiteSpace(assetName)
           && assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);

    public static bool IsWindowsInstallerAsset(string? assetName) =>
        !string.IsNullOrWhiteSpace(assetName) && assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

    /// <summary>A Flatpak bundle, or a zip holding one (e.g. Zelda64Recompiled-v1.2.2-Linux-Flatpak-X64.zip).</summary>
    public static bool IsFlatpakAsset(string? assetName) =>
        !string.IsNullOrWhiteSpace(assetName) && (assetName.EndsWith(".flatpak", StringComparison.OrdinalIgnoreCase) ||
            IsZippedFlatpakAsset(assetName));

    public static bool IsZippedFlatpakAsset(string? assetName) =>
        !string.IsNullOrWhiteSpace(assetName) && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        assetName.Contains("flatpak", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts the one .flatpak bundle in <paramref name="zipPath"/> into <paramref name="destinationDirectory"/>.</summary>
    public static string ExtractZippedFlatpak(string zipPath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var bundles = archive.Entries
            .Where(entry => entry.Name.EndsWith(".flatpak", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (bundles.Count != 1)
            throw new InvalidOperationException(bundles.Count == 0
                ? $"'{Path.GetFileName(zipPath)}' doesn't contain a Flatpak bundle."
                : $"'{Path.GetFileName(zipPath)}' contains more than one Flatpak bundle.");

        Directory.CreateDirectory(destinationDirectory);
        var bundlePath = Path.Combine(destinationDirectory, bundles[0].Name);
        bundles[0].ExtractToFile(bundlePath, overwrite: true);
        return bundlePath;
    }

    public static bool IsAppImageAsset(string? assetName)
        => !string.IsNullOrWhiteSpace(assetName)
           && assetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// After a successful AppImage install, delete the AppImages it replaces: in a folder Quiver made for the app,
    /// every other top-level <c>.AppImage</c> left behind by versioned release names; in a shared folder, only the
    /// file Quiver last installed the app from. Does not recurse, and does not fail the update if a stale file is
    /// locked. Points <c>selected_executable.txt</c> at the new file when it named a missing path or another
    /// top-level AppImage, or when other AppImages remain beside it.
    /// </summary>
    public static void RemoveStaleAppImages(string gamePath, string keepPath, GameInstallationOptions? options = null)
    {
        options ??= GameInstallationOptions.Default;

        if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(keepPath))
            return;

        if (!Directory.Exists(gamePath) || !File.Exists(keepPath))
            return;

        var keepFullPath = Path.GetFullPath(keepPath);

        foreach (var file in StaleAppImages(gamePath, options))
        {
            if (string.Equals(Path.GetFullPath(file), keepFullPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Log(options, $"Warning: failed to delete stale AppImage '{file}': {ex.Message}");
            }
        }

        RetargetSelectedExecutable(gamePath, keepFullPath, options);
    }

    static IEnumerable<string> StaleAppImages(string gamePath, GameInstallationOptions options)
    {
        if (!options.IsSharedFolder)
            return TopLevelAppImages(gamePath);

        // A shared folder can hold the player's other AppImages, Quiver's own among them.
        var previous = Path.GetFileName(options.PreviousInstallFileName ?? string.Empty);
        return IsAppImageAsset(previous) ? [Path.Combine(gamePath, previous)] : [];
    }

    static IEnumerable<string> TopLevelAppImages(string gamePath) =>
        Directory.GetFiles(gamePath, "*", SearchOption.TopDirectoryOnly).Where(file => IsAppImageAsset(file));

    static void RetargetSelectedExecutable(string gamePath, string keepPath, GameInstallationOptions options)
    {
        var selectedPath = Path.Combine(gamePath, "selected_executable.txt");

        try
        {
            var saved = File.Exists(selectedPath) ? File.ReadAllText(selectedPath).Trim() : string.Empty;
            if (saved.Length > 0 && File.Exists(saved))
            {
                // A wrapper script or another chosen program stays; an older AppImage beside the new one doesn't.
                var savedFolder = Path.GetDirectoryName(Path.GetFullPath(saved));
                if (!IsAppImageAsset(saved) || !string.Equals(Path.TrimEndingDirectorySeparator(savedFolder ?? string.Empty),
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath)), StringComparison.OrdinalIgnoreCase))
                    return;
            }
            else if (!File.Exists(selectedPath) && !TopLevelAppImages(gamePath).Any(file =>
                         !string.Equals(Path.GetFullPath(file), keepPath, StringComparison.OrdinalIgnoreCase)))
            {
                // Nothing chosen and nothing else to choose from: launching finds the new file on its own.
                return;
            }

            File.WriteAllText(selectedPath, keepPath);
        }
        catch (Exception ex)
        {
            Log(options, $"Warning: failed to update selected_executable.txt: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects zip / 7z / gzip / rar from the file header when the asset name has no usable extension.
    /// </summary>
    public static string? DetectArchiveExtensionFromFile(string downloadPath)
    {
        try
        {
            using var stream = File.OpenRead(downloadPath);
            Span<byte> header = stackalloc byte[4];
            var read = stream.Read(header);
            if (read < 2)
                return null;

            if (header[0] == (byte)'P' && header[1] == (byte)'K')
                return ".zip";

            if (read >= 4 &&
                header[0] == 0x37 &&
                header[1] == 0x7A &&
                header[2] == 0xBC &&
                header[3] == 0xAF)
            {
                return ".7z";
            }

            // RAR4: 52 61 72 21 1A 07 00; RAR5: 52 61 72 21 1A 07 01 00
            if (read >= 4 &&
                header[0] == 0x52 &&
                header[1] == 0x61 &&
                header[2] == 0x72 &&
                header[3] == 0x21)
            {
                return ".rar";
            }

            if (header[0] == 0x1F && header[1] == 0x8B)
                return ".tar.gz";
        }
        catch
        {
            return null;
        }

        return null;
    }

    static string NormalizeDownloadFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var trimmed = fileName.Trim().Trim('"');
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }

    public static async Task InstallOrUpdateGameAsync(
        string downloadPath,
        string gamePath,
        string assetName,
        string version,
        GameInstallationOptions? options = null)
    {
        options ??= GameInstallationOptions.Default;
        Directory.CreateDirectory(gamePath);

        var effectiveName = assetName;
        // Names like LADXHD.Patcher-Lite-Windows are archives and P.T.PC.Port.Setup-linux is a binary,
        // so anything without a known extension is checked for an archive before being kept as one file.
        if (!HasRecognizedInstallExtension(effectiveName))
        {
            var detected = DetectArchiveExtensionFromFile(downloadPath);
            if (!string.IsNullOrEmpty(detected))
                effectiveName = effectiveName + detected;
        }

        if (IsAndroidPackageAsset(effectiveName))
        {
            if (!OperatingSystem.IsAndroid())
            {
                throw new InvalidOperationException(
                    $"Android package '{assetName}' cannot be installed on this desktop platform.");
            }

            var destPath = Path.Combine(gamePath, Path.GetFileName(downloadPath));
            File.Copy(downloadPath, destPath, true);
            await File.WriteAllTextAsync(Path.Combine(gamePath, "version.txt"), version).ConfigureAwait(false);
            return;
        }

        if (IsSingleFileExecutableAsset(effectiveName))
        {
            var destName = Path.GetFileName(assetName);
            if (string.IsNullOrWhiteSpace(destName))
                destName = assetName;

            var destPath = Path.Combine(gamePath, destName);
            File.Move(downloadPath, destPath, true);
            MakeExecutableIfNeeded(destPath);

            if (IsAppImageAsset(destName) || IsAppImageAsset(effectiveName))
                RemoveStaleAppImages(gamePath, destPath, options);
        }
        else if (IsDiskImageAsset(effectiveName))
        {
            if (!OperatingSystem.IsMacOS())
            {
                throw new InvalidOperationException(
                    $"Disk image '{assetName}' can only be installed on macOS.");
            }

            await InstallFromDiskImageAsync(downloadPath, gamePath, options).ConfigureAwait(false);
        }
        else if (effectiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractZipAsync(downloadPath, gamePath, options).ConfigureAwait(false);
        }
        else if (IsTarballAsset(effectiveName))
        {
            await ExtractTarballAsync(downloadPath, gamePath, effectiveName).ConfigureAwait(false);
        }
        else if (IsDebianPackageAsset(effectiveName))
        {
            if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                throw new InvalidOperationException(
                    $"Debian package '{assetName}' can only be installed on Linux.");
            }

            await InstallFromDebianPackageAsync(downloadPath, gamePath, options).ConfigureAwait(false);
        }
        else if (effectiveName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                 effectiveName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractSharpCompressArchiveAsync(downloadPath, gamePath, options).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported release asset type: '{assetName}'. " +
                "Expected .exe, .appimage, .zip, .tar.gz, .tar.xz, .7z, .rar, .dmg, .deb, .run, or an extensionless binary.");
        }

        try
        {
            EnsureExecutableAtRoot(gamePath, options);
        }
        catch (Exception ex)
        {
            Log(options, $"Warning: EnsureExecutableAtRoot failed: {ex.Message}");
        }

        var versionFile = Path.Combine(gamePath, "version.txt");
        await File.WriteAllTextAsync(versionFile, version).ConfigureAwait(false);
        options.ExtractProgress?.Report(1);
    }

    public static void EnsureExecutableAtRoot(string gamePath, GameInstallationOptions? options = null)
    {
        options ??= GameInstallationOptions.Default;

        if (!Directory.Exists(gamePath))
            return;

        while (!HasTopLevelExecutable(gamePath, options))
        {
            var topLevelDirs = Directory.GetDirectories(gamePath, "*", SearchOption.TopDirectoryOnly);
            var topLevelFiles = Directory.GetFiles(gamePath, "*", SearchOption.TopDirectoryOnly)
                .Where(f => !IsLauncherMetadataFile(f, options))
                .ToList();

            var flattened = false;

            // Only hoist a single wrapper folder. Sibling files or directories
            // (e.g. game.toml + recomp-ui/ + psxrecomp/ are part of the app tree.
            if (topLevelDirs.Length == 1 && topLevelFiles.Count == 0)
            {
                var singleDir = topLevelDirs[0];
                var singleDirCandidates = FindExecutableCandidates(singleDir, SearchOption.AllDirectories, options, out _);

                if (singleDirCandidates.Count > 0)
                {
                    try
                    {
                        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                        Directory.Move(singleDir, tempDir);
                        MoveDirectoryContents(tempDir, gamePath);
                        TryDeleteDirectory(tempDir);

                        Log(options, $"Moved contents from subdirectory to root: {singleDir}");
                        flattened = true;
                    }
                    catch (Exception ex)
                    {
                        Log(options, $"Failed to flatten directory structure: {ex.Message}");
                    }
                }
            }

            if (flattened)
                continue;

            var nestedCandidates = FindExecutableCandidates(gamePath, SearchOption.AllDirectories, options, out _)
                .Where(f => !IsInRootDirectory(f, gamePath))
                .ToList();

            if (nestedCandidates.Count > 0 && (topLevelFiles.Count > 0 || topLevelDirs.Length > 1))
            {
                Log(options, "Leaving wrapper folder structure in place because nested executable cannot be safely flattened.");
            }

            return;
        }
    }

    private static readonly HashSet<string> NonExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".dll", ".so", ".json", ".sh", ".exe", ".appimage", ".x86_64", ".arm64", ".aarch64",
        // Images
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico", ".tga", ".dds", ".svg", ".psd",
        // Audio & video
        ".ogg", ".mp3", ".wav", ".flac", ".mid", ".midi", ".mp4", ".mkv", ".webm", ".avi",
        // Game assets, packages, archives
        ".pck", ".pak", ".pk3", ".pk4", ".wad", ".dat", ".vpk", ".bsp", ".zip", ".tar", ".gz", ".xz", ".bz2", ".7z", ".rar", ".zst",
        // Documents & config
        ".xml", ".yaml", ".yml", ".ini", ".cfg", ".toml", ".md", ".pdf", ".html", ".css", ".js", ".ts",
        // Libraries, debug, code, fonts
        ".dylib", ".pdb", ".map", ".ttf", ".otf", ".woff", ".woff2", ".c", ".cpp", ".h", ".hpp", ".cs", ".rs", ".py",
        // Save data
        ".sav", ".save", ".srm"
    };

    public static bool HasCompletePortableInstallation(string path, GameInstallationOptions? options = null)
    {
        if (!Directory.Exists(path) || File.Exists(Path.Combine(path, IncompleteInstallFileName)))
            return false;

        var selectedExePath = Path.Combine(path, "selected_executable.txt");
        if (File.Exists(selectedExePath))
        {
            try
            {
                var saved = File.ReadAllText(selectedExePath).Trim();
                if (IsValidSavedExecutable(saved))
                    return true;
            }
            catch { }
        }

        if (FindExecutableCandidates(path, SearchOption.TopDirectoryOnly, options, out _).Count > 0)
            return true;

        return FindExecutableCandidates(path, SearchOption.AllDirectories, options, out _).Count > 0;
    }

    public static List<string> FindExecutableCandidates(
        string path,
        SearchOption searchOption,
        GameInstallationOptions? options,
        out bool needsWine)
        => FindExecutableCandidates(path, searchOption, options, out needsWine, CurrentDesktopPlatform);

    internal static List<string> FindExecutableCandidates(
        string path,
        SearchOption searchOption,
        GameInstallationOptions? options,
        out bool needsWine,
        OSPlatform platform)
    {
        needsWine = false;

        if (!Directory.Exists(path))
            return [];

        var executables = new List<string>();
        if (IsCompatibilityDirectory(path)) return [];
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            MatchType = MatchType.Simple,
        };
        // Proton's dosdevices/z: points at /. Never follow directory links or
        // inspect Wine/Proton prefixes as if they were part of the game payload.
        string[] FindEntries(string pattern, bool directories = false)
        {
            var entries = new System.IO.Enumeration.FileSystemEnumerable<string>(path,
                (ref System.IO.Enumeration.FileSystemEntry entry) => entry.ToFullPath(), enumeration)
            {
                ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry entry) =>
                    (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !IsCompatibilityDirectory(entry.ToFullPath()),
                ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry entry) =>
                    entry.IsDirectory == directories &&
                    System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, entry.FileName,
                        ignoreCase: OperatingSystem.IsWindows()),
            };
            return entries.ToArray();
        }

        if (platform == OSPlatform.Windows)
        {
            var winFiles = FindEntries("*");
            executables.AddRange(winFiles.Where(f =>
            {
                var fileName = Path.GetFileName(f);
                return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                       fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                       fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            }));
        }
        else if (platform == OSPlatform.OSX)
        {
            executables.AddRange(FindEntries("*.app", directories: true));
            executables.AddRange(FindEntries("*")
                .Where(file => IsLikelyExtensionlessExecutable(file, platform)));

            // A Windows build (e.g. Platform set to Windows for use with CrossOver) is still
            // an installed app; like Linux, fall back to .exe when nothing native exists.
            if (executables.Count == 0)
            {
                var exeFiles = FindEntries("*.exe");
                if (exeFiles.Length > 0)
                {
                    executables.AddRange(exeFiles);
                    needsWine = true;
                }
            }
        }
        else
        {
            var allFiles = FindEntries("*")
                .Where(f => !IsLauncherMetadataFile(f, options)).ToArray();

            executables.AddRange(allFiles.Where(f =>
            {
                var fileName = Path.GetFileName(f).ToLowerInvariant();
                return fileName.EndsWith(".x86_64") ||
                       fileName.EndsWith(".appimage") ||
                       fileName.EndsWith(".arm64") ||
                       fileName.EndsWith(".aarch64");
            }));

            executables.AddRange(allFiles.Where(f =>
            {
                var fileName = Path.GetFileName(f).ToLowerInvariant();

                if (NonExecutableExtensions.Contains(Path.GetExtension(fileName)) || fileName.Contains(".so."))
                {
                    return false;
                }

                return IsLikelyNativeExecutable(f, platform);
            }));

            if (executables.Count == 0)
            {
                var exeFiles = allFiles.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
                if (exeFiles.Count > 0)
                {
                    executables.AddRange(exeFiles);
                    needsWine = true;
                }
            }

            executables.AddRange(allFiles.Where(f => f.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)));
        }

        return executables
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => GetPathDepth(path, f))
            .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsCompatibilityDirectory(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name is ".steam-compat-data" or ".wine-prefix" ||
               (Directory.Exists(Path.Combine(path, "drive_c")) && Directory.Exists(Path.Combine(path, "dosdevices")));
    }

    public static bool IsLauncherMetadataFile(string path, GameInstallationOptions? options = null)
    {
        options ??= GameInstallationOptions.Default;

        var fileName = Path.GetFileName(path);
        return fileName.Equals("version.txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(IncompleteInstallFileName, StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("LastPlayed.txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("installed-build.txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("selected_executable.txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("flatpak-install.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("flatpak-install.pending.json", StringComparison.OrdinalIgnoreCase) ||
               options.AdditionalMetadataFileNames.Any(name => fileName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static void MoveDirectoryContents(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            // File.Move falls back to copying the link target when moving across volumes.
            if (TryCopySymbolicLink(new FileInfo(file), destFile))
            {
                File.Delete(file);
                continue;
            }

            File.Move(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var destSub = Path.Combine(destDir, Path.GetFileName(dir));
            if (TryCopySymbolicLink(new DirectoryInfo(dir), destSub))
            {
                DeleteFileSystemEntry(dir);
                continue;
            }

            MoveDirectoryPreserveTree(dir, destSub);
        }
    }

    static void MoveDirectoryPreserveTree(string sourceDir, string destDir)
    {
        if (!Directory.Exists(destDir))
        {
            try
            {
                Directory.Move(sourceDir, destDir);
                return;
            }
            catch (IOException)
            {
                CopyDirectory(sourceDir, destDir);
                TryDeleteDirectory(sourceDir);
                return;
            }
        }

        MoveDirectoryContents(sourceDir, destDir);
        TryDeleteDirectory(sourceDir);
    }

    static async Task ExtractZipAsync(string downloadPath, string gamePath, GameInstallationOptions options)
    {
        var tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempExtractPath);

        try
        {
            ExtractZipToDirectory(downloadPath, tempExtractPath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ExtractNestedZips(tempExtractPath);

                var appBundle = Directory.GetDirectories(tempExtractPath, "*.app", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(appBundle))
                {
                    var appName = Path.GetFileName(appBundle);
                    var destAppPath = Path.Combine(gamePath, appName);

                    if (Directory.Exists(destAppPath))
                    {
                        Directory.Delete(destAppPath, true);
                    }

                    CopyDirectory(appBundle, destAppPath);
                    return;
                }

                // Some macOS releases wrap a drag-to-install disk image in a zip.
                var diskImages = Directory.GetFiles(tempExtractPath, "*.dmg", SearchOption.AllDirectories);
                if (diskImages.Length == 1)
                {
                    await InstallFromDiskImageAsync(diskImages[0], gamePath, options).ConfigureAwait(false);
                    return;
                }
            }

            if (TryGetNestedTarGzPayload(tempExtractPath, out var tarGzFile) &&
                !string.IsNullOrEmpty(tarGzFile))
            {
                await ExtractTarballAsync(tarGzFile, gamePath, tarGzFile).ConfigureAwait(false);
                return;
            }

            var sourcePath = GetEffectiveExtractionSource(tempExtractPath);
            MoveDirectoryContents(sourcePath, gamePath);
        }
        finally
        {
            TryDeleteDirectory(tempExtractPath);
        }
    }

    internal static void ExtractZipToDirectory(string zipPath, string destinationDirectoryPath)
    {
        // Validate before invoking system tools: unzip may silently rewrite traversal
        // entries, whereas the managed extractor rejects them. Keep every host equal.
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
                GetSafeExtractionPath(destinationDirectoryPath, entry.FullName);
        }
        Directory.CreateDirectory(destinationDirectoryPath);

        if (!OperatingSystem.IsWindows() &&
            TryExtractZipWithSystemTool(zipPath, destinationDirectoryPath))
        {
            return;
        }

        ExtractZipManaged(zipPath, destinationDirectoryPath);
    }

    internal static void ExtractZipManaged(string zipPath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var relative = (entry.FullName ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (relative.Length == 0)
                continue;

            var destPath = GetSafeExtractionPath(destinationDirectoryPath, relative);
            var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
            var fileType = unixMode & 0xF000;

            if (IsZipDirectoryEntry(entry, relative, fileType))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            var destParent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destParent))
                Directory.CreateDirectory(destParent);

            if (fileType == 0xA000)
            {
                ExtractZipSymlink(entry, destPath);
                continue;
            }

            entry.ExtractToFile(destPath, overwrite: true);
            TryApplyUnixFileMode(destPath, unixMode);
        }
    }

    static bool IsZipDirectoryEntry(ZipArchiveEntry entry, string relative, int unixFileType)
    {
        if (unixFileType == 0x4000)
            return true;

        if (string.IsNullOrEmpty(entry.Name))
            return true;

        return relative.EndsWith('/');
    }

    static void ExtractZipSymlink(ZipArchiveEntry entry, string destPath)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var target = reader.ReadToEnd();

        try
        {
            if (File.Exists(destPath))
                File.Delete(destPath);

            File.CreateSymbolicLink(destPath, target);
        }
        catch
        {
            File.WriteAllText(destPath, target);
        }
    }

    static void TryApplyUnixFileMode(string path, int unixMode)
    {
        if (OperatingSystem.IsWindows() || unixMode == 0)
            return;

        try
        {
            File.SetUnixFileMode(path, (UnixFileMode)(unixMode & 0x1FF));
        }
        catch
        {
        }
    }

    static bool TryExtractZipWithSystemTool(string zipPath, string destinationDirectoryPath)
    {
        if (TryRunArchiveExtractor("bsdtar", ["-xf", zipPath, "-C", destinationDirectoryPath]))
            return true;

        return TryRunArchiveExtractor("unzip", ["-o", "-q", zipPath, "-d", destinationDirectoryPath]);
    }

    static bool TryRunArchiveExtractor(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    static string GetEffectiveExtractionSource(string extractPath)
    {
        var rootDirs = Directory.GetDirectories(extractPath, "*", SearchOption.TopDirectoryOnly);
        var rootFiles = Directory.GetFiles(extractPath, "*", SearchOption.TopDirectoryOnly);

        if (rootDirs.Length == 1 && rootFiles.Length == 0)
        {
            return rootDirs[0];
        }

        return extractPath;
    }

    /// <summary>
    /// True when an unzipped tree is only a tar.gz wrapper: a single <c>.tar.gz</c>
    /// (optionally nested in folders) plus launcher metadata. Full app zips that
    /// happen to contain a nested archive are left intact.
    /// </summary>
    public static bool TryGetNestedTarGzPayload(string extractPath, out string? tarGzPath)
    {
        tarGzPath = null;

        if (string.IsNullOrWhiteSpace(extractPath) || !Directory.Exists(extractPath))
            return false;

        var payloadFiles = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories)
            .Where(f => !IsLauncherMetadataFile(f))
            .ToList();

        if (payloadFiles.Count != 1)
            return false;

        var onlyFile = payloadFiles[0];
        if (!onlyFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            return false;

        tarGzPath = onlyFile;
        return true;
    }

    static void ExtractNestedZips(string tempExtractPath)
    {
        var nestedZips = Directory.GetFiles(tempExtractPath, "*.zip", SearchOption.AllDirectories);
        foreach (var nestedZip in nestedZips)
        {
            var nestedZipDirectory = Path.GetDirectoryName(nestedZip) ?? tempExtractPath;
            var nestedExtractPath = Path.Combine(nestedZipDirectory, Path.GetFileNameWithoutExtension(nestedZip));
            Directory.CreateDirectory(nestedExtractPath);
            ExtractZipToDirectory(nestedZip, nestedExtractPath);

            try { File.Delete(nestedZip); } catch { }
        }
    }

    /// <summary>
    /// Mounts a macOS disk image read-only and hidden, copies its visible contents into
    /// <paramref name="gamePath"/>, then detaches it. Top-level symlinks (the usual
    /// "Applications" drag target) and hidden volume files are skipped. An image with a license agreement is
    /// opened only once the player accepts it (<see cref="GameInstallationOptions.AcceptLicenseAsync"/>).
    /// </summary>
    static async Task InstallFromDiskImageAsync(string diskImagePath, string gamePath, GameInstallationOptions options)
    {
        var fileName = Path.GetFileName(diskImagePath);
        var license = DiskImageLicense.Read(diskImagePath);
        if (license != null && (options.AcceptLicenseAsync is not { } accept ||
            !await accept(fileName, license).ConfigureAwait(false)))
            throw new LicenseDeclinedException(fileName);

        var mountPoint = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mountPoint);
        string? licenseFreeCopy = null;

        try
        {
            var image = diskImagePath;
            if (license != null)
            {
                // hdiutil asks for the agreement in a terminal and cancels without one. The player accepted it above,
                // and a converted copy of the image opens without asking.
                licenseFreeCopy = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cdr");
                var conversion = RunHdiutil("convert", diskImagePath, "-quiet", "-format", "UDTO", "-o", licenseFreeCopy);
                if (conversion.ExitCode != 0)
                    throw new InvalidOperationException($"Could not open disk image '{fileName}': {HdiutilProblem(conversion.Error)}");
                image = licenseFreeCopy;
            }

            // stdin is closed, so an agreement this missed fails here instead of hanging on hdiutil's prompt.
            var attach = RunHdiutil("attach", image, "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mountPoint);
            if (attach.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Could not open disk image '{fileName}': {HdiutilProblem(attach.Error)} " +
                    "If it asks you to accept a license agreement, open it in Finder and copy the app into the app's folder manually.");
            }

            try
            {
                Directory.CreateDirectory(gamePath);
                foreach (var entry in Directory.EnumerateFileSystemEntries(mountPoint))
                {
                    var name = Path.GetFileName(entry);
                    if (name.StartsWith('.') || new FileInfo(entry).LinkTarget != null)
                        continue;

                    var destPath = Path.Combine(gamePath, name);
                    if (Directory.Exists(entry))
                    {
                        // Replace, not merge, so an updated .app keeps no stale files.
                        DeleteFileSystemEntry(destPath);
                        CopyDirectory(entry, destPath);
                    }
                    else
                    {
                        File.Copy(entry, destPath, overwrite: true);
                    }
                }
            }
            finally
            {
                if (RunHdiutil("detach", mountPoint, "-quiet").ExitCode != 0)
                    RunHdiutil("detach", mountPoint, "-quiet", "-force");
            }
        }
        finally
        {
            TryDeleteDirectory(mountPoint);
            if (licenseFreeCopy != null)
                File.Delete(licenseFreeCopy);
        }
    }

    /// <summary>hdiutil's error without its notes that "hdiutil attach" is deprecated, which macOS 26 adds to every run.</summary>
    internal static string HdiutilProblem(string error) =>
        string.Join('\n', error.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("hdiutil: WARNING:", StringComparison.Ordinal)));

    static (int ExitCode, string Error) RunHdiutil(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "hdiutil",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start hdiutil.");
        process.StandardInput.Close();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        outputTask.Wait();
        return (process.ExitCode, error);
    }

    /// <param name="archiveName">The archive's name, which says how it is compressed; the downloaded file's may not.</param>
    static async Task ExtractTarballAsync(string sourceFilePath, string destinationDirectoryPath, string archiveName)
    {
        Directory.CreateDirectory(destinationDirectoryPath);

        var tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempExtractPath);

        try
        {
            await ExtractTarballToDirectoryAsync(sourceFilePath, tempExtractPath, archiveName).ConfigureAwait(false);
            var sourcePath = GetEffectiveExtractionSource(tempExtractPath);
            MoveDirectoryContents(sourcePath, destinationDirectoryPath);
        }
        finally
        {
            TryDeleteDirectory(tempExtractPath);
        }
    }

    static async Task ExtractSharpCompressArchiveAsync(
        string downloadPath,
        string gamePath,
        GameInstallationOptions options)
    {
        Directory.CreateDirectory(gamePath);

        var tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempExtractPath);

        try
        {
            await ExtractSharpCompressArchiveToDirectoryAsync(downloadPath, tempExtractPath, options)
                .ConfigureAwait(false);
            var sourcePath = GetEffectiveExtractionSource(tempExtractPath);
            MoveDirectoryContents(sourcePath, gamePath);
        }
        finally
        {
            TryDeleteDirectory(tempExtractPath);
        }
    }

    static async Task ExtractSharpCompressArchiveToDirectoryAsync(
        string archivePath,
        string destinationDirectoryPath,
        GameInstallationOptions options)
    {
        Directory.CreateDirectory(destinationDirectoryPath);

        using var stream = await ArchiveFileLock.OpenReadAsync(archivePath).ConfigureAwait(false);
        using var archive = ArchiveFactory.OpenArchive(stream);

        long totalBytes = 0;
        var fileCount = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
                continue;

            fileCount++;
            if (entry.Size > 0)
                totalBytes += entry.Size;
        }

        options.ExtractProgress?.Report(0);

        long extractedBytes = 0;
        var extractedFiles = 0;

        void ReportProgress(long size)
        {
            extractedFiles++;
            if (size > 0)
                extractedBytes += size;

            if (totalBytes > 0)
                options.ExtractProgress?.Report(Math.Clamp(extractedBytes / (double)totalBytes, 0, 0.99));
            else if (fileCount > 0)
                options.ExtractProgress?.Report(Math.Clamp(extractedFiles / (double)fileCount, 0, 0.99));
        }

        var extractOptions = new ExtractionOptions
        {
            Overwrite = true,
            ExtractFullPath = false,
        };

        if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory)
                    continue;

                if (!TryGetSafeArchiveDestination(destinationDirectoryPath, reader.Entry.Key, out var destination))
                    continue;

                reader.WriteEntryToFile(destination, extractOptions);
                ReportProgress(reader.Entry.Size);
            }
        }
        else
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory)
                    continue;

                if (!TryGetSafeArchiveDestination(destinationDirectoryPath, entry.Key, out var destination))
                    continue;

                entry.WriteToFile(destination, extractOptions);
                ReportProgress(entry.Size);
            }
        }
    }

    static bool TryGetSafeArchiveDestination(string destinationDirectoryPath, string? entryKey, out string destination)
    {
        destination = string.Empty;
        var relative = (entryKey ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0)
            return false;

        destination = GetSafeExtractionPath(destinationDirectoryPath, relative);
        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        return true;
    }

    static async Task ExtractTarballToDirectoryAsync(string sourceFilePath, string destinationDirectoryPath, string archiveName)
    {
        Directory.CreateDirectory(destinationDirectoryPath);
        var gzip = IsGzipTarball(archiveName) || !IsTarballAsset(archiveName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows' tar may lack xz or zstd support, so only gzip goes to it.
            if (!gzip)
                ExtractTarballManaged(sourceFilePath, destinationDirectoryPath);
            else if (!await TryExtractTarballWithSystemTarAsync(sourceFilePath, destinationDirectoryPath, gzip).ConfigureAwait(false))
                ExtractTarGzManaged(sourceFilePath, destinationDirectoryPath);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                if (await TryExtractTarballWithSystemTarAsync(sourceFilePath, destinationDirectoryPath, gzip).ConfigureAwait(false))
                    return;
            }
            catch (InvalidOperationException) when (!gzip)
            {
                // tar runs xz or zstd to decompress, and either may be missing.
                TryDeleteDirectory(destinationDirectoryPath);
            }

            if (gzip)
                throw new InvalidOperationException("Could not start tar extraction.");
            ExtractTarballManaged(sourceFilePath, destinationDirectoryPath);
            return;
        }

        throw new PlatformNotSupportedException("Unsupported operating system for tar extraction");
    }

    /// <summary>
    /// Extracts a tarball using the system <c>tar</c> tool, which keeps its programs executable. Other than gzip,
    /// tar recognizes the compression itself.
    /// Returns false only when tar could not be started (e.g. missing from PATH).
    /// </summary>
    static async Task<bool> TryExtractTarballWithSystemTarAsync(string sourceFilePath, string destinationDirectoryPath, bool gzip)
    {
        Process? process;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "tar",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { gzip ? "-xzf" : "-xf", sourceFilePath, "-C", destinationDirectoryPath })
                startInfo.ArgumentList.Add(argument);

            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }

        if (process is null)
            return false;

        using (process)
        {
            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var errorOutput = await errorTask.ConfigureAwait(false);
            await outputTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Tar extraction failed: {errorOutput}");
        }

        return true;
    }

    /// <summary>
    /// Extracts a tarball of any compression SharpCompress reads (gzip, xz, bzip2, zstd). A tarball doesn't keep the
    /// executable bit this way, so on Linux and macOS the programs in it are marked executable again.
    /// </summary>
    internal static void ExtractTarballManaged(string sourceFilePath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);
        using (var stream = File.OpenRead(sourceFilePath))
        using (var reader = ReaderFactory.OpenReader(stream))
        {
            var extractOptions = new ExtractionOptions { Overwrite = true, ExtractFullPath = false };
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory || reader.Entry.LinkTarget != null)
                    continue;
                if (!TryGetSafeArchiveDestination(destinationDirectoryPath, reader.Entry.Key, out var destination))
                    continue;
                reader.WriteEntryToFile(destination, extractOptions);
            }
        }

        if (OperatingSystem.IsWindows())
            return;
        var platform = OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        foreach (var file in Directory.EnumerateFiles(destinationDirectoryPath, "*", SearchOption.AllDirectories))
            if (IsLikelyNativeExecutable(file, platform))
                File.SetUnixFileMode(file, File.GetUnixFileMode(file) |
                    UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Installs a Debian package's files without a package manager: its data archive is unpacked into the app's
    /// folder. A package that keeps the app in one folder under /opt (GZDoom, Electron apps) installs that folder;
    /// any other keeps its usr/ tree, so a program in usr/bin still finds its files in usr/share.
    /// </summary>
    static async Task InstallFromDebianPackageAsync(string packagePath, string gamePath, GameInstallationOptions options)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            var dataArchive = ExtractDebianDataArchive(packagePath, tempPath);
            var tree = Path.Combine(tempPath, "tree");
            await ExtractTarballToDirectoryAsync(dataArchive, tree, Path.GetFileName(dataArchive)).ConfigureAwait(false);

            var opt = Path.Combine(tree, "opt");
            var appFolders = Directory.Exists(opt) ? Directory.GetDirectories(opt) : [];
            var source = appFolders.Length == 1 &&
                FindExecutableCandidates(appFolders[0], SearchOption.AllDirectories, options, out _).Count > 0
                ? appFolders[0]
                : tree;
            MoveDirectoryContents(source, gamePath);
        }
        finally
        {
            TryDeleteDirectory(tempPath);
        }
    }

    static readonly byte[] ArMagic = "!<arch>\n"u8.ToArray();

    /// <summary>
    /// Copies the data archive (data.tar.xz, data.tar.zst, …) out of a Debian package, which is an ar archive,
    /// into <paramref name="destinationDirectory"/> and returns its path.
    /// </summary>
    internal static string ExtractDebianDataArchive(string packagePath, string destinationDirectory)
    {
        var packageName = Path.GetFileName(packagePath);
        using var stream = File.OpenRead(packagePath);
        var magic = new byte[ArMagic.Length];
        if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) != magic.Length || !magic.AsSpan().SequenceEqual(ArMagic))
            throw new InvalidDataException($"'{packageName}' is not a Debian package.");

        // Each member has a 60-byte header: name (16), dates and owner (32), size (10), "`\n" (2); data is padded to even.
        var header = new byte[60];
        while (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length)
        {
            var name = Encoding.ASCII.GetString(header, 0, 16).Trim().TrimEnd('/');
            if (!long.TryParse(Encoding.ASCII.GetString(header, 48, 10).Trim(), out var size) || size < 0 ||
                header[58] != '`' || header[59] != '\n')
                throw new InvalidDataException($"'{packageName}' is damaged.");

            if (name.StartsWith("data.tar", StringComparison.Ordinal) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '.'))
            {
                var path = Path.Combine(destinationDirectory, name);
                using var output = File.Create(path);
                var buffer = new byte[81920];
                for (var remaining = size; remaining > 0;)
                {
                    var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0)
                        throw new InvalidDataException($"'{packageName}' is incomplete.");
                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
                return path;
            }

            stream.Seek(size + size % 2, SeekOrigin.Current);
        }

        throw new InvalidDataException($"'{packageName}' has no files to install.");
    }

    /// <summary>
    /// Managed GZip + ustar extraction used as a Windows fallback when system tar is unavailable.
    /// </summary>
    internal static void ExtractTarGzManaged(string sourceFilePath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);
        using var inputStream = File.OpenRead(sourceFilePath);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        ExtractTarFromStream(gzipStream, destinationDirectoryPath);
    }

    static string GetSafeExtractionPath(string destinationDirectoryPath, string archivePath)
    {
        var sanitizedArchivePath = archivePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullDestinationRoot = Path.GetFullPath(destinationDirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDestinationPath = Path.GetFullPath(Path.Combine(fullDestinationRoot, sanitizedArchivePath));

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullDestinationPath.StartsWith(fullDestinationRoot + Path.DirectorySeparatorChar, comparison) &&
            !fullDestinationPath.Equals(fullDestinationRoot, comparison))
        {
            throw new InvalidDataException($"Archive entry escapes the destination directory: {archivePath}");
        }

        return fullDestinationPath;
    }

    internal static void ExtractTarFromStream(Stream tarStream, string destinationDirectoryPath)
    {
        using var reader = new BinaryReader(tarStream);
        var buffer = new byte[8192];

        while (true)
        {
            var headerBytes = reader.ReadBytes(512);
            if (headerBytes.Length < 512) break;

            var fileName = Encoding.ASCII.GetString(headerBytes, 0, 100).Trim('\0', ' ');
            if (string.IsNullOrWhiteSpace(fileName)) break;

            var fileSizeStr = Encoding.ASCII.GetString(headerBytes, 124, 12).Trim('\0', ' ');
            var fileSize = string.IsNullOrEmpty(fileSizeStr) ? 0L : Convert.ToInt64(fileSizeStr, 8);
            var fileType = headerBytes[156];
            var destPath = GetSafeExtractionPath(destinationDirectoryPath, fileName);

            if (fileType == '5')
            {
                Directory.CreateDirectory(destPath);
            }
            else
            {
                var destinationDirectory = Path.GetDirectoryName(destPath);
                if (string.IsNullOrEmpty(destinationDirectory))
                {
                    throw new InvalidDataException($"Invalid archive entry path: {fileName}");
                }

                Directory.CreateDirectory(destinationDirectory);

                using var fileStream = File.Create(destPath);
                var remaining = fileSize;
                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(remaining, buffer.Length);
                    var read = reader.Read(buffer, 0, toRead);
                    if (read == 0)
                        throw new EndOfStreamException($"Unexpected end of tar stream while extracting '{fileName}'.");
                    fileStream.Write(buffer, 0, read);
                    remaining -= read;
                }
            }

            // Content is read exactly; skip only the trailing pad to the next 512-byte boundary.
            var paddingBytes = (int)((512 - (fileSize % 512)) % 512);
            if (paddingBytes > 0)
            {
                reader.ReadBytes(paddingBytes);
            }
        }
    }

    internal static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (TryCopySymbolicLink(new FileInfo(file), destFile))
                continue;

            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            if (TryCopySymbolicLink(new DirectoryInfo(dir), destSubDir))
                continue;

            CopyDirectory(dir, destSubDir);
        }
    }

    /// <summary>
    /// Recreates a symlink at <paramref name="destPath"/> with the same target instead of copying
    /// what it points to. macOS bundles depend on relative links such as
    /// <c>Engine.framework/Versions/Current</c>; following them duplicates files and breaks the
    /// bundle's code signature. Returns false for regular entries, or when the link cannot be
    /// created (e.g. Windows without symlink rights), so the caller copies the contents instead.
    /// </summary>
    static bool TryCopySymbolicLink(FileSystemInfo source, string destPath)
    {
        var target = source.LinkTarget;
        if (target == null)
            return false;

        try
        {
            DeleteFileSystemEntry(destPath);
            if (source is DirectoryInfo)
                Directory.CreateSymbolicLink(destPath, target);
            else
                File.CreateSymbolicLink(destPath, target);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static void DeleteFileSystemEntry(string path)
    {
        // Removes the link itself for symlinks (including links to directories); a real
        // directory makes File.Delete throw, so fall through to a recursive delete.
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    static void MakeExecutableIfNeeded(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var chmodProcess = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(chmodProcess);
            process?.WaitForExit();
        }
        catch
        {
        }
    }

    static bool HasTopLevelExecutable(string path, GameInstallationOptions options)
    {
        if (!Directory.Exists(path))
            return false;

        return FindExecutableCandidates(path, SearchOption.TopDirectoryOnly, options, out _).Count > 0;
    }

    private static OSPlatform CurrentDesktopPlatform => OperatingSystem.IsWindows() ? OSPlatform.Windows :
        OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;

    internal static bool IsValidSavedExecutable(string? path, OSPlatform? platform = null)
    {
        var target = platform ?? CurrentDesktopPlatform;
        // macOS app bundles are directories.
        if (target == OSPlatform.OSX && path != null &&
            path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
            return true;
        if (!File.Exists(path)) return false;
        return target == OSPlatform.Windows || Path.HasExtension(path) || IsLikelyExtensionlessExecutable(path, target);
    }

    // ZIP permissions and file sizes are not evidence that an extensionless file is a program.
    // In particular LICENSE/COPYING used to hide every Windows .exe in Linux installs.
    static bool IsLikelyExtensionlessExecutable(string path, OSPlatform platform)
        => !Path.HasExtension(path) && IsLikelyNativeExecutable(path, platform);

    static bool IsLikelyNativeExecutable(string path, OSPlatform platform)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[64];
            var length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (length >= 3 && header[0] == '#' && header[1] == '!' &&
                System.Text.Encoding.UTF8.GetString(header[2..length]).TrimStart(' ', '\t').StartsWith('/'))
                return true;

            if (platform == OSPlatform.OSX)
            {
                if (length < 4) return false;
                var magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
                return magic is 0xFEEDFACE or 0xCEFAEDFE or 0xFEEDFACF or 0xCFFAEDFE or 0xCAFEBABE or 0xBEBAFECA or 0xCAFEBABF or 0xBFBAFECA;
            }

            if (length < 52 || !header[..4].SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }) ||
                header[4] is not (1 or 2) || header[5] is not (1 or 2) || header[6] != 1 ||
                header[4] == 2 && length < 64) return false;
            var type = header[5] == 1
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[16..])
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(header[16..]);
            // ET_EXEC or PIE (ET_DYN), with an entry point. Shared libraries without one are not launch targets.
            return type is 2 or 3 && header.Slice(24, header[4] == 1 ? 4 : 8).ContainsAnyExcept((byte)0);
        }
        catch
        {
            return false;
        }
    }

    static int GetPathDepth(string rootPath, string targetPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, targetPath);
        return relativePath.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
    }

    static bool IsInRootDirectory(string path, string rootPath)
    {
        var parentDir = Path.GetDirectoryName(path);
        return parentDir != null &&
               Path.GetFullPath(parentDir).TrimEnd(Path.DirectorySeparatorChar)
                   .Equals(Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar),
                           StringComparison.OrdinalIgnoreCase);
    }

    static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try { Directory.Delete(path, true); } catch { }
    }

    static void Log(GameInstallationOptions options, string message)
    {
        if (options.Log is not null)
        {
            options.Log(message);
        }
        else
        {
            Debug.WriteLine(message);
        }
    }
}
