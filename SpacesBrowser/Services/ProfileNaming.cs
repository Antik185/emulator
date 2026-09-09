using System.Globalization;
using System.Text.RegularExpressions;
using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public static class ProfileNaming
{
    private static readonly Regex StandardName = new(
        @"^(Space|Leha)(?:[ _-]*(\d+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Keep the high-water marks, including archived and subsequently deleted profiles.
    public static bool Observe(AppSettings settings, IEnumerable<BrowserProfile> profiles)
    {
        var space = Math.Max(0, settings.SpaceSequence);
        var leha = Math.Max(0, settings.LehaSequence);
        var spaceCount = 0L;
        var lehaCount = 0L;
        foreach (var profile in profiles)
        {
            var match = StandardName.Match(profile.Name.Trim());
            if (!match.Success) continue;
            var number = 1L;
            if (match.Groups[2].Success &&
                !long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number)) continue;
            if (match.Groups[1].Value.Equals("Space", StringComparison.OrdinalIgnoreCase))
            {
                spaceCount++;
                space = Math.Max(space, number);
            }
            else
            {
                lehaCount++;
                leha = Math.Max(leha, number);
            }
        }
        space = Math.Max(space, spaceCount);
        leha = Math.Max(leha, lehaCount);
        var changed = space != settings.SpaceSequence || leha != settings.LehaSequence;
        settings.SpaceSequence = space;
        settings.LehaSequence = leha;
        return changed;
    }

    public static string NextName(AppSettings settings, string prefix)
    {
        var last = prefix switch
        {
            "Space" => settings.SpaceSequence,
            "Leha" => settings.LehaSequence,
            _ => throw new ArgumentException("Unknown standard name.", nameof(prefix))
        };
        return prefix + " " + checked(Math.Max(0, last) + 1).ToString(CultureInfo.InvariantCulture);
    }
}
