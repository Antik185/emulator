using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SpacesBrowser.Services;

public sealed record UpdateRequest(int ParentId, long ParentStartTicks, string TargetPath,
    string DownloadPath, string Sha256, string OldSha256);

public static class UpdateInstaller
{
    public const string HelperFlag = "--apply-app-update";
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    public static void StartHelper(string downloadedPath, string expectedHash)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Путь приложения недоступен.");
        if (!Path.GetFileName(executable).Equals("Spaces.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Обновление доступно в переносимой сборке Spaces.exe.");
        // Copy the running, trusted updater, not the downloaded version, to a separate directory.
        var directory = Path.GetDirectoryName(downloadedPath)!;
        var helper = Path.Combine(directory, "Spaces.Update.exe");
        File.Copy(executable, helper, false);
        using var current = Process.GetCurrentProcess();
        var request = new UpdateRequest(current.Id, current.StartTime.ToUniversalTime().Ticks,
            executable, downloadedPath, expectedHash, Hash(executable));
        var requestFile = Path.Combine(directory, "request.json");
        File.WriteAllText(requestFile, JsonSerializer.Serialize(request));
        var startInfo = new ProcessStartInfo(helper)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(HelperFlag);
        startInfo.ArgumentList.Add(requestFile);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить обновлятор.");
    }

    public static async Task WaitForParentAsync(UpdateRequest request)
    {
        try
        {
            using var parent = Process.GetProcessById(request.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTicks) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await parent.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException) { }
    }

    public static string Apply(UpdateRequest request, AppPaths paths)
    {
        var target = Path.GetFullPath(request.TargetPath);
        var source = Path.GetFullPath(request.DownloadPath);
        if (!Path.GetFileName(target).Equals("Spaces.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Неверный путь обновляемого приложения.");
        if (!Hash(source).Equals(request.Sha256, StringComparison.OrdinalIgnoreCase)
            || !Hash(target).Equals(request.OldSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Файл приложения изменился после подготовки обновления.");

        var backup = Path.Combine(paths.Root, "Backups", $"update-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backup);
        foreach (var file in new[] { paths.ProfilesFile, paths.SettingsFile })
            if (File.Exists(file)) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), false);
        // Preserve the previous executable, including when the target is on another drive.
        File.Copy(target, Path.Combine(backup, "Spaces.exe"), false);
        var incoming = target + ".incoming-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, incoming, false);
            if (!Hash(incoming).Equals(request.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ошибка проверки локальной копии обновления.");
            File.Replace(incoming, target, null);
            return backup;
        }
        finally
        {
            if (File.Exists(incoming)) File.Delete(incoming);
        }
    }

    public static void RestoreExecutable(string target, string backup)
    {
        var incoming = target + ".rollback-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(Path.Combine(backup, "Spaces.exe"), incoming, false);
            File.Replace(incoming, target, null);
        }
        finally { if (File.Exists(incoming)) File.Delete(incoming); }
    }
}
