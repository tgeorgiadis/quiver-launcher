namespace QuiverLauncher.Services;

public static class GamepadNavigationRepeat
{
    public static bool ShouldAllowNavigationMove(
        int movesInHold,
        double elapsedMs,
        int initialDelay,
        int repeatDelay)
    {
        return movesInHold switch
        {
            0 => true,
            1 => elapsedMs >= initialDelay,
            _ => elapsedMs >= repeatDelay,
        };
    }
}
