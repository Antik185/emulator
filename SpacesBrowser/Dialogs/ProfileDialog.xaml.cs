using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SpacesBrowser.Models;
using SpacesBrowser.Services;

namespace SpacesBrowser.Dialogs;

public partial class ProfileDialog : Window
{
    private sealed record Option(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly string[] Colors =
    {
        "#7C6CFF", "#5B9CFF", "#21C7A8", "#FFB44C", "#FF6B87", "#B875F3"
    };

    private static readonly Option[] Languages =
    {
        new("ru-RU", "Русский"),
        new("en-US", "English (US)"),
        new("de-DE", "Deutsch"),
        new("fr-FR", "Français"),
        new("es-ES", "Español"),
        new("pl-PL", "Polski")
    };

    private static readonly Option[] WindowPresets =
    {
        new("Maximized", "Развёрнуто"),
        new("Laptop", "Ноутбук 1366×768"),
        new("FullHd", "Full HD 1920×1080"),
        new("Tablet", "Планшет 1280×800"),
        new("Compact", "Компактное 1024×768")
    };

    private static readonly Option[] Capabilities =
    {
        new("Restaurant", "Ресторан"),
        new("Store", "Магазин"),
        new("Both", "Ресторан + магазин")
    };

    private readonly BrowserProfile? _existing;
    private string _selectedColor;
    private readonly string _nextSpaceName;
    private readonly string _nextLehaName;
    private bool _nameOptionsReady;
    private string _customName = string.Empty;

    public ProfileDialog(BrowserProfile? profile = null, string nextSpaceName = "Space 1", string nextLehaName = "Leha 1")
    {
        InitializeComponent();
        _existing = profile;
        _nextSpaceName = nextSpaceName;
        _nextLehaName = nextLehaName;
        _selectedColor = profile?.Color ?? Colors[0];

        HeadingText.Text = profile is null ? "Новое пространство" : "Изменить пространство";
        NameBox.Text = profile?.Name ?? string.Empty;
        CommentBox.Text = profile?.Comment ?? string.Empty;
        NamePresetBox.ItemsSource = new[] { "Space", "Leha" };
        NamePresetBox.SelectedIndex = 0;
        NameOptions.Visibility = profile is null ? Visibility.Visible : Visibility.Collapsed;
        NameHint.Visibility = NameOptions.Visibility;
        HomeUrlBox.Text = profile?.HomeUrl ?? "https://ya.ru/";
        CapabilityBox.ItemsSource = Capabilities;
        CapabilityBox.SelectedItem = FindOption(Capabilities, profile?.Capability, "Both");
        RegionBox.ItemsSource = RegionCatalog.All;
        RegionBox.SelectedItem = RegionCatalog.Find(profile?.Region, profile?.TimeZoneId);
        LanguageBox.ItemsSource = Languages;
        LanguageBox.SelectedItem = FindOption(Languages, profile?.BrowserLanguage, "ru-RU");
        WindowPresetBox.ItemsSource = WindowPresets;
        WindowPresetBox.SelectedItem = FindOption(WindowPresets, profile?.WindowPreset, "Maximized");
        var readyAfterHours = Math.Max(0, profile?.ReadyAfterHours ?? ProfileReadiness.DefaultWaitingHours);
        ReadyDaysBox.Text = (readyAfterHours / 24).ToString();
        ReadyHoursBox.Text = (readyAfterHours % 24).ToString();
        UpdateTimezoneText();

        BuildColorPicker();
        _nameOptionsReady = true;
        UpdateNameChoice();
        Loaded += (_, _) => { if (_existing is null) NamePresetBox.Focus(); else NameBox.Focus(); };
    }

    public BrowserProfile? Result { get; private set; }

    private void NamePreset_Changed(object sender, SelectionChangedEventArgs e) => UpdateNameChoice();

    private void CustomName_Changed(object sender, RoutedEventArgs e)
    {
        if (!_nameOptionsReady || _existing is not null) return;
        if (CustomNameBox.IsChecked != true) _customName = NameBox.Text;
        UpdateNameChoice();
        if (CustomNameBox.IsChecked == true) NameBox.Focus();
    }

    private void UpdateNameChoice()
    {
        if (!_nameOptionsReady || _existing is not null) return;
        var custom = CustomNameBox.IsChecked == true;
        NamePresetBox.IsEnabled = !custom;
        NameBox.IsReadOnly = !custom;
        NameBox.Text = custom ? _customName : NamePresetBox.SelectedItem as string == "Leha" ? _nextLehaName : _nextSpaceName;
        NameHint.Text = custom ? "Введите своё название — до 48 символов." : "Номер подставляется автоматически и учитывает архив.";
    }

    private void BuildColorPicker()
    {
        foreach (var color in Colors)
        {
            var button = new Button
            {
                Width = 42,
                Height = 42,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                BorderBrush = color == _selectedColor ? Brushes.White : Brushes.Transparent,
                BorderThickness = new Thickness(3),
                Tag = color,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = color
            };
            button.Click += ColorButton_Click;
            ColorPanel.Children.Add(button);
        }
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button selected || selected.Tag is not string color)
        {
            return;
        }

        _selectedColor = color;
        foreach (Button button in ColorPanel.Children)
        {
            button.BorderBrush = Equals(button, selected) ? Brushes.White : Brushes.Transparent;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ValidationText.Text = "Введите название пространства.";
            NameBox.Focus();
            return;
        }

        if (!TryReadReadyAfterHours(out var readyAfterHours))
        {
            return;
        }

        var profile = _existing ?? new BrowserProfile();
        profile.Name = name;
        profile.Comment = CommentBox.Text.Trim();
        profile.Color = _selectedColor;
        profile.HomeUrl = BrowserLauncher.NormalizeUrl(HomeUrlBox.Text);
        profile.BrowserLanguage = (LanguageBox.SelectedItem as Option)?.Value ?? "ru-RU";
        profile.WindowPreset = (WindowPresetBox.SelectedItem as Option)?.Value ?? "Maximized";
        profile.Capability = (CapabilityBox.SelectedItem as Option)?.Value ?? "Both";
        var region = RegionBox.SelectedItem as RegionOption ?? RegionCatalog.Default;
        profile.Region = region.Region;
        profile.TimeZoneId = region.TimeZoneId;
        profile.ReadyAfterHours = readyAfterHours;
        Result = profile;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void RegionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTimezoneText();
    }

    private void UpdateTimezoneText()
    {
        var region = RegionBox.SelectedItem as RegionOption ?? RegionCatalog.Default;
        SystemTimezoneText.Text = $"Будет применено: {region.TimeZoneId} ({region.UtcOffset})";
    }

    private bool TryReadReadyAfterHours(out int totalHours)
    {
        totalHours = 0;
        if (!int.TryParse(ReadyDaysBox.Text.Trim(), out var days) || days is < 0 or > 3650)
        {
            ValidationText.Text = "Укажите количество дней от 0 до 3650.";
            ReadyDaysBox.Focus();
            return false;
        }

        if (!int.TryParse(ReadyHoursBox.Text.Trim(), out var hours) || hours is < 0 or > 23)
        {
            ValidationText.Text = "Укажите количество часов от 0 до 23.";
            ReadyHoursBox.Focus();
            return false;
        }

        totalHours = days * 24 + hours;
        return true;
    }

    private static Option FindOption(IEnumerable<Option> options, string? value, string fallback)
    {
        return options.FirstOrDefault(option => option.Value == value)
               ?? options.First(option => option.Value == fallback);
    }
}
