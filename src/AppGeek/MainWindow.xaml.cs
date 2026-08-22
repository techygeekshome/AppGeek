using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AppGeek.Services;

namespace AppGeek;

public partial class MainWindow : Window
{
    // Windows 10 1809+ / Windows 11: paints the system title bar dark so the
    // native chrome matches the app instead of sitting as a white strip on top.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        Closing += OnClosing;
    }

    /// <summary>
    /// Closing the window while an installer is running is the single most damaging thing a
    /// user can do with this app, and it is an easy mistake to make: a silent installer can
    /// go minutes without printing anything, so AppGeek looks frozen when it is working
    /// perfectly well. Ending it at that moment can leave an application half removed.
    ///
    /// So: say plainly what is happening, and make continuing the default.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var runner = App.Shell?.Runner;
        if (runner is null || !runner.IsRunning) return;

        var answer = MessageBox.Show(
            this,
            "AppGeek is installing software right now.\n\n" +
            "Closing it will not stop the installer that is already running, and if Windows " +
            "ends it part-way through you can be left with an application that is half " +
            "removed and will not start.\n\n" +
            "A silent installer often shows no progress for several minutes. That is normal.\n\n" +
            "Close AppGeek anyway?",
            "A run is still in progress",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            Log.Warn("The window was closed by the user while a run was still in progress.");
            return;
        }

        e.Cancel = true;
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int enabled = 1;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }
        catch
        {
            // Older Windows builds simply keep the light title bar. Not worth failing over.
        }
    }
}
