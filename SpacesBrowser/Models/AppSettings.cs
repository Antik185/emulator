namespace SpacesBrowser.Models;

public sealed class AppSettings
{
    public string? CustomBrowserPath { get; set; }

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }
}
