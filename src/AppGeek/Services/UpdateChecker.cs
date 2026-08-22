using System.Text.Json;

namespace AppGeek.Services;

public sealed record UpdateResult(
    bool UpdateAvailable,
    string? LatestVersion,
    string? ReleaseUrl,
    string Message,
    bool Failed = false);

/// <summary>
/// Asks GitHub's releases API whether a newer tag exists.
///
/// Manual only, and it never downloads or installs anything — it reports, and offers to
/// open the release page. This matches the rest of the Geek range: an updater that
/// silently self-updates is exactly the behaviour AppGeek refuses to inflict on other
/// people's software, so it would be odd to do it to its own.
/// </summary>
public static class UpdateChecker
{
    public static async Task<UpdateResult> CheckAsync(CancellationToken ct = default)
    {
        var current = WingetBootstrapper.ParseVersion(AppInfo.CurrentVersionText);
        var url = $"https://api.github.com/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/latest";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // GitHub rejects requests with no user agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"{AppInfo.Name}/{AppInfo.CurrentVersionText} (+{AppInfo.WebsiteUrl})");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No releases published yet. Not an error worth alarming anyone about.
                return new UpdateResult(false, null, null,
                    $"You are running the latest version ({AppInfo.CurrentVersionText}).");
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = Str(root, "tag_name");
            var htmlUrl = Str(root, "html_url");
            var latest = WingetBootstrapper.ParseVersion(tag);

            if (latest is null)
            {
                Log.Warn($"Could not read a version from release tag '{tag}'.");
                return new UpdateResult(false, tag, htmlUrl,
                    "Could not read the latest version number from GitHub.", Failed: true);
            }

            if (current is not null && latest > current)
            {
                Log.Info($"Update available: {current} -> {latest}");
                return new UpdateResult(true, latest.ToString(3), htmlUrl,
                    $"Version {latest.ToString(3)} is available. You have {AppInfo.CurrentVersionText}.");
            }

            return new UpdateResult(false, latest.ToString(3), htmlUrl,
                $"You are running the latest version ({AppInfo.CurrentVersionText}).");
        }
        catch (OperationCanceledException)
        {
            return new UpdateResult(false, null, null, "The update check was cancelled.", Failed: true);
        }
        catch (Exception ex)
        {
            Log.Warn("Update check failed: " + ex.Message);
            return new UpdateResult(false, null, null,
                "Could not reach GitHub to check for updates.", Failed: true);
        }
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
