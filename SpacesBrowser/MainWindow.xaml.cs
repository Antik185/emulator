using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using SpacesBrowser.Dialogs;
using SpacesBrowser.Models;
using SpacesBrowser.Services;

namespace SpacesBrowser;

public partial class MainWindow : Window
{
    private readonly AppPaths _paths = new();
    private readonly BrowserLocator _browserLocator = new();
    private readonly ProfileStore _profileStore;
    private readonly SettingsStore _settingsStore;
    private readonly BrowserLauncher _browserLauncher;
    private readonly PublicIpService _publicIpService = new();
    private readonly BraveInstallerService _braveInstallerService = new();
    private readonly DispatcherTimer _readinessTimer;

    private List<BrowserProfile> _profiles = new();
    private AppSettings _settings = new();
    private BrowserInstall? _browser;
    private string? _lastPublicIp;
    private readonly SemaphoreSlim _ipRefreshGate = new(1, 1);
    private bool _braveInstallInProgress;
    private int _pendingLaunches;

    public MainWindow()
    {
        InitializeComponent();
        _profileStore = new ProfileStore(_paths);
        _settingsStore = new SettingsStore(_paths);
        _browserLauncher = new BrowserLauncher(_paths);
        _readinessTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _readinessTimer.Tick += (_, _) => RefreshCards();
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _readinessTimer.Stop();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = _settingsStore.Load();
            _profiles = _profileStore.Load().ToList();
            var migratedRegion = false;
            foreach (var profile in _profiles)
            {
                var oldRegion = profile.Region;
                var oldTimeZone = profile.TimeZoneId;
                RegionCatalog.Normalize(profile);
                migratedRegion |= oldRegion != profile.Region || oldTimeZone != profile.TimeZoneId;
            }
            if (migratedRegion)
            {
                _profileStore.Save(_profiles);
            }
            RefreshBrowser();
            RefreshCards();
            _readinessTimer.Start();
        }
        catch (Exception ex)
        {
            ShowError("Не удалось загрузить данные приложения.", ex);
            Close();
            return;
        }

