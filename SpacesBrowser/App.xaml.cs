using System.Windows;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using SpacesBrowser.Services;

namespace SpacesBrowser;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstance;

    private bool AcquireInstance()
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new AppPaths().Root.ToUpperInvariant())));
        _instanceMutex = new Mutex(false, "Local\\SpacesBrowser-" + id);
        try { return _ownsInstance = _instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { return _ownsInstance = true; }
    }

    private void ReleaseInstance()
    {
        if (_ownsInstance) _instanceMutex!.ReleaseMutex();
        _ownsInstance = false;
        _instanceMutex?.Dispose();
        _instanceMutex = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseInstance();
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == UpdateInstaller.HelperFlag)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = ApplyUpdateAsync(e.Args[1]);
            return;
        }
        if (BrowserEnvironmentHelper.TryParse(e.Args, out var helperOptions)
            && helperOptions is not null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunEnvironmentHelperAsync(helperOptions);
            return;
        }

        if (!AcquireInstance())
        {
            MessageBox.Show("Пространства уже открыты или обновляются. Закройте другой экземпляр приложения.", "Пространства");
            Shutdown();
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private async Task ApplyUpdateAsync(string requestFile)
    {
        string? backup = null;
        UpdateRequest? request = null;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(requestFile))!;
            var updates = Path.Combine(new AppPaths().Root, "Updates") + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(updates, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(Environment.ProcessPath), directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Неверный каталог обновления.");
            request = JsonSerializer.Deserialize<UpdateRequest>(File.ReadAllText(requestFile))
                ?? throw new InvalidDataException("Неверные данные обновления.");
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(request.DownloadPath)), directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Неверный путь скачанного обновления.");
            await UpdateInstaller.WaitForParentAsync(request);
            if (!AcquireInstance()) throw new InvalidOperationException("Закройте другой экземпляр Пространств и повторите обновление.");
            backup = UpdateInstaller.Apply(request, new AppPaths());
            ReleaseInstance();
            using var launched = Process.Start(new ProcessStartInfo(request.TargetPath) { UseShellExecute = true })
                ?? throw new InvalidOperationException("Обновление установлено, но приложение не запустилось.");
        }
        catch (Exception ex)
        {
            if (backup is not null && request is not null)
            {
                try { UpdateInstaller.RestoreExecutable(request.TargetPath, backup); }
                catch { }
            }
            MessageBox.Show("Не удалось завершить обновление. Настройки и профили сохранены.\n\n" + ex.Message
                + (backup is null ? "" : "\nКопия предыдущей версии: " + backup), "Пространства", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Shutdown(); }
    }

    private async Task RunEnvironmentHelperAsync(BrowserEnvironmentHelperOptions options)
    {
        try
        {
            await BrowserEnvironmentController.RunAsync(
                options.DebuggingPort,
                options.TimeZoneId,
                options.HomeUrl);
        }
        catch
        {
        }
        finally
        {
            Dispatcher.Invoke(Shutdown);
        }
    }
}
