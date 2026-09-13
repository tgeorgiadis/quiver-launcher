using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services
{
    public static class PlatformAssetMatcher
    {
        public static string GetPlatformIdentifier(TargetOS platform)
        {
            if (platform == TargetOS.Auto)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return "Windows";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return "macOS";

                if (OperatingSystem.IsAndroid())
                    return "Android";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return RuntimeInformation.OSArchitecture switch
                    {
                        Architecture.Arm64 => "Linux-ARM64",
                        Architecture.X64 => "Linux-X64",
                        Architecture.X86 => "Linux-X86",
                        Architecture.Arm => "Linux-ARM",
                        _ => "Linux-X64"
                    };
                }

                throw new PlatformNotSupportedException("Unsupported operating system");
            }

            return platform switch
            {
                TargetOS.Windows => "Windows",
                TargetOS.MacOS => "macOS",
                TargetOS.LinuxX64 => "Linux-X64",
                TargetOS.LinuxARM64 => "Linux-ARM64",
                TargetOS.Android => "Android",
                _ => throw new PlatformNotSupportedException("Unsupported target OS in settings")
            };
        }

        private static readonly string[] NonWindowsPlatformMarkers =
        [
            "linux", "macos", "osx", "darwin", "apple",
            ".deb", ".rpm", "appimage", ".dmg", ".pkg",
            "android", "arm64-v8a", ".apk", "switch"
        ];

        public static bool IsWindowsAsset(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                return false;

            var assetNameLower = assetName.ToLowerInvariant();
            if (IsIosAsset(assetNameLower) || IsDedicatedDeviceAsset(assetNameLower) || DownloadAssetPolicy.IsAuxiliary(assetNameLower) || IsDebugSymbolPackage(assetNameLower) || HasNonWindowsPlatformMarker(assetNameLower))
                return false;

            return HasExplicitWindowsMarker(assetNameLower) || IsUnlabeledWindowsArchive(assetNameLower);
        }

        public static bool MatchesPlatform(string assetName, string platformIdentifier)
        {
            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(platformIdentifier))
            {
                return false;
            }

            var assetNameLower = assetName.ToLowerInvariant();
            var platformLower = platformIdentifier.ToLowerInvariant();

            // Symbol archives are downloadable, but are not runnable platform builds.
            if (IsIosAsset(assetNameLower) || IsDedicatedDeviceAsset(assetNameLower) || DownloadAssetPolicy.IsAuxiliary(assetNameLower) || IsDebugSymbolPackage(assetNameLower))
                return false;


            if (platformLower.Contains("windows"))
            {

                if (HasNonWindowsPlatformMarker(assetNameLower))
                {
                    return false;
                }

                bool isWindows = HasExplicitWindowsMarker(assetNameLower) ||
                    IsUnlabeledWindowsArchive(assetNameLower);

                return isWindows;
            }

            if (platformLower.Contains("macos") || platformLower.Contains("mac"))
            {

                if (HasAnyOf(assetNameLower, "linux", "windows", "win32", "win64", ".exe", ".msi", "switch", "android", ".apk"))
                {
                    return false;
                }

                bool isMac = HasMacPlatformMarker(assetNameLower);

                return isMac;
            }

            if (platformLower.Contains("linux"))
            {

                if (IsWindowsAsset(assetNameLower) || HasMacPlatformMarker(assetNameLower) || HasAnyOf(assetNameLower, "windows", "win32", "win64", "macos", "osx", "darwin", ".exe", ".msi", ".dmg", "switch", "android", ".apk"))
                {
                    return false;
                }

                bool hasLinux = HasAnyOf(assetNameLower, "linux", "appimage", ".deb", ".rpm", "tar.gz", "tar.xz");

                if (!hasLinux)
                {
                    return false;
                }

                if (platformLower.Contains("arm64") || platformLower.Contains("arm") || platformLower.Contains("aarch64"))
                {
                    bool isArm = !HasAnyOf(assetNameLower, "x64", "x86", "amd64", "i686", "i386", "i586", "armv7", "armhf", "arm-");
                    return isArm;
                }

                if (!platformLower.Contains("arm"))
                {
                    if (HasAnyOf(assetNameLower, "i686", "i386", "i586", "x86-linux", "-i686-"))
                    {
                        return false;
                    }

                    if (HasAnyOf(assetNameLower, "arm64", "aarch64", "armv7", "armhf", "arm-"))
                    {
                        return false;
                    }

                    // Explicit x64 markers, or arch-unspecified Linux builds (e.g. CrashBandicoot_Linux).
                    return true;
                }
            }

            if (platformLower.Contains("android"))
            {
                if (HasMacPlatformMarker(assetNameLower) || HasAnyOf(assetNameLower, "windows", "win32", "win64", "linux", "macos", "osx", "darwin",
                        ".exe", ".msi", "appimage", ".dmg", ".deb", ".rpm", "switch"))
                {
                    return false;
                }

                return HasAnyOf(assetNameLower, "android", "arm64-v8a")
                       || assetNameLower.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
            }

            bool fallbackMatch = assetNameLower.Contains(platformLower);
            return fallbackMatch;
        }

        public static bool HasAnyOf(string input, params string[] substrings)
        {
            foreach (var substring in substrings)
            {
                if (input.Contains(substring))
                    return true;
            }

            return false;
        }

        // Match platform tokens, not substrings such as "BIOS" or "Studios".
        // iOS is a known incompatible platform, not an unknown desktop archive.
        public static bool IsIosAsset(string assetName) =>
            Regex.IsMatch(assetName, @"(?:^|[^a-z0-9])(?:ios|ipados|iphone|ipad|iphoneos|iphonesimulator)(?:$|[^a-z0-9])|\.ipa(?:$|[._-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // These packages target a console or a dedicated handheld environment, even
        // when that environment uses Windows/Linux internally. Do not treat them as
        // generic desktop archives or offer them as unknown downloads.
        public static bool IsDedicatedDeviceAsset(string assetName) =>
            Regex.IsMatch(assetName, @"(?:^|[^a-z0-9])(?:xbox|portmaster|stockos\d*|rg\d{2,3}[a-z0-9]*)(?:$|[^a-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool HasNonWindowsPlatformMarker(string assetNameLower) =>
            HasAnyOf(assetNameLower, NonWindowsPlatformMarkers) || HasMacPlatformMarker(assetNameLower);

        private static bool HasMacPlatformMarker(string assetNameLower) =>
            HasAnyOf(assetNameLower, "macos", "osx", "darwin", ".dmg", ".pkg", "apple") ||
            Regex.IsMatch(assetNameLower, @"(?:^|[^a-z0-9])mac(?:$|[^a-z0-9])");

        private static bool IsDebugSymbolPackage(string assetNameLower) =>
            Regex.IsMatch(assetNameLower, @"(?:^|[._-])(?:pdb|symbols|debugsymbols|debug[._-]symbols)(?:$|[._-])");

        private static bool HasExplicitWindowsMarker(string assetNameLower) =>
            HasAnyOf(assetNameLower,
                "windows", "win64", "win32", "win-x64", "win-x86",
                "-win.", "_win.", ".exe", ".msi", "msvc", "mingw") ||
            Regex.IsMatch(assetNameLower, @"[_-]win[_-]|[_-]win\d|^win[_-]");

        private static bool IsUnlabeledWindowsArchive(string assetNameLower) =>
            assetNameLower.EndsWith(".zip", StringComparison.Ordinal) ||
            assetNameLower.EndsWith(".7z", StringComparison.Ordinal) ||
            assetNameLower.EndsWith(".rar", StringComparison.Ordinal);
    }
}