        _ = CheckForUpdatesAsync(false);
        await RefreshPublicIpAsync();
    }

    private async void RefreshIp_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPublicIpAsync();
    }

    private async Task<string?> RefreshPublicIpAsync()
    {
        await _ipRefreshGate.WaitAsync();
        RefreshIpButton.IsEnabled = false;
        PublicIpChangeText.Foreground = Brush("#9DA7BA");
        PublicIpChangeText.Text = "Обновление…";
        PublicIpChangeText.ToolTip = null;

        try
        {
            var currentIp = await _publicIpService.GetPublicIpAsync();
            var previousIp = _lastPublicIp;
            _lastPublicIp = currentIp;
            PublicIpText.Text = currentIp;

            var checkedAt = DateTime.Now.ToString("HH:mm");
            if (previousIp is null)
            {
                PublicIpChangeText.Text = $"Проверено {checkedAt} · первый замер";
            }
            else if (string.Equals(previousIp, currentIp, StringComparison.Ordinal))
            {
                PublicIpChangeText.Text = $"Проверено {checkedAt} · не изменился";
                PublicIpChangeText.Foreground = Brush("#58E0B5");
            }
            else
            {
                PublicIpChangeText.Text = $"Изменился · был {previousIp}";
                PublicIpChangeText.Foreground = Brush("#58E0B5");
                PublicIpChangeText.ToolTip = $"Проверено {checkedAt}. Предыдущий IP: {previousIp}";
            }

            return currentIp;
        }
        catch (Exception ex)
        {
            PublicIpText.Text = _lastPublicIp ?? "Недоступен";
            PublicIpChangeText.Text = _lastPublicIp is null
                ? "Не удалось проверить · повторите"
                : "Ошибка проверки · показан последний";
            PublicIpChangeText.Foreground = Brush("#FF647C");
            PublicIpChangeText.ToolTip = ex.Message;
            return null;
        }
        finally
        {
            RefreshIpButton.IsEnabled = true;
            _ipRefreshGate.Release();
        }
    }

    private void RefreshBrowser()
    {
        _browser = _browserLocator.FindPreferred(_settings.CustomBrowserPath);

        if (_browser is null)
        {
            BrowserStatusDot.Fill = Brush("#FF647C");
            BrowserStatusTitle.Text = "Браузер не найден";
            BrowserStatusDetails.Text = "Установите Brave либо выберите Chromium-браузер вручную.";
            DownloadBraveButton.Visibility = Visibility.Visible;
            return;
        }

        if (_browser.HasBuiltInFingerprintProtection)
        {
            BrowserStatusDot.Fill = Brush("#21C7A8");
            BrowserStatusTitle.Text = $"{_browser.DisplayName} · privacy-режим";
            BrowserStatusDetails.Text = "Каждое пространство запускается с отдельным каталогом данных.";
            DownloadBraveButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            BrowserStatusDot.Fill = Brush("#FFB44C");
            BrowserStatusTitle.Text = $"{_browser.DisplayName} · резервный режим";
            BrowserStatusDetails.Text = "Профили изолированы, но усиленная защита Brave недоступна.";
            DownloadBraveButton.Visibility = Visibility.Visible;
        }
    }

    private void RefreshCards()
    {
        ActiveProfilesPanel.Children.Clear();
        ArchivedProfilesPanel.Children.Clear();

        var capabilityFilter = (CapabilityFilterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
        var filtered = _profiles
            .Where(profile => MatchesCapability(profile, capabilityFilter))
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var profile in filtered.Where(profile => !profile.IsArchived))
        {
            ActiveProfilesPanel.Children.Add(CreateProfileCard(profile));
        }

        var showArchived = ShowArchivedBox.IsChecked == true;
        ArchivedHeading.Visibility = showArchived ? Visibility.Visible : Visibility.Collapsed;
        ArchivedProfilesPanel.Visibility = showArchived ? Visibility.Visible : Visibility.Collapsed;
        if (showArchived)
        {
            foreach (var profile in filtered.Where(profile => profile.IsArchived))
            {
                ArchivedProfilesPanel.Children.Add(CreateProfileCard(profile));
            }
        }

        var visibleCount = filtered.Count(profile => !profile.IsArchived)
                           + (showArchived ? filtered.Count(profile => profile.IsArchived) : 0);
        EmptyState.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActiveHeading.Visibility = filtered.Any(profile => !profile.IsArchived)
            ? Visibility.Visible
            : Visibility.Collapsed;
        FooterStatus.Text = $"{_profiles.Count(profile => !profile.IsArchived)} активных · {_profiles.Count(profile => profile.IsArchived)} в архиве";
    }

    private Border CreateProfileCard(BrowserProfile profile)
    {
        var accent = Brush(profile.Color);
        var readiness = ProfileReadiness.Calculate(profile);
        var readyBorder = profile.Capability switch
        {
            "Restaurant" => "#6EAEFF",
            "Store" => "#FFBC57",
            _ => "#2DD4A3"
        };
        var card = new Border
        {
            Width = 340,
            Height = 305,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(19),
            CornerRadius = new CornerRadius(16),
            Background = (System.Windows.Media.Brush)FindResource("Surface"),
            BorderBrush = readiness.IsReady ? Brush(readyBorder) : Brushes.Transparent,
            BorderThickness = readiness.IsReady ? new Thickness(1.5) : new Thickness(0),
            Cursor = Cursors.Arrow
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(14),
            Background = accent,
            Child = new TextBlock
            {
                Text = Initials(profile.Name),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            }
        };
        heading.Children.Add(avatar);

        var titleStack = new StackPanel { Margin = new Thickness(13, 2, 8, 0) };
        Grid.SetColumn(titleStack, 1);
        titleStack.Children.Add(new TextBlock
        {
            Text = profile.Name,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = profile.IsArchived ? "В архиве" : "Отдельное хранилище",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
            FontSize = 12
        });
        heading.Children.Add(titleStack);

        var menuButton = new Button
        {
            Content = "•••",
            Style = (Style)FindResource("RoundedButton"),
            Padding = new Thickness(9, 5, 9, 7),
            FontSize = 15,
            Tag = profile
        };
        Grid.SetColumn(menuButton, 2);
        menuButton.Click += ProfileMenu_Click;
        menuButton.ContextMenu = BuildProfileMenu(profile);
        heading.Children.Add(menuButton);
        root.Children.Add(heading);

        var details = new StackPanel { Margin = new Thickness(1, 14, 0, 0) };
        Grid.SetRow(details, 1);

        var badges = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var capability = profile.Capability switch
        {
            "Restaurant" => ("РЕСТОРАН", "#192E4A", "#6EAEFF"),
            "Store" => ("МАГАЗИН", "#3A2B17", "#FFBC57"),
            _ => ("РЕСТ + МАГАЗИН", "#15392F", "#58E0B5")
        };
        badges.Children.Add(CreateBadge(capability.Item1, capability.Item2, capability.Item3));
        badges.Children.Add(CreateBadge(
            RegionCatalog.Find(profile.Region, profile.TimeZoneId).DisplayName,
            "#202632", "#AAB4C6"));
        badges.Children.Add(readiness.IsReady
            ? CreateBadge("READY", "#15392F", "#58E0B5")
            : CreateBadge(ProfileReadiness.CountdownLabel(readiness), "#3A311B", "#FFD071"));
        details.Children.Add(badges);

        details.Children.Add(new TextBlock
        {
            Text = $"{new Uri(profile.HomeUrl).Host} · {EnvironmentLabel(profile)}",
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        details.Children.Add(new TextBlock
        {
            Text = $"Создан: {profile.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = Brush("#7F8BA0"),
            FontSize = 12
        });
        details.Children.Add(new TextBlock
        {
            Text = profile.LastOpenedAtUtc is null
                ? "Ещё не запускалось"
                : $"Последний запуск: {profile.LastOpenedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = Brush("#6F7B90"),
            FontSize = 12
        });
        details.Children.Add(new TextBlock
        {
            Text = profile.LastPublicIp is null
                ? "Последний IP запуска: нет данных"
                : $"Последний IP запуска: {profile.LastPublicIp}",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = Brush("#7F8BA0"),
            FontSize = 12
        });
        root.Children.Add(details);

        var openButton = new Button
        {
            Content = profile.IsArchived ? "Вернуть из архива" : "Открыть пространство",
            Style = (Style)FindResource(profile.IsArchived ? "RoundedButton" : "PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = profile
        };
        Grid.SetRow(openButton, 2);
        if (profile.IsArchived)
        {
            openButton.Click += RestoreProfile_Click;
        }
        else
        {
            openButton.Click += OpenProfile_Click;
        }
        root.Children.Add(openButton);

        card.Child = root;
        return card;
    }

    private static Border CreateBadge(string text, string background, string foreground)
    {
        return new Border
        {
            Margin = new Thickness(0, 0, 7, 6),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(7),
            Background = Brush(background),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brush(foreground),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            }
        };
    }

    private ContextMenu BuildProfileMenu(BrowserProfile profile)
    {
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "Изменить", Tag = profile };
        edit.Click += EditProfile_Click;
        menu.Items.Add(edit);

        var archive = new MenuItem
        {
            Header = profile.IsArchived ? "Вернуть из архива" : "В архив",
            Tag = profile
        };
        archive.Click += ToggleArchive_Click;
        menu.Items.Add(archive);

        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "Удалить данные…", Tag = profile };
        delete.Click += DeleteProfile_Click;
        menu.Items.Add(delete);
        return menu;
    }

    private static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Take(2).Select(word => char.ToUpper(word[0])));
    }

    private static bool MatchesCapability(BrowserProfile profile, string filter)
    {
        return filter switch
        {
            "Restaurant" => profile.Capability is "Restaurant" or "Both",
            "Store" => profile.Capability is "Store" or "Both",
            "Both" => profile.Capability == "Both",
            _ => true
        };
    }

    private static string EnvironmentLabel(BrowserProfile profile)
    {
        var window = profile.WindowPreset switch
        {
            "Laptop" => "1366×768",
            "FullHd" => "1920×1080",
            "Tablet" => "1280×800",
            "Compact" => "1024×768",
            _ => "развёрнуто"
        };
        return $"{BrowserLauncher.NormalizeLanguage(profile.BrowserLanguage)} · {window} · {profile.TimeZoneId}";
    }

    private void CreateProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _profiles.Add(dialog.Result);
            SaveAndRefresh("Пространство создано.");
        }
    }

    private async void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BrowserProfile profile })
        {
            return;
        }

        if (_browser is null)
        {
            MessageBox.Show(this, "Сначала установите или выберите Chromium-браузер.", "Браузер не найден",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _pendingLaunches++;
        try
        {
            _browserLauncher.Launch(_browser, profile);
            profile.LastOpenedAtUtc = DateTime.UtcNow;
            SaveAndRefresh($"Открыто: {profile.Name}");

            var launchIp = await RefreshPublicIpAsync();
            if (launchIp is not null)
            {
                profile.LastPublicIp = launchIp;
                SaveAndRefresh($"Открыто: {profile.Name} · IP {launchIp}");
            }
        }
        catch (Exception ex)
        {
            ShowError("Не удалось запустить браузер.", ex);
        }
        finally { _pendingLaunches--; }
    }

    private void ProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BrowserProfile profile })
        {
            return;
        }

        var dialog = new ProfileDialog(profile) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SaveAndRefresh("Изменения сохранены.");
        }
    }

    private void ToggleArchive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BrowserProfile profile })
        {
            profile.IsArchived = !profile.IsArchived;
            SaveAndRefresh(profile.IsArchived ? "Перемещено в архив." : "Возвращено из архива.");
        }
    }

    private void RestoreProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BrowserProfile profile })
        {
            profile.IsArchived = false;
            SaveAndRefresh("Возвращено из архива.");
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BrowserProfile profile })
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"Удалить «{profile.Name}»?\n\nКаталог браузерных данных будет перемещён в Корзину.",
            "Удаление пространства", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var dataDirectory = _paths.ProfileData(profile.Id);
            if (Directory.Exists(dataDirectory))
            {
                FileSystem.DeleteDirectory(dataDirectory, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }

            _profiles.Remove(profile);
            SaveAndRefresh("Пространство удалено; данные перемещены в Корзину.");
        }
        catch (Exception ex)
        {
            ShowError("Не удалось удалить профиль. Возможно, его браузер ещё запущен.", ex);
        }
    }

    private void ChooseBrowser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите Chromium-браузер",
            Filter = "Приложения (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var selected = _browserLocator.FromPath(dialog.FileName);
        if (selected is null)
        {
            MessageBox.Show(this, "Выбранный файл недоступен.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _settings.CustomBrowserPath = dialog.FileName;
        _settingsStore.Save(_settings);
        RefreshBrowser();
        FooterStatus.Text = $"Выбран браузер: {selected.DisplayName}";
    }

    private async void DownloadBrave_Click(object sender, RoutedEventArgs e)
    {
        if (_braveInstallInProgress)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            "Приложение скачает официальный установщик Brave, проверит его цифровую подпись и установит браузер. Продолжить?",
            "Установка Brave", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        string? installerPath = null;
        _braveInstallInProgress = true;
        DownloadBraveButton.IsEnabled = false;

        try
        {
            BrowserStatusDot.Fill = Brush("#FFB44C");
            BrowserStatusTitle.Text = "Скачивание Brave…";
            BrowserStatusDetails.Text = "Получаем официальный установщик и проверяем подпись.";
            DownloadBraveButton.Content = "Скачивание…";

            var progress = new Progress<int>(percent =>
            {
                DownloadBraveButton.Content = $"Скачивание {percent}%";
                BrowserStatusDetails.Text = $"Загружено {percent}% · после загрузки установка продолжится автоматически.";
            });
            installerPath = await _braveInstallerService.DownloadOfficialInstallerAsync(progress);

            BrowserStatusTitle.Text = "Установка Brave…";
            BrowserStatusDetails.Text = "Установщик проверен. Ожидаем завершения установки.";
            DownloadBraveButton.Content = "Установка…";
            var exitCode = await BraveInstallerService.RunInstallerAsync(installerPath);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Установщик Brave завершился с кодом {exitCode}.");
            }

            var brave = await WaitForBraveAsync();
            if (brave is null)
            {
                throw new InvalidOperationException("Установка завершилась, но Brave пока не найден. Перезапустите приложение или выберите brave.exe вручную.");
            }

            _settings.CustomBrowserPath = brave.ExecutablePath;
            _settingsStore.Save(_settings);
            RefreshBrowser();
            RefreshCards();
            FooterStatus.Text = "Brave установлен и выбран автоматически.";
        }
        catch (Exception ex)
        {
            RefreshBrowser();
            ShowError("Не удалось автоматически установить Brave. Можно повторить или выбрать браузер вручную.", ex);
        }
        finally
        {
            BraveInstallerService.Cleanup(installerPath);
            _braveInstallInProgress = false;
            DownloadBraveButton.IsEnabled = true;
            DownloadBraveButton.Content = "Установить Brave автоматически";
        }
    }

    private async Task<BrowserInstall?> WaitForBraveAsync()
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            var brave = _browserLocator.FindBrave();
            if (brave is not null)
            {
                return brave;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return null;
    }

    private void ShowArchived_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshCards();
        }
    }

    private void CapabilityFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RefreshCards();
        }
    }

    private void SaveAndRefresh(string status)
    {
        _profileStore.Save(_profiles);
        RefreshCards();
        FooterStatus.Text = status;
    }

    private void ShowError(string message, Exception ex)
    {
        MessageBox.Show(this, $"{message}\n\n{ex.Message}", "Пространства",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static SolidColorBrush Brush(string color)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
