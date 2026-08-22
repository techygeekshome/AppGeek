using System.Globalization;
using System.Text;
using AppGeek.Models;

namespace AppGeek.Services;

/// <summary>
/// Produces the shareable inventory/update report. HTML is written rather than PDF so
/// there is no PDF dependency in v1 — the browser's "Print to PDF" covers it, and a
/// proper PDF writer can be dropped in later.
/// </summary>
public static class ReportExporter
{
    public static string ExportCsv(string path, IEnumerable<InstalledApp> apps, IEnumerable<UpdateCandidate> updates)
    {
        var updateById = updates
            .GroupBy(u => u.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("Application,Version,Available,Publisher,Size (bytes),Installed,Scope,Source,Package ID,Status");

        foreach (var a in apps.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            UpdateCandidate? u = null;
            if (a.PackageId is not null) updateById.TryGetValue(a.PackageId, out u);

            var status = u is null ? (a.IsTracked ? "Up to date" : "Not tracked")
                       : u.IsPinned ? "Pinned"
                       : u.IsSecuritySensitive ? "Update available (security)"
                       : "Update available";

            sb.AppendLine(string.Join(',', new[]
            {
                Csv(a.DisplayName), Csv(a.DisplayVersion), Csv(u?.AvailableVersion), Csv(a.Publisher),
                a.EstimatedSizeBytes.ToString(CultureInfo.InvariantCulture),
                Csv(a.InstallDate?.ToString("yyyy-MM-dd")),
                Csv(a.ScopeDisplay), Csv(a.SourceName), Csv(a.PackageId), Csv(status)
            }));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Log.Info($"CSV report written to {path}");
        return path;
    }

    public static string ExportHtml(string path, IEnumerable<InstalledApp> apps, IEnumerable<UpdateCandidate> updates)
    {
        var appList = apps.ToList();
        var updateList = updates.ToList();
        var security = updateList.Count(u => u.IsSecuritySensitive);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>AppGeek inventory report</title><style>");
        sb.AppendLine(@"
body{margin:0;padding:32px;background:#0A0D16;color:#fff;
     font-family:'Segoe UI',system-ui,sans-serif;font-size:14px}
h1{font-size:22px;margin:0 0 4px}
.sub{color:#7C8699;font-size:13px;margin-bottom:24px}
.tiles{display:flex;gap:12px;margin-bottom:26px;flex-wrap:wrap}
.tile{background:#111725;border:1px solid #242D42;border-radius:10px;padding:14px 18px;min-width:150px}
.tile .k{font-size:10.5px;letter-spacing:.8px;text-transform:uppercase;color:#7C8699;font-weight:700}
.tile .v{font-size:26px;font-weight:700;margin-top:6px}
.tile.warn .v{color:#E8834A}
.tile.accent .v{color:#6BA8F0}
table{width:100%;border-collapse:collapse;background:#111725;border:1px solid #242D42;border-radius:10px;overflow:hidden}
th{text-align:left;font-size:10.5px;letter-spacing:.7px;text-transform:uppercase;color:#5C6578;
   padding:11px 14px;border-bottom:1px solid #242D42}
td{padding:9px 14px;border-bottom:1px solid #161D2C;color:#BEC4D2;font-size:12.5px}
td.nm{color:#fff;font-weight:600}
.tag{font-size:10px;font-weight:700;padding:2px 7px;border-radius:5px;text-transform:uppercase}
.tag.sec{background:#3A1B10;color:#F0916B}
.tag.upd{background:#102A38;color:#5CC8D6}
.tag.pin{background:#2A2410;color:#D9BC5E}
footer{margin-top:26px;color:#4E5666;font-size:11.5px}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>AppGeek inventory report</h1>");
        sb.AppendLine($"<div class=\"sub\">{Html(Environment.MachineName)} · generated {DateTime.Now:dd MMM yyyy HH:mm}</div>");

        sb.AppendLine("<div class=\"tiles\">");
        sb.AppendLine($"<div class=\"tile accent\"><div class=\"k\">Updates available</div><div class=\"v\">{updateList.Count}</div></div>");
        sb.AppendLine($"<div class=\"tile warn\"><div class=\"k\">Security updates</div><div class=\"v\">{security}</div></div>");
        sb.AppendLine($"<div class=\"tile\"><div class=\"k\">Apps installed</div><div class=\"v\">{appList.Count}</div></div>");
        sb.AppendLine($"<div class=\"tile\"><div class=\"k\">Tracked</div><div class=\"v\">{appList.Count(a => a.IsTracked)}</div></div>");
        sb.AppendLine("</div>");

        if (updateList.Count > 0)
        {
            sb.AppendLine("<h2 style=\"font-size:15px;margin:0 0 10px\">Needs attention</h2><table>");
            sb.AppendLine("<tr><th>Application</th><th>Installed</th><th>Available</th><th>Source</th><th>Flags</th></tr>");
            foreach (var u in updateList)
            {
                var flags = new List<string>();
                if (u.IsSecuritySensitive) flags.Add("<span class=\"tag sec\">Security</span>");
                if (u.IsMajorVersionChange) flags.Add("<span class=\"tag upd\">Major</span>");
                if (u.IsPinned) flags.Add("<span class=\"tag pin\">Pinned</span>");
                sb.AppendLine($"<tr><td class=\"nm\">{Html(u.Name)}</td><td>{Html(u.CurrentVersion)}</td>" +
                              $"<td>{Html(u.AvailableVersion)}</td><td>{Html(u.SourceName)}</td>" +
                              $"<td>{string.Join(" ", flags)}</td></tr>");
            }
            sb.AppendLine("</table><br>");
        }

        sb.AppendLine("<h2 style=\"font-size:15px;margin:0 0 10px\">All installed applications</h2><table>");
        sb.AppendLine("<tr><th>Application</th><th>Version</th><th>Publisher</th><th>Size</th><th>Installed</th><th>Source</th></tr>");
        foreach (var a in appList.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            sb.AppendLine($"<tr><td class=\"nm\">{Html(a.DisplayName)}</td><td>{Html(a.DisplayVersion)}</td>" +
                          $"<td>{Html(a.Publisher)}</td><td>{a.SizeDisplay}</td>" +
                          $"<td>{a.InstallDate?.ToString("dd MMM yyyy") ?? "—"}</td>" +
                          $"<td>{Html(a.SourceName ?? "—")}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<footer>Generated by AppGeek · TechyGeeksHome · techygeekshome.info</footer>");
        sb.AppendLine("</body></html>");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Log.Info($"HTML report written to {path}");
        return path;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }

    private static string Html(string? value) =>
        string.IsNullOrEmpty(value) ? "—" :
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
