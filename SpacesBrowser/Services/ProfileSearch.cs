using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public static class ProfileSearch
{
    public static IEnumerable<BrowserProfile> Select(IEnumerable<BrowserProfile> profiles, string query)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return Enumerable.Empty<BrowserProfile>();
        return profiles.Reverse()
            .Where(profile => Matches(profile, terms))
            .OrderByDescending(profile => profile.CreatedAtUtc);
    }

    private static bool Matches(BrowserProfile profile, IEnumerable<string> terms)
    {
        var typeKeywords = profile.Capability switch
        {
            "Restaurant" => "ресторан рестораны restaurant",
            "Store" => "магазин магазины store shop",
            _ => "ресторан рестораны магазин магазины restaurant store shop оба"
        };
        var archiveKeywords = profile.IsArchived ? "архив archive archived" : "активный active";
        var text = string.Join(' ', profile.Name, profile.Comment, profile.Region, profile.TimeZoneId,
            profile.LastPublicIp, profile.BrowserLanguage, typeKeywords, archiveKeywords);
        return terms.All(term => text.Contains(term, StringComparison.CurrentCultureIgnoreCase));
    }
}
