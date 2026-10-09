using System.Runtime.InteropServices;

namespace CodexDiscordPresence;

internal static class DashboardAnimationPreferences
{
    public static bool AnimationsEnabled => SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
