using System.Text.Json;
using AppGeek.Models;

namespace AppGeek.Services;

/// <summary>
/// Enumerates MSIX / Microsoft Store packages for the current user via PowerShell.
/// Kept separate and optional because it is noticeably slower than the registry scan.
/// </summary>
public sealed class AppxInventoryService
{
    private const string Script =
        "Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | " +
        "Select-Object Name, PackageFullName, Publisher, Version, InstallLocation | " +
        "ConvertTo-Json -Compress -Depth 2";

    public async Task<List<InstalledApp>> ScanAsync(ScanPhase? phase = null, CancellationToken ct = default)
    {
        var results = new List<InstalledApp>();
        phase?.Begin();
        try
        {
            var r = await ProcessRunner.RunAsync(
                "powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{Script}\"",
                ct,
                timeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);

            if (!r.Success || string.IsNullOrWhiteSpace(r.StdOut))
            {
                Log.Debug("Appx scan returned nothing.");
                return results;
            }

            using var doc = JsonDocument.Parse(r.StdOut.Trim());
            var root = doc.RootElement;
            var items = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            int done = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (++done % 10 == 0 && items.Count > 0) phase?.Report(done / (double)items.Count);

                var name = Str(item, "Name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                results.Add(new InstalledApp
                {
                    DisplayName = PrettifyPackageName(name!),
                    DisplayVersion = Str(item, "Version"),
                    Publisher = CleanPublisher(Str(item, "Publisher")),
                    InstallLocation = Str(item, "InstallLocation"),
                    PackageId = Str(item, "Name"),
                    SourceName = "msstore",
                    Scope = InstallScope.Store
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warn("Appx scan failed: " + ex.Message);
        }

        return results;
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>"Microsoft.WindowsTerminal" -> "Windows Terminal".</summary>
    private static string PrettifyPackageName(string packageName)
    {
        var last = packageName.Split('.').Last();
        var spaced = System.Text.RegularExpressions.Regex.Replace(last, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return spaced.Trim();
    }

    /// <summary>Turns an X.500 publisher string into just the common name.</summary>
    private static string? CleanPublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return null;
        foreach (var part in publisher.Split(','))
        {
            var t = part.Trim();
            if (t.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return t[3..].Trim();
        }
        return publisher;
    }
}
