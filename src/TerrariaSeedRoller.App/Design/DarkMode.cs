using System.Runtime.InteropServices;

namespace TerrariaSeedRoller.App.Design;

/// <summary>
/// Opts the process into the platform dark theme so that the few remaining
/// OS-drawn pieces (window title bar, scroll bars, message boxes) do not appear
/// as bright slabs inside the dark interface.
/// </summary>
internal static class DarkMode
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    private static bool _processPrepared;

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
    private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    /// <summary>Must run before the first window is created.</summary>
    public static void PrepareProcess()
    {
        if (_processPrepared) return;
        _processPrepared = true;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try
        {
            // 2 == ForceDark
            SetPreferredAppMode(2);
        }
        catch (Exception)
        {
            // The ordinal export is undocumented; failing here is harmless.
        }
    }

    /// <summary>Applies dark scroll bars and control theming to a window handle.</summary>
    public static void Apply(Control control)
    {
        if (!control.IsHandleCreated) return;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try
        {
            AllowDarkModeForWindow(control.Handle, true);
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
        catch (Exception)
        {
            // Ignored: the control simply keeps its default scroll bar.
        }
    }

    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try
        {
            int enabled = 1;
            if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            }
        }
        catch (Exception)
        {
            // Ignored: older builds keep the default title bar colour.
        }
    }
}
