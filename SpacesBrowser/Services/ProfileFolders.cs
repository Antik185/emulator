using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public static class ProfileFolders
{
    public static bool Contains(BrowserProfile profile, string folder)
    {
        if (folder == "Archive") return profile.IsArchived;
        if (profile.IsArchived) return false;
        return folder switch
        {
            "All" => true,
            "Restaurant" => profile.Capability is "Restaurant" or "Both",
            "Store" => profile.Capability is "Store" or "Both",
            "Both" => profile.Capability == "Both",
            _ => false
        };
    }

    public static IEnumerable<BrowserProfile> Select(IEnumerable<BrowserProfile> profiles, string folder) =>
        profiles.Reverse().Where(profile => Contains(profile, folder)).OrderByDescending(profile => profile.CreatedAtUtc);
}
