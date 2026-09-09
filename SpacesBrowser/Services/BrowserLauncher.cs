using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public sealed class BrowserLauncher
{
    private readonly AppPaths _paths;

    public BrowserLauncher(AppPaths paths)
    {
        _paths = paths;
    }

    public Process Launch(BrowserInstall browser, BrowserProfile profile)
    {
        var dataDirectory = _paths.ProfileData(profile.Id);
        Directory.CreateDirectory(dataDirectory);
        RegionCatalog.Normalize(profile);

        var debuggingPort = FindFreeLoopbackPort();
        var helperStarted = BrowserEnvironmentHelper.TryStart(debuggingPort, profile);

        var startInfo = new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            UseShellExecute = true,
            Arguments = BuildArguments(
                dataDirectory,
                profile,
                helperStarted ? debuggingPort : null,
                helperStarted)
        };

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Браузер не удалось запустить.");
    }

    public static string BuildArguments(string dataDirectory, string homeUrl)
    {
        return BuildArguments(dataDirectory, new BrowserProfile { HomeUrl = homeUrl });
    }

    public static string BuildArguments(
        string dataDirectory,
        BrowserProfile profile,
        int? debuggingPort = null,
        bool startWithBlankPage = false)
    {
        var safeDirectory = dataDirectory.Replace("\"", "\\\"");
        var safeUrl = startWithBlankPage
            ? "about:blank"
            : NormalizeUrl(profile.HomeUrl).Replace("\"", "%22");
        var language = NormalizeLanguage(profile.BrowserLanguage);
        var windowArgument = profile.WindowPreset switch
        {
            "Laptop" => "--window-size=1366,768",
            "FullHd" => "--window-size=1920,1080",
            "Tablet" => "--window-size=1280,800",
            "Compact" => "--window-size=1024,768",
            _ => "--start-maximized"
        };

        var debuggingArguments = debuggingPort is > 0 and <= 65535
            ? $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={debuggingPort.Value} "
            : string.Empty;

        return $"--user-data-dir=\"{safeDirectory}\" --no-first-run --no-default-browser-check "
               + debuggingArguments
               + $"--lang={language} {windowArgument} --new-window \"{safeUrl}\"";
    }

    public static string NormalizeLanguage(string? value)
    {
        return value switch
        {
            "en-US" => "en-US",
            "de-DE" => "de-DE",
            "fr-FR" => "fr-FR",
            "es-ES" => "es-ES",
            "pl-PL" => "pl-PL",
            _ => "ru-RU"
        };
    }

    public static string NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "https://ya.ru/";
        }

        var trimmed = value.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : "https://ya.ru/";
    }

    private static int FindFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
