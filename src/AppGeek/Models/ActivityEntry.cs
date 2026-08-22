using System.Text.Json.Serialization;

namespace AppGeek.Models;

public enum ActivityKind { Success, Warning, Failure, Info }

public sealed class ActivityEntry
{
    [JsonPropertyName("whenUtc")] public DateTime WhenUtc { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("kind")] public ActivityKind Kind { get; set; } = ActivityKind.Info;
    [JsonPropertyName("text")] public string Text { get; set; } = "";

    [JsonIgnore]
    public string WhenDisplay
    {
        get
        {
            var local = WhenUtc.ToLocalTime();
            var d = DateTime.Now.Date - local.Date;
            if (d.TotalDays < 1) return "Today, " + local.ToString("HH:mm");
            if (d.TotalDays < 2) return "Yesterday, " + local.ToString("HH:mm");
            if (d.TotalDays < 7) return local.ToString("dddd, HH:mm");
            return local.ToString("dd MMM, HH:mm");
        }
    }

    [JsonIgnore]
    public string Glyph => Kind switch
    {
        ActivityKind.Success => "✓",
        ActivityKind.Warning => "!",
        ActivityKind.Failure => "✕",
        _ => "·"
    };
}
