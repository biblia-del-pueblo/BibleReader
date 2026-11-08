using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace BibleReader.Library.UI;

/// <summary>
/// The settings page: shown instead of the text while the toolbar menu's "Settings…" is chosen. Changes are applied
/// at once and saved to <see cref="Settings.Current"/>. The layout is in SettingsView.axaml.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>The name of the font that is embedded in this assembly (see <see cref="FamilyOf"/>).</summary>
    public const string EmbeddedFont = "Bible Roman";
    /// <summary>Inter comes with the Avalonia.Fonts.Inter package, which every platform head registers (<c>WithInterFont</c>).</summary>
    public const string InterFont = "Inter";
    /// <summary>All the fonts in this assembly's Assets folder (by family name); Bible Roman is the default.</summary>
    private static readonly string[] AssetFonts = { EmbeddedFont, "Cardo", "Frank Ruehl CLM" };
    public const double DefaultFontSize = 18;

    private bool _loading;

    /// <summary>Raised after the font size, the font or the words of Jesus setting changed, so the text can follow.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>Raised when the close button is clicked, so the host can show the text again.</summary>
    public event EventHandler? CloseRequested;

    public SettingsView()
    {
        InitializeComponent();
        // The fonts that ship with the app first, then the ones installed on the system.
        FillFonts();
        FontSizeSlider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) OnFontSizeChanged(); };
        AttachedToVisualTree += (_, _) => Reload();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsVisible) Reload(); };
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The font family for a saved name. Bible Roman lives in this assembly's Assets folder and Inter in the
    /// Avalonia.Fonts.Inter package; both need their own URI. Anything else is a system font, by name.
    /// </summary>
    public static FontFamily FamilyOf(string? name) => name switch
    {
        null or "" or EmbeddedFont => new FontFamily("avares://BibleReader.Library/Assets#" + EmbeddedFont),
        InterFont => new FontFamily("fonts:Inter#Inter"),
        _ when string.IsNullOrWhiteSpace(name) => new FontFamily("avares://BibleReader.Library/Assets#" + EmbeddedFont),
        _ when AssetFonts.Contains(name) => new FontFamily("avares://BibleReader.Library/Assets#" + name),
        _ when UserFonts.Contains(name) => UserFonts.FamilyOf(name),
        _ => new FontFamily(name),
    };

    /// <summary>Lists the fonts: the ones that ship with the app, the installed ones and the system's.</summary>
    private void FillFonts()
    {
        FontBox.ItemsSource = AssetFonts.Append(InterFont)
            .Concat(UserFonts.Families)
            .Concat(FontManager.Current.SystemFonts.Select(f => f.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Picks a font file or a zip of fonts and installs it into <see cref="UserFonts.Folder"/>.</summary>
    private async void OnInstallFontClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        InstallFontButton.IsEnabled = false;
        try
        {
            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Install fonts",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Fonts or zip of fonts") { Patterns = UserFonts.PickerPatterns },
                },
            });
            if (files.Count == 0) return;
            var installed = new List<string>();
            var skipped = new List<string>();
            foreach (var file in files)
            {
                List<string> families;
                await using (var stream = await file.OpenReadAsync())
                    families = await System.Threading.Tasks.Task.Run(() => UserFonts.Install(file.Name, stream));
                if (families.Count == 0) skipped.Add(file.Name);
                installed.AddRange(families.Where(f => !installed.Contains(f)));
            }
            if (installed.Count == 0)
            {
                FontStatus.Text = "No usable font found in " + string.Join(", ", skipped);
                return;
            }
            FontStatus.Text = "Installed: " + string.Join(", ", installed)
                + (skipped.Count > 0 ? ". No usable font in " + string.Join(", ", skipped) : "");
            FillFonts();
            FontBox.SelectedItem = installed[0]; // uses it at once (OnFontChanged saves and applies it)
        }
        catch (Exception ex)
        {
            FontStatus.Text = "Could not install the font: " + ex.Message;
        }
        finally { InstallFontButton.IsEnabled = true; }
    }

    /// <summary>Shows the current values (also when the page is shown again).</summary>
    private void Reload()
    {
        _loading = true;
        try
        {
            var settings = Settings.Current;
            ThemeBox.SelectedIndex = settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
            double size = settings.FontSize ?? DefaultFontSize;
            FontSizeSlider.Value = size;
            FontSizeText.Text = size.ToString("0");
            FontBox.SelectedItem = string.IsNullOrWhiteSpace(settings.FontFamily) ? EmbeddedFont : settings.FontFamily;
            JesusBox.IsChecked = settings.ShowWordsOfJesus ?? true;
            GreenVerseBox.IsChecked = settings.ShowVerseNumbersInGreen ?? false;
            NoPersecutionBox.IsChecked = settings.InMyCountryThereIsNoPersecution;
            UpdateSample();
            PathText.Text = settings.ConfigPath;
            int count = settings.Bibles?.Count ?? 0;
            BiblesText.Text = count == 1 ? "1 Bible installed" : $"{count} Bibles installed";
        }
        finally { _loading = false; }
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        Settings.Current.Theme = theme;
        ApplyTheme(theme);
        Save();
    }

    private void OnFontSizeChanged()
    {
        if (_loading) return;
        double size = Math.Round(FontSizeSlider.Value);
        FontSizeText.Text = size.ToString("0");
        if (Settings.Current.FontSize == size) return;
        Settings.Current.FontSize = size;
        UpdateSample();
        Save();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFontChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontBox.SelectedItem is not string name || Settings.Current.FontFamily == name) return;
        Settings.Current.FontFamily = name;
        UpdateSample();
        Save();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnJesusChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.ShowWordsOfJesus = JesusBox.IsChecked == true;
        UpdateSample();
        Save();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNoPersecutionChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.InMyCountryThereIsNoPersecution = NoPersecutionBox.IsChecked == true;
        Save();
    }

    private void OnGreenVerseChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.ShowVerseNumbersInGreen = GreenVerseBox.IsChecked == true;
        UpdateSample();
        Save();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Shows the example text in the chosen font and size: a normal sentence followed by words of Jesus, which are
    /// red unless that setting is off.
    /// </summary>
    private void UpdateSample()
    {
        var settings = Settings.Current;
        SampleText.FontFamily = FamilyOf(settings.FontFamily);
        SampleText.FontSize = settings.FontSize ?? DefaultFontSize;
        SetColor(VerseNumberRun, settings.ShowVerseNumbersInGreen ?? false ? Color.Parse("#2E8B57") : null);
        SetColor(JesusRun, settings.ShowWordsOfJesus ?? true ? Color.Parse("#B22222") : null);
    }

    /// <summary>
    /// Colours a run, or hands it back to the theme. Assigning <c>Foreground = null</c> would be a local value that
    /// stops the inheritance from the text block (and so renders black in dark mode); clearing it restores it.
    /// </summary>
    private static void SetColor(Avalonia.Controls.Documents.Run run, Color? color)
    {
        if (color is { } c) run.Foreground = new SolidColorBrush(c);
        else run.ClearValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty);
    }

    private static void Save()
    {
        try { Settings.Current.Save(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
    }

    /// <summary>Applies a saved theme name ("Light", "Dark"; anything else follows the system).</summary>
    public static void ApplyTheme(string? theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }
}
