using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SpacesBrowser;
using SpacesBrowser.Dialogs;
using SpacesBrowser.Models;

static class ProfileUiSmokeTests
{
    // Exercise the real WPF controls without showing windows or loading personal data.
    public static void Run(string? outputDirectory)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                var dialog = new ProfileDialog(nextSpaceName: "Space 13", nextLehaName: "Leha 8");
                var name = (TextBox)dialog.FindName("NameBox");
                var preset = (ComboBox)dialog.FindName("NamePresetBox");
                var custom = (CheckBox)dialog.FindName("CustomNameBox");
                Require(name.Text == "Space 13" && name.IsReadOnly, "Default generated name");
                preset.SelectedItem = "Leha";
                Require(name.Text == "Leha 8", "Leha selection");
                custom.IsChecked = true;
                Require(name.Text == "" && !name.IsReadOnly && !preset.IsEnabled, "Custom name enabled");
                name.Text = "Мой профиль";
                custom.IsChecked = false;
                Require(name.Text == "Leha 8" && name.IsReadOnly, "Back to generated name");
                custom.IsChecked = true;
                Require(name.Text == "Мой профиль", "Keep custom draft when switching");
                custom.IsChecked = false;
                Render(dialog, 560, 690, outputDirectory, "new-profile");

                var existing = new BrowserProfile { Name = "Leha 3", Comment = "Сохранённая заметка" };
                var edit = new ProfileDialog(existing);
                Require(((TextBox)edit.FindName("NameBox")).Text == "Leha 3", "Editing must not renumber");
                Require(((TextBox)edit.FindName("CommentBox")).Text == existing.Comment, "Load existing comment");
                Require(((StackPanel)edit.FindName("NameOptions")).Visibility == Visibility.Collapsed, "Presets only during creation");

                var main = new MainWindow();
                var profiles = new List<BrowserProfile>
                {
                    new() { Name = "Space 13", Capability = "Both", CreatedAtUtc = DateTime.UtcNow,
                        Comment = "Новый профиль. Заметка видна прямо на карточке.", LastPublicIp = "203.0.113.42" },
                    new() { Name = "Leha 8", Capability = "Restaurant", CreatedAtUtc = DateTime.UtcNow.AddDays(-8),
                        Comment = "Длинная заметка: " + new string('я', 180) },
                    new() { Name = "Space 12", Capability = "Store", CreatedAtUtc = DateTime.UtcNow.AddDays(-9) },
                    new() { Name = "Space 11", Capability = "Both", CreatedAtUtc = DateTime.UtcNow.AddDays(-10), IsArchived = true }
                };
                typeof(MainWindow).GetField("_profiles", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, profiles);
                Invoke(main, "RefreshCards");
                Require(((WrapPanel)main.FindName("FoldersPanel")).Children.Count == 4, "Four folder cards");
                Render(main, 1120, 720, outputDirectory, "folders");
                Invoke(main, "NavigateToFolder", "All");
                var cards = (WrapPanel)main.FindName("ActiveProfilesPanel");
                Require(cards.Children.Count == 3, "All active profiles");
                Render(main, 1120, 720, outputDirectory, "profiles");
                foreach (Border card in cards.Children)
                {
                    var grid = (Grid)card.Child;
                    var details = (StackPanel)grid.Children[1];
                    var open = (Button)grid.Children[2];
                    var detailsBottom = details.TranslatePoint(new Point(0, details.DesiredSize.Height), grid).Y;
                    Require(detailsBottom <= open.TranslatePoint(new Point(), grid).Y, "Card details must not overlap Open button");
                }
                Invoke(main, "NavigateToFolder", "Archive");
                Require(cards.Children.Count == 1, "Archive view");
                Invoke(main, "NavigateToFolder", new object?[] { null });
                Require(((WrapPanel)main.FindName("FoldersPanel")).Visibility == Visibility.Visible, "Return to folders");
                var search = (TextBox)main.FindName("SearchBox");
                search.Text = "заметка 203.0.113.42";
                Invoke(main, "RefreshCards");
                Require(cards.Children.Count == 1, "Global multi-keyword search");
                Require(((TextBlock)main.FindName("ActiveHeading")).Text == "Результаты поиска · 1", "Search result heading");
                Require(((Button)main.FindName("SearchClearButton")).Visibility == Visibility.Visible, "Clear search button");
                Render(main, 1120, 720, outputDirectory, "search");
                search.Text = "несуществующее";
                Invoke(main, "RefreshCards");
                Require(cards.Children.Count == 0 && ((Border)main.FindName("EmptyState")).Visibility == Visibility.Visible,
                    "Empty search state");
                search.Clear();
                Invoke(main, "RefreshCards");
                Require(((WrapPanel)main.FindName("FoldersPanel")).Visibility == Visibility.Visible, "Clearing search restores folders");
                main.Close();
                edit.Close();
                dialog.Close();
                app.Shutdown();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("WPF profile UI check failed", error);
        Console.WriteLine("PASS: WPF name controls, editing, folders, archive, card layout");
    }

    private static void Invoke(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static void Render(Window window, int width, int height, string? directory, string name)
    {
        var content = (FrameworkElement)window.Content;
        if (content is Panel panel) panel.Background = window.Background;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
