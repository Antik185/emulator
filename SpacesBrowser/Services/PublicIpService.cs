using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpacesBrowser.Services;

public sealed class PublicIpService
{
    private static readonly Uri Endpoint = new("https://api.ipify.org?format=json");
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient _httpClient;

    public PublicIpService()
        : this(SharedClient)
    {
    }

    public PublicIpService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> GetPublicIpAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            Endpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<IpifyResponse>(
            stream,
            cancellationToken: cancellationToken);

        if (result?.Ip is null
            || !IPAddress.TryParse(result.Ip, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException("Сервис вернул некорректный IPv4-адрес.");
        }

        return address.ToString();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SpacesBrowser/1.0");
        return client;
    }

    private sealed class IpifyResponse
    {
        [JsonPropertyName("ip")]
        public string? Ip { get; set; }
    }
}
