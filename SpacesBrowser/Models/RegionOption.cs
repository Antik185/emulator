namespace SpacesBrowser.Models;

public sealed record RegionOption(string Region, string TimeZoneId, string UtcOffset)
{
    public string DisplayName => $"{Region} · {UtcOffset}";
    public override string ToString() => DisplayName;
}
