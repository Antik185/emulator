using System.Text.Json;
using SpacesBrowser.Models;
using SpacesBrowser.Services;

static class ProfileFeatureTests
{
    public static void Run(string root)
    {
        var settings = new AppSettings();
        Require(ProfileNaming.NextName(settings, "Space") == "Space 1", "Initial Space name");
        Require(ProfileNaming.NextName(settings, "Leha") == "Leha 1", "Initial Leha name");
        var profiles = new List<BrowserProfile>
        {
            new() { Name = "Space 1" }, new() { Name = " space5 " },
            new() { Name = "Space 12", IsArchived = true }, new() { Name = "Leha 3" },
            new() { Name = "Leha_7" }, new() { Name = "Space 999 custom" }
        };
        Require(ProfileNaming.Observe(settings, profiles), "Startup must discover existing names");
        Require(ProfileNaming.NextName(settings, "Space") == "Space 13", "Archive and largest suffix must count");
        Require(ProfileNaming.NextName(settings, "Leha") == "Leha 8", "Independent Leha numbering");
        Require(!ProfileNaming.Observe(settings, profiles), "Repeated scan must not consume numbers");
        profiles.Clear();
        ProfileNaming.Observe(settings, profiles);
        Require(ProfileNaming.NextName(settings, "Space") == "Space 13", "Deletion must not reset numbering");
        var unnumbered = new AppSettings();
        ProfileNaming.Observe(unnumbered, Enumerable.Range(0, 5).Select(_ => new BrowserProfile { Name = "Space" }));
        Require(ProfileNaming.NextName(unnumbered, "Space") == "Space 6", "Legacy unnumbered profiles count");

        var paths = new AppPaths(Path.Combine(root, "profile-features"));
        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(settings);
        var reloadedSettings = settingsStore.Load();
        Require(ProfileNaming.NextName(reloadedSettings, "Space") == "Space 13", "Counter survives restart");
        Require(ProfileNaming.NextName(reloadedSettings, "Leha") == "Leha 8", "Leha counter survives restart");

        var old = JsonSerializer.Deserialize<BrowserProfile>("{\"Name\":\"Old\",\"Id\":\"keep-id\",\"FutureField\":true}")!;
        Require(old.Comment == "", "Old profiles need no comment migration");
        old.Comment = "Небольшой комментарий\nВторая строка";
        old.LastPublicIp = "203.0.113.42";
        var store = new ProfileStore(paths);
        store.Save(new[] { old });
        var restored = store.Load().Single();
        Require(restored.Comment == old.Comment && restored.Id == "keep-id" && restored.LastPublicIp == old.LastPublicIp,
            "Comments must round-trip without changing profile identity/IP");
        Require(restored.ExtraFields!["FutureField"].GetBoolean(), "Preserve unknown profile fields");

        var now = DateTime.UtcNow;
        var both = new BrowserProfile { Name = "Z", Capability = "Both", CreatedAtUtc = now.AddDays(-1) };
        var restaurant = new BrowserProfile { Name = "A", Capability = "Restaurant", CreatedAtUtc = now };
        var shop = new BrowserProfile { Name = "B", Capability = "Store", CreatedAtUtc = now.AddDays(-2) };
        var archive = new BrowserProfile { Capability = "Both", IsArchived = true, CreatedAtUtc = now.AddDays(1) };
        profiles.AddRange(new[] { shop, both, restaurant, archive });
        Require(ProfileFolders.Select(profiles, "All").SequenceEqual(new[] { restaurant, both, shop }), "Newest first, no archive");
        Require(ProfileFolders.Select(profiles, "Both").SequenceEqual(new[] { both }), "Both folder is exact");
        Require(ProfileFolders.Select(profiles, "Restaurant").SequenceEqual(new[] { restaurant, both }), "Restaurant includes both");
        Require(ProfileFolders.Select(profiles, "Store").SequenceEqual(new[] { both, shop }), "Store includes both");
        Require(ProfileFolders.Select(profiles, "Archive").SequenceEqual(new[] { archive }), "Separate archive");
        var sameTime = new BrowserProfile { CreatedAtUtc = now };
        profiles.Add(sameTime);
        Require(ProfileFolders.Select(profiles, "All").First() == sameTime, "Newest insertion wins timestamp ties");
        Console.WriteLine("PASS: standard names, legacy numbering, persistent counters, comments, folders, newest-first sorting");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
