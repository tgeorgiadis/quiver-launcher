using QuiverLauncher.Models;
using System;
using System.Diagnostics;
#if !EXCLUDE_WINDOWS_DRAWING
using System.Drawing;
using System.Drawing.Imaging;
#endif
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack.Locators;

namespace QuiverLauncher.Services
{
    public static class ShortcutHelper
    {
        private static readonly string LauncherSteamTag = QuiverLauncherProfile.Instance.SteamTag;

        /// <summary>
        /// Stable launcher path for shortcuts and Steam. AppImages must use the
        /// <c>.AppImage</c> file, not the temporary <c>/tmp/.mount_*</c> squashfs.
        /// </summary>
        public static string? ResolveLauncherPath() =>
            ResolveLauncherPath(
                TryGetVelopackAppImagePath(),
                Environment.GetEnvironmentVariable("APPIMAGE"),
                Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName);

        public static string? ResolveLauncherPath(
            string? velopackAppImagePath,
            string? appImageEnv,
            string? processPath)
        {
            foreach (var candidate in new[] { velopackAppImagePath, appImageEnv, processPath })
            {
                if (IsUsableLauncherPath(candidate))
                    return candidate;
            }

            return null;
        }

        public static bool IsAppImageMountPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalized = path.Replace('\\', '/');
            return normalized.Contains("/tmp/.mount_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUsableLauncherPath(string? path) =>
            !string.IsNullOrWhiteSpace(path) && !IsAppImageMountPath(path);

        private static string? TryGetVelopackAppImagePath()
        {
            try
            {
                if (VelopackLocator.Current is LinuxVelopackLocator linux &&
                    !string.IsNullOrWhiteSpace(linux.AppImagePath))
                    return linux.AppImagePath;
            }
            catch
            {
                // Not a Velopack AppImage, or locator unavailable.
            }

            return null;
        }

        private static string RequireLauncherPath(string? launcherPath)
        {
            var resolved = ResolveLauncherPath(
                TryGetVelopackAppImagePath(),
                Environment.GetEnvironmentVariable("APPIMAGE"),
                launcherPath);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new InvalidOperationException(
                    "Could not determine a stable launcher path. Shortcuts cannot use an AppImage mount directory.");
            }

            return resolved;
        }

        public static async Task CreateGameShortcutAsync(GameInfo game, GameShortcutTarget target, string? cacheDirectory, string? desktopDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(game?.Name))
                throw new ArgumentException("Game name is required.", nameof(game));

            string desktopPath = desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string? iconPath = await PrepareIconAsync(game, cacheDirectory).ConfigureAwait(false);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                CreateWindowsShortcut(desktopPath, target, game, iconPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                CreateLinuxDesktopFile(desktopPath, target, game, iconPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                throw new PlatformNotSupportedException("macOS shortcuts not yet implemented");
            }
        }

        public static string AddGameToSteam(GameInfo game, GameShortcutTarget target, string? cacheDirectory)
        {
            if (string.IsNullOrWhiteSpace(game?.Name))
                throw new ArgumentException("Game name is required.", nameof(game));

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException("Adding non-Steam shortcuts is currently supported on Windows and Linux only.");
            }

            if (IsSteamRunning())
                throw new InvalidOperationException("Steam is still running.");

            return AddGameToSteamInternalAsync(game, target, cacheDirectory).GetAwaiter().GetResult();
        }

        public static string QueueGameAddToSteam(GameInfo game, string launcherPath)
        {
            if (string.IsNullOrWhiteSpace(game?.Name))
                throw new ArgumentException("Game name is required.", nameof(game));

            launcherPath = RequireLauncherPath(launcherPath);

            if (IsRunningUnderSteam())
                throw new InvalidOperationException("Steam is running this launcher, so the shortcut worker would keep Steam from seeing the launcher as closed. Close Steam and run the launcher outside Steam to add shortcuts.");

            var startInfo = CreateSteamShortcutWorkerStartInfo(game, launcherPath);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the Steam shortcut worker.");

            return $"Queued {game.Name} for Steam. Restart Steam once and the shortcut will be written after Steam fully closes.";
        }

