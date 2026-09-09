using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public static class RegionCatalog
{
    public static IReadOnlyList<RegionOption> All { get; } = new[]
    {
        new RegionOption("Калининград", "Europe/Kaliningrad", "UTC+2"),
        new RegionOption("Москва", "Europe/Moscow", "UTC+3"),
        new RegionOption("Санкт-Петербург", "Europe/Moscow", "UTC+3"),
        new RegionOption("Беларусь", "Europe/Minsk", "UTC+3"),
        new RegionOption("Турция", "Europe/Istanbul", "UTC+3"),
        new RegionOption("Армения", "Asia/Yerevan", "UTC+4"),
        new RegionOption("Грузия", "Asia/Tbilisi", "UTC+4"),
        new RegionOption("ОАЭ", "Asia/Dubai", "UTC+4"),
        new RegionOption("Казахстан", "Asia/Almaty", "UTC+5"),
        new RegionOption("Узбекистан", "Asia/Tashkent", "UTC+5"),
        new RegionOption("Кыргызстан", "Asia/Bishkek", "UTC+6")
    };

    public static RegionOption Default => All[0];

    public static RegionOption Find(string? region, string? timeZoneId = null)
    {
        return All.FirstOrDefault(option =>
                   string.Equals(option.Region, region, StringComparison.CurrentCultureIgnoreCase))
               ?? All.FirstOrDefault(option =>
                   string.Equals(option.TimeZoneId, timeZoneId, StringComparison.Ordinal))
               ?? Default;
    }

    public static void Normalize(BrowserProfile profile)
    {
        var option = Find(profile.Region, profile.TimeZoneId);
        profile.Region = option.Region;
        profile.TimeZoneId = option.TimeZoneId;
    }
}
