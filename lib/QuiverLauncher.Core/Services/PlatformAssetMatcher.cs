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

        public static bool IsWindowsAsset(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName))
                return false;

            var assetNameLower = assetName.ToLowerInvariant();
            return HasAnyOf(assetNameLower, "windows", "win64", "win32", "win-x64", "win-x86", "-win.", "_win.", ".exe", ".msi") ||
                   Regex.IsMatch(assetNameLower, @"[_-]win[_-]|[_-]win\d|^win[_-]");
        }

        public static bool MatchesPlatform(string assetName, string platformIdentifier)
        {
            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(platformIdentifier))
            {
                System.Diagnostics.Debug.WriteLine("Invalid input: assetName or platformIdentifier is null/empty");
                return false;
            }

            var assetNameLower = assetName.ToLowerInvariant();
            var platformLower = platformIdentifier.ToLowerInvariant();

            System.Diagnostics.Debug.WriteLine($"Checking asset: {assetName}");
            System.Diagnostics.Debug.WriteLine($"Platform identifier: {platformIdentifier}");

            if (platformLower.Contains("windows"))
            {
                System.Diagnostics.Debug.WriteLine("Checking Windows patterns...");

                if (HasAnyOf(assetNameLower, "linux", "macos", "osx", "darwin", "apple", ".deb", ".rpm", ".appimage", ".dmg", ".pkg", "switch"))
                {
                    System.Diagnostics.Debug.WriteLine("Excluded: contains non-Windows platform marker");
                    return false;
                }

                bool isWindows = HasAnyOf(assetNameLower,
                    "windows", "win64", "win32", "win-x64", "win-x86",
                    "-win.", "_win.", ".exe", ".msi", "msvc", "mingw") ||
                    Regex.IsMatch(assetNameLower, @"[_-]win[_-]|[_-]win\d|^win[_-]");

                System.Diagnostics.Debug.WriteLine($"Windows match result: {isWindows}");
                return isWindows;
            }

            if (platformLower.Contains("macos") || platformLower.Contains("mac"))
            {
                System.Diagnostics.Debug.WriteLine("Checking macOS patterns...");

                if (HasAnyOf(assetNameLower, "linux", "windows", "win32", "win64", ".exe", ".msi", "switch"))
                {
                    System.Diagnostics.Debug.WriteLine("Excluded: contains non-macOS platform marker");
                    return false;
                }

                bool isMac = HasAnyOf(assetNameLower, "macos", "osx", "darwin", ".dmg", ".pkg") ||
                             (assetNameLower.Contains("mac") && !assetNameLower.Contains("machin"));

                System.Diagnostics.Debug.WriteLine($"macOS match result: {isMac}");
                return isMac;
            }

            if (platformLower.Contains("linux"))
            {
                System.Diagnostics.Debug.WriteLine("Checking Linux patterns...");

                if (HasAnyOf(assetNameLower, "windows", "win32", "win64", "macos", "osx", "darwin", ".exe", ".msi", ".dmg", "switch"))
                {
                    System.Diagnostics.Debug.WriteLine("Excluded: contains non-Linux platform marker");
                    return false;
                }

                bool hasLinux = HasAnyOf(assetNameLower, "linux", ".appimage", ".deb", ".rpm", "tar.gz", "tar.xz");

                if (!hasLinux)
                {
                    System.Diagnostics.Debug.WriteLine("No Linux markers found");
                    return false;
                }

                if (platformLower.Contains("arm64") || platformLower.Contains("arm") || platformLower.Contains("aarch64"))
                {
                    bool isArm = HasAnyOf(assetNameLower, "arm64", "aarch64", "armv7", "armhf", "arm-");
                    System.Diagnostics.Debug.WriteLine($"Linux ARM64 match result: {isArm}");
                    return isArm;
                }

                if (!platformLower.Contains("arm"))
                {
                    if (HasAnyOf(assetNameLower, "i686", "i386", "i586", "x86-linux", "-i686-"))
                    {
                        System.Diagnostics.Debug.WriteLine("Excluded: 32-bit Linux build");
                        return false;
                    }

                    if (HasAnyOf(assetNameLower, "arm64", "aarch64", "armv7", "armhf", "arm-"))
                    {
                        System.Diagnostics.Debug.WriteLine("Excluded: ARM Linux build for x64 platform");
                        return false;
                    }

                    // Explicit x64 markers, or arch-unspecified Linux builds (e.g. CrashBandicoot_Linux).
                    System.Diagnostics.Debug.WriteLine("Linux x64 match result: True");
                    return true;
                }
            }

            if (platformLower.Contains("android"))
            {
                if (HasAnyOf(assetNameLower, "windows", "win32", "win64", "linux", "macos", "osx", "darwin",
                        ".exe", ".msi", ".appimage", ".dmg", ".deb", ".rpm", "switch"))
                {
                    return false;
                }

                return HasAnyOf(assetNameLower, ".apk", "android", "arm64-v8a", "aarch64")
                       || assetNameLower.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
            }

            System.Diagnostics.Debug.WriteLine("Using fallback substring match");
            bool fallbackMatch = assetNameLower.Contains(platformLower);
            System.Diagnostics.Debug.WriteLine($"Fallback match result: {fallbackMatch}");
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
    }
}
