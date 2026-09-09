using System.Diagnostics;
using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

internal sealed record BrowserEnvironmentHelperOptions(
    int DebuggingPort,
    string TimeZoneId,
    string HomeUrl);

internal static class BrowserEnvironmentHelper
{
    private const string HelperFlag = "--browser-environment-helper";

    public static bool TryStart(int debuggingPort, BrowserProfile profile)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add(HelperFlag);
            startInfo.ArgumentList.Add(debuggingPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(profile.TimeZoneId);
            startInfo.ArgumentList.Add(BrowserLauncher.NormalizeUrl(profile.HomeUrl));
            return Process.Start(startInfo) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParse(string[] arguments, out BrowserEnvironmentHelperOptions? options)
    {
        options = null;
        if (arguments.Length != 4 || arguments[0] != HelperFlag
            || !int.TryParse(arguments[1], out var port) || port is < 1 or > 65535)
        {
            return false;
        }

        var region = RegionCatalog.All.FirstOrDefault(item => item.TimeZoneId == arguments[2]);
        if (region is null)
        {
            return false;
        }

        options = new BrowserEnvironmentHelperOptions(
            port,
            region.TimeZoneId,
            BrowserLauncher.NormalizeUrl(arguments[3]));
        return true;
    }
}
