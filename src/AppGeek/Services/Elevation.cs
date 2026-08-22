using System.Diagnostics;
using System.Security.Principal;

namespace AppGeek.Services;

public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public static string CurrentUserName
    {
        get
        {
            try { return WindowsIdentity.GetCurrent().Name; }
            catch { return Environment.UserName; }
        }
    }

    /// <summary>
    /// Relaunches AppGeek with a UAC prompt, passing through arguments, and asks the
    /// current instance to exit. Returns false if the user cancelled the prompt.
    /// </summary>
    public static bool RelaunchElevated(params string[] args)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', args.Select(Quote))
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            // 1223 == ERROR_CANCELLED (user declined the UAC prompt)
            Log.Warn("Elevation declined or failed: " + ex.Message);
            return false;
        }
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
}
