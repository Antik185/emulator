namespace SpacesBrowser.Models;

public sealed class BrowserProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Новое пространство";
    public string Color { get; set; } = "#7C6CFF";
    public string HomeUrl { get; set; } = "https://ya.ru/";
    public string BrowserLanguage { get; set; } = "ru-RU";
    public string WindowPreset { get; set; } = "Maximized";
    public string Capability { get; set; } = "Both";
    public string Region { get; set; } = "Калининград";
    public string TimeZoneId { get; set; } = "Europe/Kaliningrad";
    public int ReadyAfterHours { get; set; } = 7 * 24;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastOpenedAtUtc { get; set; }
    public string? LastPublicIp { get; set; }
    public bool IsArchived { get; set; }

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }
}
