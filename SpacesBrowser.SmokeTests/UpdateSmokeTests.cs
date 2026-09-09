using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using SpacesBrowser.Services;

static class UpdateSmokeTests
{
    public static async Task RunAsync(string root)
    {
        var oldBytes = new byte[] { 77, 90, 1, 2, 3 };
        var newBytes = new byte[] { 77, 90, 4, 5, 6 };
        var expectedHash = Convert.ToHexString(SHA256.HashData(newBytes));
        var json = JsonSerializer.Serialize(new
        {
            tag_name = "v1.2.0", draft = false, prerelease = false,
            assets = new[] { new { name = "Spaces.exe", size = newBytes.Length, digest = "sha256:" + expectedHash,
                browser_download_url = "https://github.com/Antik185/emulator/releases/download/v1.2.0/Spaces.exe" } }
        });
        var release = GitHubUpdateService.ParseRelease(json, "Antik185/emulator", new Version(1, 1, 0, 0))!;
        Require(release.Version == new Version(1, 2, 0, 0), "New version was not detected");
        Require(GitHubUpdateService.ParseRelease(json, "Antik185/emulator", new Version(1, 2, 0, 0)) is null,
            "Same version must not reinstall");
        Require(GitHubUpdateService.ParseRelease(json, "Antik185/emulator", new Version(1, 3, 0, 0)) is null,
            "Downgrade must not install");
        Require(GitHubUpdateService.ParseRelease(json.Replace("\"prerelease\":false", "\"prerelease\":true"),
            "Antik185/emulator", new Version(1, 1, 0, 0)) is null, "Prerelease must not install");
        Reject(() => GitHubUpdateService.ParseRelease(json.Replace("github.com/Antik185", "github.com/other"),
            "Antik185/emulator", new Version(1, 1, 0, 0)), "Wrong repo accepted");
        Reject(() => GitHubUpdateService.ParseRelease(json.Replace("sha256:", "md5:"),
            "Antik185/emulator", new Version(1, 1, 0, 0)), "Missing digest accepted");

        using var client = new HttpClient(new PayloadHandler(newBytes));
        var downloaded = await new GitHubUpdateService("Antik185/emulator", client)
            .DownloadAsync(release, Path.Combine(root, "download"));
        Require(File.ReadAllBytes(downloaded).SequenceEqual(newBytes), "Download differs");
        using var badClient = new HttpClient(new PayloadHandler(oldBytes));
        try
        {
            await new GitHubUpdateService("Antik185/emulator", badClient)
                .DownloadAsync(release, Path.Combine(root, "corrupt"));
            throw new Exception("Corrupt download accepted");
        }
        catch (InvalidDataException) { }
        Require(!File.Exists(Path.Combine(root, "corrupt", "Spaces.download.exe")), "Corrupt download was retained");

        var target = Path.Combine(root, "Spaces.exe");
        File.WriteAllBytes(target, oldBytes);
        var paths = new AppPaths(Path.Combine(root, "data"));
        paths.EnsureCreated();
        const string profilesJson = "[{\"Name\":\"Existing\",\"ReadyAfterHours\":31,\"FutureField\":\"preserve\"}]";
        const string settingsJson = "{\"CustomBrowserPath\":\"brave.exe\",\"FutureSetting\":true}";
        File.WriteAllText(paths.ProfilesFile, profilesJson);
        File.WriteAllText(paths.SettingsFile, settingsJson);
        var cookieFile = Path.Combine(paths.BrowserDataRoot, "cookie-test");
        File.WriteAllText(cookieFile, "DO NOT TOUCH");
        var request = new UpdateRequest(0, 0, target, downloaded, expectedHash, UpdateInstaller.Hash(target));
        // A changed download must leave the target and data untouched.
        Reject(() => UpdateInstaller.Apply(request with { Sha256 = new string('0', 64) }, paths), "Bad hash accepted");
        Require(File.ReadAllBytes(target).SequenceEqual(oldBytes), "Old EXE changed after validation failure");
        // Windows denies replacing an in-use EXE: preserve old EXE on this failure too.
        using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { UpdateInstaller.Apply(request, paths); throw new Exception("Locked target replaced"); }
            catch (IOException) { }
        }
        Require(File.ReadAllBytes(target).SequenceEqual(oldBytes), "Old EXE changed after locked-file failure");

        var backup = UpdateInstaller.Apply(request, paths);
        Require(File.ReadAllBytes(target).SequenceEqual(newBytes), "EXE was not updated");
        Require(File.ReadAllText(paths.ProfilesFile) == profilesJson && File.ReadAllText(paths.SettingsFile) == settingsJson,
            "Update changed profiles/settings");
        Require(File.ReadAllText(cookieFile) == "DO NOT TOUCH", "Update changed browser data");
        Require(File.ReadAllText(Path.Combine(backup, "profiles.json")) == profilesJson, "Profile backup is wrong");
        Require(File.ReadAllText(Path.Combine(backup, "settings.json")) == settingsJson, "Settings backup is wrong");
        UpdateInstaller.RestoreExecutable(target, backup);
        Require(File.ReadAllBytes(target).SequenceEqual(oldBytes), "Rollback did not restore old EXE");

        var store = new ProfileStore(paths);
        store.Save(store.Load());
        Require(File.ReadAllText(paths.ProfilesFile).Contains("FutureField"), "Unknown profile fields discarded");
        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(settingsStore.Load());
        Require(File.ReadAllText(paths.SettingsFile).Contains("FutureSetting"), "Unknown settings discarded");
        File.WriteAllText(paths.SettingsFile, "broken-json");
        Reject(() => settingsStore.Load(), "Broken settings silently reset");
        Require(File.ReadAllText(paths.SettingsFile) == "broken-json", "Broken settings overwritten");
        Console.WriteLine("PASS: updates (version, source, digest, download, locked EXE, backups, replacement, rollback, data preservation)");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception(message);
    }

    private sealed class PayloadHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        public PayloadHandler(byte[] bytes) => _bytes = bytes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) });
    }
}