        internal static ProcessStartInfo CreateSteamShortcutWorkerStartInfo(GameInfo game, string launcherPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--add-steam-shortcut");
            startInfo.ArgumentList.Add(game.Name);
            startInfo.ArgumentList.Add("--app-identity");
            startInfo.ArgumentList.Add(game.IdentityKey);
            startInfo.ArgumentList.Add("--wait-for-steam-exit");
            return startInfo;
        }

        public static async Task<string> AddGameToSteamFromCliAsync(GameInfo game, string gamesFolder, AppSettings settings, string? cacheDirectory, bool waitForSteamExit)
        {
            if (waitForSteamExit)
            {
                await WaitForSteamExitAsync().ConfigureAwait(false);
            }
            else if (IsSteamRunning())
            {
                throw new InvalidOperationException("Steam must be closed before modifying shortcuts.vdf.");
            }

            var target = await GameShortcutLaunch.PrepareAsync(game, gamesFolder, settings).ConfigureAwait(false);
            return await AddGameToSteamInternalAsync(game, target!, cacheDirectory).ConfigureAwait(false);
        }

        private static async Task<string> AddGameToSteamInternalAsync(GameInfo game, GameShortcutTarget target, string? cacheDirectory)
        {
            string? configDirectory = FindSteamConfigDirectory();
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                throw new DirectoryNotFoundException("Could not find a Steam userdata config directory. Open Steam at least once on this device first.");
            }

            Directory.CreateDirectory(configDirectory);

