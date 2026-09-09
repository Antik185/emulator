using System.Diagnostics;
using System.Windows;
using SpacesBrowser.Services;

namespace SpacesBrowser;

public partial class MainWindow
{
    private bool _updating;
    private AppRelease? _availableRelease;

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (_availableRelease is null)
        {
            await CheckForUpdatesAsync(true);
            return;
        }
        if (_braveInstallInProgress || _pendingLaunches > 0)
        {
            MessageBox.Show(this, "Дождитесь завершения установки Brave или проверки IP запуска.", "Обновление");
            return;
        }
        var answer = MessageBox.Show(this,
            $"Установить версию {_availableRelease.Version.ToString(3)} и перезапустить Пространства?\n\nЗакройте окна браузера, открытые через пространства. Настройки и список профилей будут сохранены в резервную копию.",
            "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;

        _updating = true;
        IsEnabled = false;
        UpdateButton.IsEnabled = false;
        try
        {
            var executable = Environment.ProcessPath!;
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    string? path;
                    try { path = process.MainModule?.FileName; }
                    catch { continue; }
                    if (string.Equals(path, executable, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Ещё работают окна пространств или другой экземпляр приложения. Закройте их и повторите обновление.");
                }
            }
            var directory = Path.Combine(_paths.Root, "Updates", Guid.NewGuid().ToString("N"));
            var service = new GitHubUpdateService(GitHubUpdateService.Repository);
            var progress = new Progress<int>(percent => UpdateButton.Content = $"Обновление: {percent}%");
            var downloaded = await service.DownloadAsync(_availableRelease, directory, progress);
            var fileVersion = FileVersionInfo.GetVersionInfo(downloaded).FileVersion;
            if (!Version.TryParse(fileVersion, out var downloadedVersion) || downloadedVersion != _availableRelease.Version)
                throw new InvalidDataException("Версия скачанного EXE не совпадает с выпуском GitHub.");
            // Finish pending IP/profile writes before handing the data directory to the updater.
            IsEnabled = false;
            await _ipRefreshGate.WaitAsync();
            try { UpdateInstaller.StartHelper(downloaded, _availableRelease.Sha256); }
            finally { _ipRefreshGate.Release(); }
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            IsEnabled = true;
            ShowError("Обновление не установлено.", ex);
        }
        finally
        {
            _updating = false;
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = $"Обновить до {_availableRelease.Version.ToString(3)}";
        }
    }

    private async Task CheckForUpdatesAsync(bool showResult)
    {
        if (_updating) return;
        var version = GitHubUpdateService.CurrentVersion.ToString(3);
        UpdateButton.ToolTip = $"Установлена версия {version}";
        if (string.IsNullOrWhiteSpace(GitHubUpdateService.Repository))
        {
            UpdateButton.Content = $"v{version} · обновления не подключены";
            if (showResult) MessageBox.Show(this, "В этой сборке ещё не указан GitHub-репозиторий обновлений.", "Обновления");
            return;
        }
        _updating = true;
        UpdateButton.IsEnabled = false;
        try
        {
            _availableRelease = await new GitHubUpdateService(GitHubUpdateService.Repository)
                .CheckAsync(GitHubUpdateService.CurrentVersion);
            UpdateButton.Content = _availableRelease is null
                ? $"v{version} · проверить обновления"
                : $"Обновить до {_availableRelease.Version.ToString(3)}";
            if (showResult && _availableRelease is null)
                MessageBox.Show(this, "У вас последняя опубликованная версия.", "Обновления");
        }
        catch (Exception ex)
        {
            UpdateButton.Content = $"v{version} · повторить проверку";
            UpdateButton.ToolTip = ex.Message;
            if (showResult) ShowError("Не удалось проверить обновления.", ex);
        }
        finally { _updating = false; UpdateButton.IsEnabled = true; }
    }
}
