using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace QuiverLauncher.Services;

/// <summary>
/// Moves files/directories to the OS Recycle Bin / Trash instead of permanent deletion.
/// </summary>
public static class RecycleBinHelper
{
    public static void MoveToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException("Path does not exist.", fullPath);

        if (OperatingSystem.IsWindows())
            MoveToRecycleBinWindows(fullPath);
        else if (OperatingSystem.IsLinux())
            MoveToRecycleBinLinux(fullPath);
        else if (OperatingSystem.IsMacOS())
            MoveToRecycleBinMacOs(fullPath);
        else
            throw new PlatformNotSupportedException("Moving to Recycle Bin / Trash is not supported on this platform.");
    }

    [SupportedOSPlatform("windows")]
    private static void MoveToRecycleBinWindows(string fullPath)
    {
        // Double-null-terminated path list required by SHFileOperationW.
        var fromBytes = Encoding.Unicode.GetBytes(fullPath + "\0\0");
        var fromPtr = Marshal.AllocHGlobal(fromBytes.Length);
        try
        {
            Marshal.Copy(fromBytes, 0, fromPtr, fromBytes.Length);

            var fileOp = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = fromPtr,
                pTo = IntPtr.Zero,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = IntPtr.Zero,
            };

            var result = SHFileOperationW(ref fileOp);
            if (result != 0 || fileOp.fAnyOperationsAborted)
                throw new IOException($"Failed to move '{fullPath}' to the Recycle Bin (error {result}).");
        }
        finally
        {
            Marshal.FreeHGlobal(fromPtr);
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new IOException($"Failed to move '{fullPath}' to the Recycle Bin.");
    }

    /// <summary>
    /// Builds a FreeDesktop .trashinfo body. <paramref name="deletionTime"/> is formatted as local time.
    /// </summary>
    internal static string BuildLinuxTrashInfo(string originalPath, DateTime deletionTime)
    {
        var escapedPath = Uri.EscapeDataString(originalPath).Replace("%2F", "/", StringComparison.Ordinal);
        var local = deletionTime.Kind == DateTimeKind.Utc
            ? deletionTime.ToLocalTime()
            : deletionTime;
        var timestamp = local.ToString("yyyy-MM-ddTHH:mm:ss");
        return $"[Trash Info]\nPath={escapedPath}\nDeletionDate={timestamp}\n";
    }

    internal static string AllocateUniqueTrashName(string trashFilesDir, string trashInfoDir, string baseName)
    {
        var candidate = baseName;
        var counter = 1;
        while (File.Exists(Path.Combine(trashFilesDir, candidate)) ||
               Directory.Exists(Path.Combine(trashFilesDir, candidate)) ||
               File.Exists(Path.Combine(trashInfoDir, candidate + ".trashinfo")))
        {
            candidate = $"{baseName}.{counter}";
            counter++;
        }

        return candidate;
    }

    internal static string ResolveLinuxHomeTrashRoot(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<Environment.SpecialFolder, string>? getSpecialFolder = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        getSpecialFolder ??= Environment.GetFolderPath;

        var xdgDataHome = getEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
            return Path.Combine(xdgDataHome, "Trash");

        var home = getSpecialFolder(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("Could not resolve the user home directory for Trash.");

        return Path.Combine(home, ".local", "share", "Trash");
    }

    internal static bool AreSameFilesystem(string pathA, string pathB, Func<string, ulong?> getDeviceId)
    {
        var a = getDeviceId(pathA);
        var b = getDeviceId(pathB);
        return a.HasValue && b.HasValue && a.Value == b.Value;
    }

    /// <summary>
    /// Walks parents until the device id changes; that directory is the mount point.
    /// </summary>
    internal static string ResolveMountPoint(string fullPath, Func<string, ulong?> getDeviceId)
    {
        var path = Path.GetFullPath(fullPath);
        if (File.Exists(path))
            path = Path.GetDirectoryName(path) ?? path;

        path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(path))
            path = "/";

        var deviceId = getDeviceId(path);
        if (!deviceId.HasValue)
            return "/";

        while (path != "/")
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
                parent = "/";

            var parentDevice = getDeviceId(parent);
            if (!parentDevice.HasValue || parentDevice.Value != deviceId.Value)
                return path;

            path = parent;
        }

        return "/";
    }

    internal static string? TryResolveLinuxVolumeTrashRoot(
        string fullPath,
        int uid,
        Func<string, ulong?> getDeviceId)
    {
        var topDir = ResolveMountPoint(fullPath, getDeviceId);
        if (string.IsNullOrEmpty(topDir) || topDir == "/")
        {
            // Prefer home trash for the root filesystem.
            return null;
        }

        var stickyTrash = Path.Combine(topDir, ".Trash", uid.ToString());
        if (Directory.Exists(Path.Combine(topDir, ".Trash")))
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(stickyTrash, "files"));
                Directory.CreateDirectory(Path.Combine(stickyTrash, "info"));
                return stickyTrash;
            }
            catch
            {
                // Fall through to .Trash-$uid
            }
        }

        var volumeTrash = Path.Combine(topDir, $".Trash-{uid}");
        try
        {
            Directory.CreateDirectory(Path.Combine(volumeTrash, "files"));
            Directory.CreateDirectory(Path.Combine(volumeTrash, "info"));
            return volumeTrash;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("linux")]
    private static void MoveToRecycleBinLinux(string fullPath)
    {
        if (TryTrashViaGio(fullPath))
            return;

        MoveToLinuxTrashManual(fullPath, ResolveLinuxHomeTrashRoot(), TryGetUnixDeviceId, GetUnixUserId);
    }

    /// <summary>
    /// FreeDesktop manual trash. Exposed for tests with injectable trash root / device ids.
    /// </summary>
    internal static void MoveToLinuxTrashManual(
        string fullPath,
        string homeTrashRoot,
        Func<string, ulong?> getDeviceId,
        Func<int> getUid)
    {
        fullPath = Path.GetFullPath(fullPath);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException("Path does not exist.", fullPath);

        Directory.CreateDirectory(Path.Combine(homeTrashRoot, "files"));
        Directory.CreateDirectory(Path.Combine(homeTrashRoot, "info"));

        var homeTrashFiles = Path.Combine(homeTrashRoot, "files");
        var trashRoot = AreSameFilesystem(fullPath, homeTrashFiles, getDeviceId)
            ? homeTrashRoot
            : TryResolveLinuxVolumeTrashRoot(fullPath, getUid(), getDeviceId) ?? homeTrashRoot;

        try
        {
            TrashIntoRoot(fullPath, trashRoot, preferMove: true);
        }
        catch when (!string.Equals(trashRoot, homeTrashRoot, StringComparison.Ordinal))
        {
            // Volume trash failed — copy into home Trash with metadata.
            TrashIntoRoot(fullPath, homeTrashRoot, preferMove: false);
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new IOException($"Failed to move '{fullPath}' to Trash.");
    }

    private static void TrashIntoRoot(string fullPath, string trashRoot, bool preferMove)
    {
        var trashFiles = Path.Combine(trashRoot, "files");
        var trashInfo = Path.Combine(trashRoot, "info");
        Directory.CreateDirectory(trashFiles);
        Directory.CreateDirectory(trashInfo);

        var baseName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(baseName))
            baseName = "deleted-item";

        var trashName = AllocateUniqueTrashName(trashFiles, trashInfo, baseName);
        var destination = Path.Combine(trashFiles, trashName);
        var infoPath = Path.Combine(trashInfo, trashName + ".trashinfo");

        File.WriteAllText(infoPath, BuildLinuxTrashInfo(fullPath, DateTime.Now), Encoding.UTF8);

        try
        {
            MoveOrCopyToTrash(fullPath, destination, preferMove);
        }
        catch
        {
            try { File.Delete(infoPath); } catch { /* best effort */ }
            if (Directory.Exists(destination))
            {
                try { Directory.Delete(destination, recursive: true); } catch { /* best effort */ }
            }
            else if (File.Exists(destination))
            {
                try { File.Delete(destination); } catch { /* best effort */ }
            }

            throw;
        }
    }

    private static void MoveOrCopyToTrash(string fullPath, string destination, bool preferMove)
    {
        if (preferMove)
        {
            try
            {
                if (Directory.Exists(fullPath))
                    Directory.Move(fullPath, destination);
                else
                    File.Move(fullPath, destination);
                return;
            }
            catch (IOException)
            {
                // Cross-device link; fall through to copy+delete.
            }
        }

        if (Directory.Exists(fullPath))
        {
            CopyDirectory(fullPath, destination);
            Directory.Delete(fullPath, recursive: true);
        }
        else
        {
            File.Copy(fullPath, destination, overwrite: false);
            File.Delete(fullPath);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: false);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSub = Path.Combine(destinationDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSub);
        }
    }

    internal static bool TryTrashViaGio(
        string fullPath,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        startProcess ??= Process.Start;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "gio",
                ArgumentList = { "trash", "--", fullPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = startProcess(startInfo);
            if (process == null)
                return false;

            process.WaitForExit(120_000);
            if (process.ExitCode != 0)
                return false;

            return !File.Exists(fullPath) && !Directory.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    internal static ulong? TryGetUnixDeviceId(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "stat",
                ArgumentList = { "-c", "%d", path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5_000);
            if (process.ExitCode == 0 && ulong.TryParse(output, out var deviceId))
                return deviceId;
        }
        catch
        {
            // ignored
        }

        return null;
    }

    internal static int GetUnixUserId()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "id",
                ArgumentList = { "-u" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return 1000;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5_000);
            if (process.ExitCode == 0 && int.TryParse(output, out var uid))
                return uid;
        }
        catch
        {
            // ignored
        }

        return 1000;
    }

    [SupportedOSPlatform("macos")]
    private static void MoveToRecycleBinMacOs(string fullPath)
    {
        // Finder "delete" moves to Trash (recoverable), unlike rm.
        var escaped = fullPath.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var script = $"tell application \"Finder\" to delete (POSIX file \"{escaped}\" as alias)";

        var startInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            ArgumentList = { "-e", script },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new IOException("Failed to start osascript to move the item to Trash.");

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        if (process.ExitCode != 0)
            throw new IOException($"Failed to move '{fullPath}' to Trash: {stderr.Trim()}");

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw new IOException($"Failed to move '{fullPath}' to Trash.");
    }

    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_SILENT = 0x0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT fileOp);
}
