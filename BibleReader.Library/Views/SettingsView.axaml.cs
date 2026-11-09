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
    /// <summary>The size of the reading text until the reader chooses one: 18, larger when the system has set a larger font size.</summary>
    public static double DefaultFontSize => Math.Clamp(Math.Round(18 * UiScale.Factor), 12, 40);

    private bool _loading;

    /// <summary>Raised after the font size, the font or the words of Jesus setting changed, so the text can follow.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>Raised when the close button is clicked, so the host can show the text again.</summary>
    public event EventHandler? CloseRequested;

    public SettingsView()
    {
        InitializeComponent();
        FlowDirection = Localizer.FlowDirection; // mirrored for Arabic and Hebrew
        // The fonts that ship with the app first, then the ones installed on the system.
        FillFonts();
        UiSizeSlider.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) OnUiSizeChanged(); };
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
                Title = Localizer.Get("InstallFonts"),
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType(Localizer.Get("FontFiles")) { Patterns = UserFonts.PickerPatterns },
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
                FontStatus.Text = Localizer.Format("NoUsableFont", string.Join(", ", skipped));
                return;
            }
            FontStatus.Text = Localizer.Format("FontsInstalled", string.Join(", ", installed))
                + (skipped.Count > 0 ? Localizer.Format("NoUsableFontIn", string.Join(", ", skipped)) : "");
            FillFonts();
            FontBox.SelectedItem = installed[0]; // uses it at once (OnFontChanged saves and applies it)
        }
        catch (Exception ex)
        {
            FontStatus.Text = Localizer.Format("CouldNotInstallFont", ex.Message);
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
            UiSizeSlider.Value = Math.Round((settings.UiSize ?? UiScale.Factor) * 100 / 5) * 5; // the size in use: the chosen one, or the system's
            UiSizeText.Text = $"{UiSizeSlider.Value:0}%";
            LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == (settings.Language ?? "System"))
                ?? LanguageBox.Items.OfType<ComboBoxItem>().First();
            double size = settings.FontSize ?? DefaultFontSize;
            FontSizeSlider.Value = size;
            FontSizeText.Text = size.ToString("0");
            FontBox.SelectedItem = string.IsNullOrWhiteSpace(settings.FontFamily) ? EmbeddedFont : settings.FontFamily;
            JesusBox.IsChecked = settings.ShowWordsOfJesus ?? true;
            GreenVerseBox.IsChecked = settings.ShowVerseNumbersInGreen ?? false;
            HyphenateBox.IsChecked = settings.Hyphenate ?? true;
            NoPersecutionBox.IsChecked = settings.InMyCountryThereIsNoPersecution;
            UpdateSample();
            PathText.Text = settings.ConfigPath;
            int count = settings.Bibles?.Count ?? 0;
            BiblesText.Text = count == 1 ? Localizer.Get("OneBibleInstalled") : Localizer.Format("BiblesInstalled", count);
        }
        finally { _loading = false; }
    }

    private Avalonia.Threading.DispatcherTimer? _uiSizeTimer;

    /// <summary>
    /// The slider of the interface size moved. The size is saved at once, but taken (and the main window made again) a
    /// moment after the last move: a slider changes with every step of a drag.
    /// </summary>
    private void OnUiSizeChanged()
    {
        if (_loading) return;
        double percent = Math.Round(UiSizeSlider.Value);
        UiSizeText.Text = $"{percent:0}%";
        Settings.Current.UiSize = percent / 100;
        Save();
        _uiSizeTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _uiSizeTimer.Tick -= OnUiSizeTimer;
        _uiSizeTimer.Tick += OnUiSizeTimer;
        _uiSizeTimer.Stop();
        _uiSizeTimer.Start();
    }

    private void OnUiSizeTimer(object? sender, EventArgs e) => TakeUiSize();

    private void OnUiSizeResetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _uiSizeTimer?.Stop();
        Settings.Current.UiSize = null; // the size of the system
        Save();
        TakeUiSize();
    }

    private void TakeUiSize()
    {
        _uiSizeTimer?.Stop();
        BibleToolbar.ReturnToSettings = true; // the new window shows this page again
        if (!UiScale.Change()) BibleToolbar.ReturnToSettings = false; // nobody opens it again: the next start
    }

    /// <summary>The reader chose a language: it is saved and taken, and the app opens its main window again in it.</summary>
    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        var language = tag == "System" ? null : tag;
        if (Settings.Current.Language == language) return;
        Settings.Current.Language = language;
        Save();
        // The head of the app opens its main window again in the new language; this page is then shown again.
        BibleToolbar.ReturnToSettings = true;
        if (!Localizer.Change(language))
        {
            BibleToolbar.ReturnToSettings = false;
            LanguageHint.Text = Localizer.Get("RestartForLanguage"); // nobody opens the window again: it is the next start
            LanguageHint.IsVisible = true;
        }
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

    private void OnHyphenateChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.Hyphenate = HyphenateBox.IsChecked == true;
        Save();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
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
