namespace AppGeek.Services;

public static class AppPaths
{
    public static string RoamingDir { get; } = EnsureDir(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppGeek"));

    public static string ProgramDataDir { get; } = EnsureDir(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AppGeek"));

    public static string LogDir { get; } = EnsureDir(Path.Combine(RoamingDir, "Logs"));
    public static string CacheDir { get; } = EnsureDir(Path.Combine(ProgramDataDir, "Cache"));

    public static string SettingsFile => Path.Combine(RoamingDir, "settings.json");
    public static string ActivityFile => Path.Combine(RoamingDir, "activity.json");
    public static string CatalogueCacheFile => Path.Combine(RoamingDir, "catalogue.json");

    private static string EnsureDir(string p)
    {
        try { Directory.CreateDirectory(p); } catch { /* non-fatal */ }
        return p;
    }
}
