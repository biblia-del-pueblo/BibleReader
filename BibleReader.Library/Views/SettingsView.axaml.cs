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
        FontBox.ItemsSource = (new[] { EmbeddedFont, InterFont }
            .Concat(FontManager.Current.SystemFonts.Select(f => f.Name).Where(n => n != EmbeddedFont && n != InterFont).Distinct())
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
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
        _ => new FontFamily(name),
    };

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

    /// <summary>
    /// Shows the example text in the chosen font and size: a normal sentence followed by words of Jesus, which are
    /// red unless that setting is off.
    /// </summary>
    private void UpdateSample()
    {
        var settings = Settings.Current;
        SampleText.FontFamily = FamilyOf(settings.FontFamily);
        SampleText.FontSize = settings.FontSize ?? DefaultFontSize;
        JesusRun.Foreground = settings.ShowWordsOfJesus ?? true ? new SolidColorBrush(Color.Parse("#B22222")) : null;
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
