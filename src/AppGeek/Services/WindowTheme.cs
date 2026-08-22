using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AppGeek.Services;

/// <summary>
/// Paints a window's system title bar dark so the native chrome matches the app instead
/// of sitting as a white strip on top. Windows 10 1809+ and Windows 11; older builds
/// keep the light title bar, which is not worth failing over.
/// </summary>
public static class WindowTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyDarkTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int enabled = 1;
            if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
        catch
        {
            // Older Windows builds simply keep the light title bar.
        }
    }
}