            string shortcutsPath = Path.Combine(configDirectory, "shortcuts.vdf");
            string? iconPath = await PrepareIconAsync(game, cacheDirectory).ConfigureAwait(false);
            WriteGameToSteamFile(shortcutsPath, game, target, iconPath, out var updated);
            return updated
                ? "Updated the Steam shortcut to launch the game directly. Restart Steam or return to Game Mode to refresh your library."
                : "Added the game to Steam. Restart Steam or return to Game Mode to refresh your library.";
        }

        internal static void WriteGameToSteamFile(string shortcutsPath, GameInfo game, GameShortcutTarget target, string? iconPath, out bool updated)
        {
            var root = File.Exists(shortcutsPath)
                ? ReadSteamShortcuts(shortcutsPath)
                : CreateEmptySteamShortcutsRoot();

            var shortcutsObject = EnsureObject(root, "shortcuts");
            string launchOptions = string.Join(" ", target.Arguments.Select(QuoteCommandArgument));
            string quotedTargetPath = QuoteCommandArgument(target.FileName);
            int appId = CalculateSteamShortcutAppId(quotedTargetPath, game.Name!);

            var existingEntry = FindMatchingSteamEntry(shortcutsObject, game.Name!, quotedTargetPath);
            updated = existingEntry != null;

            var shortcutEntry = existingEntry ?? new SteamObject();
            // Retain Steam's identity when replacing a launcher-based shortcut so
            // artwork and per-game controller/compatibility settings remain attached.
            if (existingEntry?.Properties.FirstOrDefault(p => p.Key == "appid").Value is not SteamInt)
                SetShortcutInt(shortcutEntry, "appid", appId);
            SetShortcutString(shortcutEntry, "appname", game.Name!);
            SetShortcutString(shortcutEntry, "exe", quotedTargetPath);
            SetShortcutString(shortcutEntry, "StartDir", QuoteCommandArgument(target.WorkingDirectory));
            if (iconPath != null || !updated) SetShortcutString(shortcutEntry, "icon", iconPath ?? string.Empty);
            SetShortcutString(shortcutEntry, "LaunchOptions", launchOptions);
            if (!updated)
            {
                SetShortcutString(shortcutEntry, "ShortcutPath", string.Empty);
                SetShortcutInt(shortcutEntry, "IsHidden", 0);
                SetShortcutInt(shortcutEntry, "AllowDesktopConfig", 1);
                SetShortcutInt(shortcutEntry, "AllowOverlay", 1);
                SetShortcutInt(shortcutEntry, "OpenVR", 0);
                SetShortcutInt(shortcutEntry, "Devkit", 0);
                SetShortcutString(shortcutEntry, "DevkitGameID", string.Empty);
                SetShortcutInt(shortcutEntry, "DevkitOverrideAppID", 0);
                SetShortcutInt(shortcutEntry, "LastPlayTime", 0);
                SetShortcutString(shortcutEntry, "FlatpakAppID", string.Empty);
                SetShortcutString(shortcutEntry, "sortas", string.Empty);
            }

            var tags = EnsureObject(shortcutEntry, "tags");
            if (!tags.Properties.Any(p => p.Value is SteamString value && value.Value == LauncherSteamTag))
                tags.Properties.Add(new KeyValuePair<string, SteamValue>(GetNextShortcutIndex(tags).ToString(), new SteamString(LauncherSteamTag)));

            if (!updated)
            {
                shortcutsObject.Properties.Add(new KeyValuePair<string, SteamValue>(
                    GetNextShortcutIndex(shortcutsObject).ToString(),
                    shortcutEntry));
            }

            NormalizeShortcutIndices(shortcutsObject);
            WriteSteamShortcuts(shortcutsPath, root);

        }

        private static async Task<string?> PrepareIconAsync(GameInfo game, string? cacheDirectory)
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory))
                return null;

            string iconsDir = Path.Combine(cacheDirectory, "ShortcutIcons");
            Directory.CreateDirectory(iconsDir);

            string? sourcePath = null;

            if (!string.IsNullOrEmpty(game.IconUrl) && File.Exists(game.IconUrl))
            {
                sourcePath = game.IconUrl;
            }
            else if (!string.IsNullOrEmpty(game.DefaultIconUrl) &&
                     (game.DefaultIconUrl.StartsWith("http://") || game.DefaultIconUrl.StartsWith("https://")))
            {
                try
                {
                    var tempIconPath = Path.Combine(iconsDir, $"{game.FolderName}_temp.png");
                    using var client = new System.Net.Http.HttpClient();
                    var iconData = await client.GetByteArrayAsync(game.DefaultIconUrl).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(tempIconPath, iconData).ConfigureAwait(false);
                    sourcePath = tempIconPath;
                }
                catch
                {
                    return null;
                }
            }

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return null;

            if (OperatingSystem.IsWindows())
            {
                string icoPath = Path.Combine(iconsDir, $"{game.FolderName}.ico");

                if (!File.Exists(icoPath))
                {
#if !EXCLUDE_WINDOWS_DRAWING
                    ConvertToIco(sourcePath, icoPath);
#else
                    return sourcePath;
#endif
                }

                return icoPath;
            }

            return sourcePath;
        }

#if !EXCLUDE_WINDOWS_DRAWING
        [SupportedOSPlatform("windows")]
        private static void ConvertToIco(string sourcePath, string icoPath)
        {
            try
            {
                using var sourceImage = Image.FromFile(sourcePath);
                using var resizedImage = new Bitmap(sourceImage, new Size(256, 256));
                using var stream = new FileStream(icoPath, FileMode.Create);

                // Write ICO header
                stream.WriteByte(0); stream.WriteByte(0); // Reserved
                stream.WriteByte(1); stream.WriteByte(0); // Type (1 = ICO)
                stream.WriteByte(1); stream.WriteByte(0); // Image count

                // Write ICONDIRENTRY
                stream.WriteByte(0); // Width (0 = 256)
                stream.WriteByte(0); // Height (0 = 256)
                stream.WriteByte(0); // Color palette
                stream.WriteByte(0); // Reserved
                stream.WriteByte(1); stream.WriteByte(0); // Color planes
                stream.WriteByte(32); stream.WriteByte(0); // Bits per pixel

                // Write placeholder for image size and offset
                long sizePos = stream.Position;
                stream.Write(new byte[8], 0, 8);

                // Write PNG data
                long imageStart = stream.Position;
                using (var ms = new MemoryStream())
                {
                    resizedImage.Save(ms, ImageFormat.Png);
                    var pngData = ms.ToArray();
                    stream.Write(pngData, 0, pngData.Length);
                }
                long imageEnd = stream.Position;

                // Go back and write size and offset
                stream.Seek(sizePos, SeekOrigin.Begin);
                int imageSize = (int)(imageEnd - imageStart);
                stream.Write(BitConverter.GetBytes(imageSize), 0, 4);
                stream.Write(BitConverter.GetBytes((int)imageStart), 0, 4);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to convert icon: {ex.Message}");
            }
        }
