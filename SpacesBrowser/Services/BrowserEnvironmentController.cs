using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SpacesBrowser.Services;

internal static class BrowserEnvironmentController
{
    public static async Task RunAsync(
        int debuggingPort,
        string timeZoneId,
        string homeUrl,
        CancellationToken cancellationToken = default)
    {
        var supportedTimeZone = RegionCatalog.All.Any(option => option.TimeZoneId == timeZoneId)
            ? timeZoneId
            : RegionCatalog.Default.TimeZoneId;
        var safeHomeUrl = BrowserLauncher.NormalizeUrl(homeUrl);
        var endpoint = await WaitForBrowserEndpointAsync(debuggingPort, cancellationToken);
        if (endpoint is null)
        {
            return;
        }

        await using var connection = new CdpConnection();
        await connection.ConnectAsync(endpoint, cancellationToken);

        connection.EventReceived = async (method, parameters, _) =>
        {
            if (method != "Target.attachedToTarget")
            {
                return;
            }

            var sessionId = parameters.GetProperty("sessionId").GetString();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var targetInfo = parameters.GetProperty("targetInfo");
            var targetType = targetInfo.GetProperty("type").GetString();
            if (targetType == "page")
            {
                await ApplyTimeZoneAsync(connection, sessionId, supportedTimeZone, cancellationToken);
            }

            if (parameters.TryGetProperty("waitingForDebugger", out var waiting)
                && waiting.ValueKind == JsonValueKind.True)
            {
                await connection.SendCommandAsync(
                    "Runtime.runIfWaitingForDebugger", sessionId: sessionId, cancellationToken: cancellationToken);
            }
        };

        var targetsResult = await connection.SendCommandAsync(
            "Target.getTargets", cancellationToken: cancellationToken);
        var initialTarget = targetsResult.GetProperty("targetInfos")
            .EnumerateArray()
            .FirstOrDefault(target => target.GetProperty("type").GetString() == "page");

        string? initialSession = null;
        if (initialTarget.ValueKind != JsonValueKind.Undefined)
        {
            var targetId = initialTarget.GetProperty("targetId").GetString();
            if (!string.IsNullOrWhiteSpace(targetId))
            {
                var attachResult = await connection.SendCommandAsync(
                    "Target.attachToTarget",
                    new { targetId, flatten = true },
                    cancellationToken: cancellationToken);
                initialSession = attachResult.GetProperty("sessionId").GetString();
                if (!string.IsNullOrWhiteSpace(initialSession))
                {
                    await ApplyTimeZoneAsync(connection, initialSession, supportedTimeZone, cancellationToken);
                }
            }
        }

        await connection.SendCommandAsync(
            "Target.setAutoAttach",
            new { autoAttach = true, waitForDebuggerOnStart = true, flatten = true },
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(initialSession))
        {
            await connection.SendCommandAsync(
                "Page.navigate", new { url = safeHomeUrl }, initialSession, cancellationToken);
        }

        await connection.Completion.WaitAsync(cancellationToken);
    }

    private static async Task ApplyTimeZoneAsync(
        CdpConnection connection,
        string sessionId,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        await connection.SendCommandAsync(
            "Emulation.setTimezoneOverride",
            new { timezoneId = timeZoneId },
            sessionId,
            cancellationToken);
    }

    private static async Task<Uri?> WaitForBrowserEndpointAsync(
        int debuggingPort,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
        var endpointUrl = $"http://127.0.0.1:{debuggingPort}/json/version";

        for (var attempt = 0; attempt < 80 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                var json = await client.GetStringAsync(endpointUrl, cancellationToken);
                using var document = JsonDocument.Parse(json);
                var socketUrl = document.RootElement.GetProperty("webSocketDebuggerUrl").GetString();
                if (Uri.TryCreate(socketUrl, UriKind.Absolute, out var socketUri)
                    && socketUri.Scheme == "ws"
                    && (socketUri.Host == "127.0.0.1" || socketUri.Host == "localhost"
                        || IPAddress.TryParse(socketUri.Host, out var address) && IPAddress.IsLoopback(address)))
                {
                    return socketUri;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch
            {
            }

            await Task.Delay(250, cancellationToken);
        }

        return null;
    }
}
