using System.Diagnostics;
using SpacesBrowser.Models;

namespace SpacesBrowser.Services;

public sealed class BrowserLocator
{
    public BrowserInstall? FindPreferred(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return FromPath(customPath!);
        }

        return CandidatePaths()
            .Where(candidate => File.Exists(candidate.ExecutablePath))
            .OrderBy(candidate => candidate.Kind switch
            {
                BrowserKind.Brave => 0,
                BrowserKind.Edge => 1,
                BrowserKind.Chrome => 2,
                _ => 3
            })
            .FirstOrDefault();
    }

    public BrowserInstall? FindBrave()
    {
        return CandidatePaths()
            .Where(candidate => candidate.Kind == BrowserKind.Brave)
            .FirstOrDefault(candidate => File.Exists(candidate.ExecutablePath));
    }

    public BrowserInstall? FromPath(string executablePath)
    {
        if (!File.Exists(executablePath))
        {
            return null;
        }

        var normalized = executablePath.ToLowerInvariant();
        if (normalized.Contains("bravesoftware") || Path.GetFileName(normalized) == "brave.exe")
        {
            return new BrowserInstall(BrowserKind.Brave, "Brave", executablePath);
        }

        if (Path.GetFileName(normalized) == "msedge.exe")
        {
            return new BrowserInstall(BrowserKind.Edge, "Microsoft Edge", executablePath);
        }

        if (Path.GetFileName(normalized) == "chrome.exe")
        {
            return new BrowserInstall(BrowserKind.Chrome, "Google Chrome", executablePath);
        }

        var description = FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
        var displayName = string.IsNullOrWhiteSpace(description)
            ? Path.GetFileNameWithoutExtension(executablePath)
            : description;
        return new BrowserInstall(BrowserKind.Custom, displayName, executablePath);
    }

    private static IEnumerable<BrowserInstall> CandidatePaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return new BrowserInstall(BrowserKind.Brave, "Brave",
            Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
        yield return new BrowserInstall(BrowserKind.Brave, "Brave",
            Path.Combine(programFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
        yield return new BrowserInstall(BrowserKind.Brave, "Brave",
            Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));

        yield return new BrowserInstall(BrowserKind.Edge, "Microsoft Edge",
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"));
        yield return new BrowserInstall(BrowserKind.Edge, "Microsoft Edge",
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"));
        yield return new BrowserInstall(BrowserKind.Edge, "Microsoft Edge",
            Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe"));

        yield return new BrowserInstall(BrowserKind.Chrome, "Google Chrome",
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"));
        yield return new BrowserInstall(BrowserKind.Chrome, "Google Chrome",
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"));
        yield return new BrowserInstall(BrowserKind.Chrome, "Google Chrome",
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"));
    }
}
