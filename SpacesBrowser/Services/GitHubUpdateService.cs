using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpacesBrowser.Services;

public sealed record AppRelease(Version Version, Uri DownloadUri, long Size, string Sha256);

public sealed class GitHubUpdateService
{
    private static readonly HttpClient Client = CreateClient();
    private readonly HttpClient _client;
    private readonly string _repository;
    public const long MaximumSize = 300L * 1024 * 1024;

    public static Version CurrentVersion => typeof(GitHubUpdateService).Assembly.GetName().Version!;
    public static string Repository => typeof(GitHubUpdateService).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? "";

    public GitHubUpdateService(string repository, HttpClient? client = null)
    {
        if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+$"))
            throw new ArgumentException("Источник обновлений ещё не настроен.");
        _repository = repository;
        _client = client ?? Client;
    }

    public async Task<AppRelease?> CheckAsync(Version installedVersion, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _client.GetAsync(
            $"https://api.github.com/repos/{_repository}/releases/latest", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Репозиторий недоступен или в нём ещё нет опубликованных выпусков.");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        return ParseRelease(json, _repository, installedVersion);
    }

    public static AppRelease? ParseRelease(string json, string repository, Version installedVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version))
            throw new InvalidDataException("Неверный номер версии выпуска GitHub.");
        version = new Version(version.Major, version.Minor, version.Build, 0);
        if (version <= installedVersion) return null;

        var assets = root.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == "Spaces.exe").ToArray();
        if (assets.Length != 1) throw new InvalidDataException("В выпуске отсутствует Spaces.exe для Windows x64.");
        var asset = assets[0];
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.AbsolutePath != $"/{repository}/releases/download/{tag}/Spaces.exe")
            throw new InvalidDataException("Адрес файла не соответствует репозиторию обновлений.");
        var size = asset.GetProperty("size").GetInt64();
        var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
        if (size is <= 0 or > MaximumSize || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("GitHub не предоставил корректный размер или SHA-256 выпуска.");
        return new AppRelease(version, uri, size, digest[7..]);
    }

    public async Task<string> DownloadAsync(AppRelease release, string directory,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Spaces.download.exe");
        try
        {
            using var response = await _client.GetAsync(release.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    received += count;
                    if (received > release.Size || received > MaximumSize)
                        throw new InvalidDataException("Размер скачанного обновления не совпадает с выпуском.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    progress?.Report((int)(received * 100 / release.Size));
                }
            }
            if (received != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Обновление повреждено: проверка SHA-256 не пройдена.");
            return path;
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Spaces-Updater/1.1");
        return client;
    }
}
