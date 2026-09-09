namespace SpacesBrowser.Services;

public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpacesBrowser");
    }

    public string Root { get; }
    public string ProfilesFile => Path.Combine(Root, "profiles.json");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string BrowserDataRoot => Path.Combine(Root, "BrowserData");
    public string ProfileData(string profileId) => Path.Combine(BrowserDataRoot, profileId);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BrowserDataRoot);
    }
}