#endif

        [SupportedOSPlatform("windows")]
        private static void CreateWindowsShortcut(string desktopPath, GameShortcutTarget target, GameInfo game, string? iconPath)
        {
            var shortcutPath = Path.Combine(desktopPath, $"{SanitizeFileName(game.Name!)}.lnk");
            // COM properties accept literal paths; no shell interpolation of app names,
            // apostrophes, arguments, or installation folders is needed.
            object? shell = null;
            object? shortcut = null;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell")
                    ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
                shell = Activator.CreateInstance(shellType)!;
                dynamic link = ((dynamic)shell).CreateShortcut(shortcutPath);
                shortcut = link;
                link.TargetPath = target.FileName;
                link.Arguments = string.Join(" ", target.Arguments.Select(QuoteCommandArgument));
                link.WorkingDirectory = target.WorkingDirectory;
                link.Description = $"Launch {game.Name}";
                link.IconLocation = iconPath != null ? iconPath + ",0" : target.FileName + ",0";
                link.Save();
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void CreateLinuxDesktopFile(string desktopPath, GameShortcutTarget target, GameInfo game, string? iconPath)
        {
            var desktopFilePath = Path.Combine(desktopPath, $"{SanitizeFileName(game.Name!)}.desktop");
            File.WriteAllText(desktopFilePath, BuildLinuxDesktopFile(game.Name!, target, iconPath));
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(desktopFilePath, File.GetUnixFileMode(desktopFilePath) | UnixFileMode.UserExecute);
        }

        internal static string BuildLinuxDesktopFile(string name, GameShortcutTarget target, string? iconPath)
        {
            var command = string.Join(" ", new[] { target.FileName }.Concat(target.Arguments)
                .Select(value => EscapeDesktopValue(QuoteUnixArgument(value).Replace("%", "%%"))));
            return $"[Desktop Entry]\nType=Application\nName={EscapeDesktopValue(name)}\nExec={command}\nPath={EscapeDesktopValue(target.WorkingDirectory)}\nIcon={EscapeDesktopValue(iconPath ?? "")}\nTerminal=false\nCategories=Game;\nComment={EscapeDesktopValue($"Launch {name}")}\n";
        }

        private static string EscapeDesktopValue(string value) => value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        private static string QuoteUnixArgument(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`") + "\"";

        internal static string QuoteCommandArgument(string value)
        {
            if (!OperatingSystem.IsWindows()) return QuoteUnixArgument(value);
            // Windows CommandLineToArgvW / CRT quoting, including trailing backslashes.
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                result.Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        private static string SanitizeFileName(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars());
            foreach (char c in invalid)
            {
                name = name.Replace(c.ToString(), "");
            }
            return name;
        }

        private static int CalculateSteamShortcutAppId(string quotedLauncherPath, string gameName)
        {
            string identity = $"{quotedLauncherPath}{gameName}";
            uint crc = ComputeCrc32(Encoding.UTF8.GetBytes(identity));

            crc |= 0x80000000;

            if (crc == 0x80000000)
                crc = 0x80000001;

            return unchecked((int)crc);
        }

        private static uint ComputeCrc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;

            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    bool lsbSet = (crc & 1) != 0;
                    crc >>= 1;
                    if (lsbSet)
                    {
                        crc ^= 0xEDB88320;
                    }
                }
            }

            return ~crc;
        }

        public static bool IsSteamRunning()
        {
            try
            {
                return Process.GetProcessesByName("steam").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRunningUnderSteam()
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SteamGameId")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SteamAppId")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SteamOverlayGameId")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SteamDeck"));
        }

        private static async Task WaitForSteamExitAsync()
        {
            while (IsSteamRunning())
            {
                await Task.Delay(1000).ConfigureAwait(false);
            }

            // Give Steam a moment to finish flushing its own config writes.
            await Task.Delay(1000).ConfigureAwait(false);
        }

        private static string? FindSteamConfigDirectory()
        {
            var candidates = GetSteamUserdataRoots()
                .Where(Directory.Exists)
                .SelectMany(root =>
                {
                    try
                    {
                        return Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
                    }
                    catch
                    {
                        return Array.Empty<string>();
                    }
                })
                .Where(path => Path.GetFileName(path).All(char.IsDigit))
                .Select(path => Path.Combine(path, "config"))
                .ToList();

            if (candidates.Count == 0)
                return null;

            return candidates
                .OrderByDescending(GetSteamConfigSortTime)
                .FirstOrDefault();
        }

        private static IEnumerable<string> GetSteamUserdataRoots()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                return new[]
                {
                    Path.Combine(programFilesX86, "Steam", "userdata"),
                    Path.Combine(programFiles, "Steam", "userdata"),
                    Path.Combine(localAppData, "Steam", "userdata")
                }.Distinct(StringComparer.OrdinalIgnoreCase);
            }

            return new[]
            {
                Path.Combine(userProfile, ".steam", "steam", "userdata"),
                Path.Combine(userProfile, ".local", "share", "Steam", "userdata"),
                Path.Combine(userProfile, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "userdata")
            }.Distinct(StringComparer.Ordinal);
        }

        private static DateTime GetSteamConfigSortTime(string configDirectory)
        {
            try
            {
                string shortcutsPath = Path.Combine(configDirectory, "shortcuts.vdf");
                if (File.Exists(shortcutsPath))
                    return File.GetLastWriteTimeUtc(shortcutsPath);

                return Directory.GetLastWriteTimeUtc(configDirectory);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static SteamObject CreateEmptySteamShortcutsRoot()
        {
            var root = new SteamObject();
            root.Properties.Add(new KeyValuePair<string, SteamValue>("shortcuts", new SteamObject()));
            return root;
        }

        private static SteamObject ReadSteamShortcuts(string shortcutsPath)
        {
            using var stream = new FileStream(shortcutsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            return ReadSteamObject(reader);
        }

        private static SteamObject ReadSteamObject(BinaryReader reader)
        {
            var result = new SteamObject();

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                byte entryType = reader.ReadByte();
                if (entryType == 0x08)
                    break;

                string key = ReadNullTerminatedString(reader);
                SteamValue value = entryType switch
                {
                    0x00 => ReadSteamObject(reader),
                    0x01 => new SteamString(ReadNullTerminatedString(reader)),
                    0x02 => new SteamInt(reader.ReadInt32()),
                    _ => throw new InvalidDataException($"Unsupported Steam shortcut entry type: 0x{entryType:X2}")
                };

                result.Properties.Add(new KeyValuePair<string, SteamValue>(key, value));
            }

            return result;
        }

        private static string ReadNullTerminatedString(BinaryReader reader)
        {
            using var stream = new MemoryStream();

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                byte next = reader.ReadByte();
                if (next == 0x00)
                    break;

                stream.WriteByte(next);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteSteamShortcuts(string shortcutsPath, SteamObject rootValue)
        {
            var temporary = shortcutsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    WriteSteamObject(writer, rootValue);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, shortcutsPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void WriteSteamObject(BinaryWriter writer, SteamObject value)
        {
            foreach (var property in value.Properties)
            {
                switch (property.Value)
                {
                    case SteamObject nestedObject:
                        writer.Write((byte)0x00);
                        WriteNullTerminatedString(writer, property.Key);
                        WriteSteamObject(writer, nestedObject);
                        break;
                    case SteamString stringValue:
                        writer.Write((byte)0x01);
                        WriteNullTerminatedString(writer, property.Key);
                        WriteNullTerminatedString(writer, stringValue.Value);
                        break;
                    case SteamInt intValue:
                        writer.Write((byte)0x02);
                        WriteNullTerminatedString(writer, property.Key);
                        writer.Write(intValue.Value);
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported Steam shortcut value type: {property.Value.GetType().Name}");
                }
            }

            writer.Write((byte)0x08);
        }

        private static void WriteNullTerminatedString(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte)0x00);
        }

        private static SteamObject? FindMatchingSteamEntry(SteamObject shortcutsObject, string gameName, string quotedTargetPath)
        {
            foreach (var property in shortcutsObject.Properties)
            {
                if (property.Value is not SteamObject shortcut)
                    continue;

                string? existingName = GetShortcutString(shortcut, "appname");
                string? existingTarget = GetShortcutString(shortcut, "exe");
                var tags = TryGetObject(shortcut, "tags");
                bool hasLauncherTag = tags?.Properties.Any(p => p.Value is SteamString tag && string.Equals(tag.Value, LauncherSteamTag, StringComparison.Ordinal)) == true;

                if (string.Equals(existingName, gameName, StringComparison.Ordinal) &&
                    (string.Equals(existingTarget, quotedTargetPath, StringComparison.Ordinal) || hasLauncherTag))
                {
                    return shortcut;
                }
            }

            return null;
        }

        private static int GetNextShortcutIndex(SteamObject shortcutsObject)
        {
            int maxIndex = -1;
            foreach (var property in shortcutsObject.Properties)
            {
                if (int.TryParse(property.Key, out int index) && index > maxIndex)
                {
                    maxIndex = index;
                }
            }

            return maxIndex + 1;
        }

        private static void NormalizeShortcutIndices(SteamObject shortcutsObject)
        {
            for (int i = 0; i < shortcutsObject.Properties.Count; i++)
            {
                shortcutsObject.Properties[i] =
                    new KeyValuePair<string, SteamValue>(i.ToString(), shortcutsObject.Properties[i].Value);
            }
        }

        private static string? GetShortcutString(SteamObject shortcut, string key)
        {
            return shortcut.Properties
                .FirstOrDefault(property => string.Equals(property.Key, key, StringComparison.Ordinal))
                .Value is SteamString stringValue
                ? stringValue.Value
                : null;
        }

        private static void SetShortcutString(SteamObject shortcut, string key, string value)
        {
            SetShortcutValue(shortcut, key, new SteamString(value));
        }

        private static void SetShortcutInt(SteamObject shortcut, string key, int value)
        {
            SetShortcutValue(shortcut, key, new SteamInt(value));
        }

        private static void SetShortcutValue(SteamObject shortcut, string key, SteamValue value)
        {
            for (int i = 0; i < shortcut.Properties.Count; i++)
            {
                if (string.Equals(shortcut.Properties[i].Key, key, StringComparison.Ordinal))
                {
                    shortcut.Properties[i] = new KeyValuePair<string, SteamValue>(key, value);
                    return;
                }
            }

            shortcut.Properties.Add(new KeyValuePair<string, SteamValue>(key, value));
        }

        private static SteamObject EnsureObject(SteamObject shortcut, string key)
        {
            var existing = TryGetObject(shortcut, key);
            if (existing != null)
                return existing;

            var created = new SteamObject();
            shortcut.Properties.Add(new KeyValuePair<string, SteamValue>(key, created));
            return created;
        }

        private static SteamObject? TryGetObject(SteamObject shortcut, string key)
        {
            return shortcut.Properties
                .FirstOrDefault(property => string.Equals(property.Key, key, StringComparison.Ordinal))
                .Value as SteamObject;
        }

        private abstract record SteamValue;
        private sealed record SteamString(string Value) : SteamValue;
        private sealed record SteamInt(int Value) : SteamValue;
        private sealed record SteamObject : SteamValue
        {
            public List<KeyValuePair<string, SteamValue>> Properties { get; } = [];
        }
    }
}
