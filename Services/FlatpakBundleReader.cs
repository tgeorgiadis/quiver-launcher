using System.Runtime.InteropServices;

namespace QuiverLauncher.Services;

public interface IFlatpakBundleReader
{
    void CheckAvailable();
    FlatpakReceipt Read(string path, string releaseTag);
}

/// <summary>Read the bundle's authoritative identity using the host's libflatpak.</summary>
public sealed class FlatpakBundleReader : IFlatpakBundleReader
{
    public void CheckAvailable()
    {
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException("Flatpak bundles can only be installed on Linux desktops.");
        try { _ = flatpak_get_default_arch(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException(FlatpakService.SetupGuidance, ex);
        }
    }

    public FlatpakReceipt Read(string path, string releaseTag)
    {
        CheckAvailable();
        var file = g_file_new_for_path(Path.GetFullPath(path));
        IntPtr bundle = IntPtr.Zero, error = IntPtr.Zero;
        try
        {
            bundle = flatpak_bundle_ref_new(file, out error);
            if (bundle == IntPtr.Zero)
                throw new InvalidDataException("This file is not a valid Flatpak bundle.");
            if (flatpak_ref_get_kind(bundle) != 0)
                throw new InvalidDataException("This bundle contains a runtime, not an application.");
            var receipt = new FlatpakReceipt(Text(flatpak_ref_get_name(bundle)),
                Text(flatpak_ref_get_arch(bundle)), Text(flatpak_ref_get_branch(bundle)),
                Text(flatpak_ref_get_commit(bundle)), releaseTag);
            receipt.Validate();
            if (receipt.Architecture != Text(flatpak_get_default_arch()))
                throw new InvalidDataException($"This Flatpak targets {receipt.Architecture}, which does not match this computer.");
            return receipt;
        }
        finally
        {
            if (bundle != IntPtr.Zero) g_object_unref(bundle);
            if (file != IntPtr.Zero) g_object_unref(file);
            if (error != IntPtr.Zero) g_error_free(error);
        }
    }

    private static string Text(IntPtr value) => Marshal.PtrToStringUTF8(value) ?? "";
    [DllImport("libgio-2.0.so.0")] private static extern IntPtr g_file_new_for_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr value);
    [DllImport("libglib-2.0.so.0")] private static extern void g_error_free(IntPtr value);
    [DllImport("libflatpak.so.0")] private static extern IntPtr flatpak_get_default_arch();
    [DllImport("libflatpak.so.0")] private static extern IntPtr flatpak_bundle_ref_new(IntPtr file, out IntPtr error);
    [DllImport("libflatpak.so.0")] private static extern int flatpak_ref_get_kind(IntPtr reference);
    [DllImport("libflatpak.so.0")] private static extern IntPtr flatpak_ref_get_name(IntPtr reference);
    [DllImport("libflatpak.so.0")] private static extern IntPtr flatpak_ref_get_arch(IntPtr reference);
    [DllImport("libflatpak.so.0")] private static extern IntPtr flatpak_ref_get_branch(IntPtr reference);
    [DllImport("libflatpak.so.0")] private static extern IntPtr flatpak_ref_get_commit(IntPtr reference);
}
