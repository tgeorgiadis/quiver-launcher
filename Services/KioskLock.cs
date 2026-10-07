using System.Security.Cryptography;
using QuiverLauncher.Views;

namespace QuiverLauncher.Services;

/// <summary>Command-line and process arming for kiosk mode. Unlock state lives on <see cref="KioskSession"/>.</summary>
public static class KioskLaunch
{
    public static bool IsProcessArmed { get; private set; }

    public readonly record struct ParsedArgs(bool Kiosk, string[] Args);

    public static ParsedArgs Parse(string[] args)
    {
        var keep = new List<string>(args.Length);
        var kiosk = false;
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--kiosk", StringComparison.OrdinalIgnoreCase))
                kiosk = true;
            else
                keep.Add(arg);
        }

        return new ParsedArgs(kiosk, keep.ToArray());
    }

    public static void ArmProcess() => IsProcessArmed = true;

    public static bool IsStartupLocked(AppSettings? settings) =>
        IsProcessArmed || settings?.KioskMode == true;
}

/// <summary>Whether this process started locked, and whether the operator has unlocked it.</summary>
public sealed class KioskSession
{
    public bool Armed { get; }
    public bool Unlocked { get; private set; }
    public KioskSession(bool armed) => Armed = armed;
    public bool IsLocked => Armed && !Unlocked;
    public void Unlock() => Unlocked = true;
    public void Lock() => Unlocked = false;
}

/// <summary>Kiosk PIN storage and which library actions stay available while locked.</summary>
public static class KioskLock
{
    public const string UnlockChord = "Ctrl+Alt+K";
    private const int PinIterations = 100_000;

    public static bool AllowsLibraryAction(LibraryActionKind action) =>
        action is LibraryActionKind.LaunchGameMenu or LibraryActionKind.LibrarySearchClear;

    public static bool HasPin(AppSettings settings) =>
        !string.IsNullOrEmpty(settings.KioskPinHash) && !string.IsNullOrEmpty(settings.KioskPinSalt);

    public static void SetPin(AppSettings settings, string? pin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            settings.KioskPinSalt = "";
            settings.KioskPinHash = "";
            return;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        settings.KioskPinSalt = Convert.ToBase64String(salt);
        settings.KioskPinHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
            pin, salt, PinIterations, HashAlgorithmName.SHA256, 32));
    }

    public static bool VerifyPin(AppSettings settings, string pin)
    {
        if (!HasPin(settings))
            return true;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(settings.KioskPinSalt);
            expected = Convert.FromBase64String(settings.KioskPinHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, PinIterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
