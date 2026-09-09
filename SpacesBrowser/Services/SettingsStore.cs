using System.Text.Json;
using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public AppSettings Load()
    {
        _paths.EnsureCreated();
        if (!File.Exists(_paths.SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_paths.SettingsFile), JsonOptions)
                   ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Файл настроек повреждён. Исходный файл сохранён без изменений.", ex);
        }
    }

    public void Save(AppSettings settings)
    {
        _paths.EnsureCreated();
        var temporary = _paths.SettingsFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _paths.SettingsFile, true);
    }
}
