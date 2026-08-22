using System.IO.Compression;
using System.Text;

namespace AppGeek.Services;

/// <summary>
/// Bundles everything needed to work out what AppGeek actually did on a machine:
/// every log, the settings and exclusions, and the environment it ran in.
///
/// Written because "it broke my PC" is impossible to act on without the run log that
/// records the exact winget command and the exit code it returned.
/// </summary>
public static class DiagnosticsExporter
{
    public static async Task<string> ExportAsync(string zipPath, WingetClient winget, SettingsService settings)
    {
        var staging = Path.Combine(Path.GetTempPath(), "AppGeek-diag-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(staging, "environment.txt"),
                await BuildEnvironmentReportAsync(winget).ConfigureAwait(false)).ConfigureAwait(false);

            CopyIfPresent(AppPaths.SettingsFile, staging, "settings.json");
            CopyIfPresent(AppPaths.ActivityFile, staging, "activity.json");

            var logs = Path.Combine(staging, "Logs");
            Directory.CreateDirectory(logs);

            if (Directory.Exists(AppPaths.LogDir))
            {
                // Most recent first, capped so a long-running install does not produce a
                // bundle too large to email.
                var files = new DirectoryInfo(AppPaths.LogDir)
                    .GetFiles("*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(40);

                foreach (var f in files)
                {
                    try { f.CopyTo(Path.Combine(logs, f.Name), overwrite: true); }
                    catch (Exception ex) { Log.Debug($"Could not include {f.Name}: {ex.Message}"); }
                }
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            Log.Info($"Diagnostics exported to {zipPath}");
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    private static async Task<string> BuildEnvironmentReportAsync(WingetClient winget)
    {
        var sb = new StringBuilder();
        sb.AppendLine("AppGeek diagnostics");
        sb.AppendLine("===================");
        sb.AppendLine($"Generated       : {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"AppGeek version : {AppInfo.CurrentVersionText}");
        sb.AppendLine($"Machine         : {Environment.MachineName}");
        sb.AppendLine($"OS              : {Environment.OSVersion}");
        sb.AppendLine($"64-bit OS       : {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"64-bit process  : {Environment.Is64BitProcess}");
        sb.AppendLine($"Elevated        : {Elevation.IsElevated}");
        sb.AppendLine($"User            : {Elevation.CurrentUserName}");
        sb.AppendLine($"Culture         : {System.Globalization.CultureInfo.CurrentCulture.Name}");
        sb.AppendLine($"winget path     : {winget.ExePath ?? "(not found)"}");
        sb.AppendLine($"winget version  : {winget.Version ?? "(unknown)"}");
        sb.AppendLine();

        if (winget.IsAvailable)
        {
            sb.AppendLine("winget --info");
            sb.AppendLine("-------------");
            try
            {
                var info = await ProcessRunner.RunAsync(winget.ExePath!, "--info",
                    timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                sb.AppendLine(WingetText.Clean(info.Combined));
            }
            catch (Exception ex)
            {
                sb.AppendLine("(could not be read: " + ex.Message + ")");
            }
        }

        return sb.ToString();
    }

    private static void CopyIfPresent(string source, string intoDir, string asName)
    {
        try
        {
            if (File.Exists(source)) File.Copy(source, Path.Combine(intoDir, asName), overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not include {asName}: {ex.Message}");
        }
    }
}
