using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace QuiverLauncher.Core.Services;

public static class GameInstallationService
{
    /// <summary>
    /// True for single-file release binaries: .exe, .appimage, or extensionless (e.g. CrashBandicoot_Linux).
    /// </summary>
    public static bool IsSingleFileExecutableAsset(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
            return false;

        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            assetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !Path.HasExtension(assetName);
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
               assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase) ||
               assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAndroidPackageAsset(string? assetName)
        => !string.IsNullOrWhiteSpace(assetName)
           && assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);

    public static bool IsAppImageAsset(string? assetName)
        => !string.IsNullOrWhiteSpace(assetName)
           && assetName.EndsWith(".appimage", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// After a successful AppImage install, delete other top-level <c>.AppImage</c> files
    /// left behind by versioned release names. Does not recurse, and does not fail the update
    /// if a stale file is locked. Retargets <c>selected_executable.txt</c> when it points at
    /// a missing path.
    /// </summary>
    public static void RemoveStaleAppImages(string gamePath, string keepPath, GameInstallationOptions? options = null)
    {
        options ??= GameInstallationOptions.Default;

        if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(keepPath))
            return;

        if (!Directory.Exists(gamePath) || !File.Exists(keepPath))
            return;

        var keepFullPath = Path.GetFullPath(keepPath);

        foreach (var file in Directory.GetFiles(gamePath, "*", SearchOption.TopDirectoryOnly))
        {
            if (!IsAppImageAsset(file))
                continue;

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

        RetargetSelectedExecutableIfStale(gamePath, keepFullPath, options);
    }

    static void RetargetSelectedExecutableIfStale(string gamePath, string keepPath, GameInstallationOptions options)
    {
        var selectedPath = Path.Combine(gamePath, "selected_executable.txt");
        if (!File.Exists(selectedPath))
            return;

        try
        {
            var saved = File.ReadAllText(selectedPath).Trim();
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                return;

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
        if (!HasRecognizedInstallExtension(effectiveName) && !IsSingleFileExecutableAsset(effectiveName))
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
        else if (effectiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractZipAsync(downloadPath, gamePath).ConfigureAwait(false);
        }
        else if (effectiveName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(downloadPath, gamePath).ConfigureAwait(false);
        }
        else if (effectiveName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                 effectiveName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractSharpCompressArchiveAsync(downloadPath, gamePath).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported release asset type: '{assetName}'. " +
                "Expected .exe, .appimage, .zip, .tar.gz, .7z, .rar, or an extensionless binary.");
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

    public static List<string> FindExecutableCandidates(
        string path,
        SearchOption searchOption,
        GameInstallationOptions? options,
        out bool needsWine)
    {
        _ = options;
        needsWine = false;

        if (!Directory.Exists(path))
            return [];

        var executables = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            executables.AddRange(Directory.GetFiles(path, "*.exe", searchOption));
            executables.AddRange(Directory.GetFiles(path, "*.bat", searchOption));
            executables.AddRange(Directory.GetFiles(path, "*.cmd", searchOption));
            executables.AddRange(Directory.GetFiles(path, "launch.bat", searchOption));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            executables.AddRange(Directory.GetDirectories(path, "*.app", searchOption));
            executables.AddRange(Directory.GetFiles(path, "*", searchOption)
                .Where(IsLikelyExtensionlessExecutable));
        }
        else
        {
            var allFiles = Directory.GetFiles(path, "*", searchOption);

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

                if (fileName.EndsWith(".appimage") || fileName.EndsWith(".x86_64") ||
                    fileName.EndsWith(".arm64") || fileName.EndsWith(".aarch64") ||
                    fileName.EndsWith(".txt") || fileName.EndsWith(".dll") ||
                    fileName.EndsWith(".so") || fileName.EndsWith(".json") ||
                    fileName.EndsWith(".sh") || fileName.EndsWith(".exe"))
                {
                    return false;
                }

                return IsLikelyExtensionlessExecutable(f);
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

    public static bool IsLauncherMetadataFile(string path, GameInstallationOptions? options = null)
    {
        options ??= GameInstallationOptions.Default;

        var fileName = Path.GetFileName(path);
        return fileName.Equals("version.txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("LastPlayed.txt", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("selected_executable.txt", StringComparison.OrdinalIgnoreCase) ||
               options.AdditionalMetadataFileNames.Any(name => fileName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static void MoveDirectoryContents(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Move(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            var destSub = Path.Combine(destDir, Path.GetFileName(dir));
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

    static async Task ExtractZipAsync(string downloadPath, string gamePath)
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
            }

            if (TryGetNestedTarGzPayload(tempExtractPath, out var tarGzFile) &&
                !string.IsNullOrEmpty(tarGzFile))
            {
                await ExtractTarGzAsync(tarGzFile, gamePath).ConfigureAwait(false);
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

    static async Task ExtractTarGzAsync(string sourceFilePath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);

        var tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempExtractPath);

        try
        {
            await ExtractTarGzToDirectoryAsync(sourceFilePath, tempExtractPath).ConfigureAwait(false);
            var sourcePath = GetEffectiveExtractionSource(tempExtractPath);
            MoveDirectoryContents(sourcePath, destinationDirectoryPath);
        }
        finally
        {
            TryDeleteDirectory(tempExtractPath);
        }
    }

    static Task ExtractSharpCompressArchiveAsync(string downloadPath, string gamePath)
    {
        Directory.CreateDirectory(gamePath);

        var tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempExtractPath);

        try
        {
            ExtractSharpCompressArchiveToDirectory(downloadPath, tempExtractPath);
            var sourcePath = GetEffectiveExtractionSource(tempExtractPath);
            MoveDirectoryContents(sourcePath, gamePath);
        }
        finally
        {
            TryDeleteDirectory(tempExtractPath);
        }

        return Task.CompletedTask;
    }

    static void ExtractSharpCompressArchiveToDirectory(string archivePath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);
        var destinationRoot = Path.GetFullPath(destinationDirectoryPath);

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
                continue;

            var key = entry.Key ?? string.Empty;
            var relative = key.Replace('\\', '/').TrimStart('/');
            if (relative.Length == 0)
                continue;

            var destination = Path.GetFullPath(
                Path.Combine(destinationDirectoryPath, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            entry.WriteToFile(destination, new ExtractionOptions
            {
                Overwrite = true,
                ExtractFullPath = false,
            });
        }
    }

    static async Task ExtractTarGzToDirectoryAsync(string sourceFilePath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!await TryExtractTarGzWithSystemTarAsync(sourceFilePath, destinationDirectoryPath).ConfigureAwait(false))
                ExtractTarGzManaged(sourceFilePath, destinationDirectoryPath);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (!await TryExtractTarGzWithSystemTarAsync(sourceFilePath, destinationDirectoryPath).ConfigureAwait(false))
                throw new InvalidOperationException("Could not start tar extraction.");
            return;
        }

        throw new PlatformNotSupportedException("Unsupported operating system for tar.gz extraction");
    }

    /// <summary>
    /// Extracts a .tar.gz using the system <c>tar</c> tool.
    /// Returns false only when tar could not be started (e.g. missing from PATH).
    /// </summary>
    static async Task<bool> TryExtractTarGzWithSystemTarAsync(string sourceFilePath, string destinationDirectoryPath)
    {
        Process? process;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{sourceFilePath}\" -C \"{destinationDirectoryPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

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
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var errorOutput = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"Tar extraction failed: {errorOutput}");
            }
        }

        return true;
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

        if (!fullDestinationPath.StartsWith(fullDestinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !fullDestinationPath.Equals(fullDestinationRoot, StringComparison.OrdinalIgnoreCase))
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

    static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
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

    static bool IsLikelyExtensionlessExecutable(string path)
    {
        try
        {
            return !Path.HasExtension(path) && new FileInfo(path).Length > 1024;
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
