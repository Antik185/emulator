using System.Text.Json;
using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;

    public ProfileStore(AppPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyList<BrowserProfile> Load()
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.ProfilesFile))
        {
            return Array.Empty<BrowserProfile>();
        }

        try
        {
            var json = File.ReadAllText(_paths.ProfilesFile);
            return JsonSerializer.Deserialize<List<BrowserProfile>>(json, JsonOptions)
                   ?? new List<BrowserProfile>();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Файл профилей повреждён.", ex);
        }
    }

    public void Save(IEnumerable<BrowserProfile> profiles)
    {
        _paths.EnsureCreated();
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        var temporaryFile = _paths.ProfilesFile + ".tmp";
        File.WriteAllText(temporaryFile, json);
        File.Move(temporaryFile, _paths.ProfilesFile, true);
    }
}
