using System;
using System.Globalization;
using System.Resources;

namespace BibleReader.Library;

/// <summary>
/// The texts of the app in the language of the reader. They are in Resources/Strings.resx (English) and
/// Resources/Strings.de.resx (German); more languages are more files, Strings.fr.resx and so on. The language is
/// the one of the device unless the reader chose one in the settings (<see cref="Settings.Language"/>).
/// </summary>
public static class Localizer
{
    static readonly ResourceManager Manager = new("BibleReader.Library.Resources.Strings", typeof(Localizer).Assembly);

    /// <summary>The culture of the texts. The device's language until <see cref="Apply"/> chooses another.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Right to left for the languages that are written so (Arabic, Hebrew), else left to right.</summary>
    public static Avalonia.Media.FlowDirection FlowDirection =>
        Culture.TextInfo.IsRightToLeft ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;

    /// <summary>Takes the language that the reader chose: "en", "de" and so on, or null (or "System") for the language of the device.</summary>
    public static void Apply(string? language)
    {
        try { Culture = string.IsNullOrWhiteSpace(language) || language == "System" ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { Culture = CultureInfo.CurrentUICulture; }
    }

    /// <summary>
    /// Raised by <see cref="Change"/> after the language changed. The head of the app (the desktop app, the Android app)
    /// answers it by opening its main window or view again, which builds all the texts in the new language.
    /// </summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// The reader chose another language: it is taken, and <see cref="LanguageChanged"/> is raised.
    /// </summary>
    /// <returns>False when nobody listens to <see cref="LanguageChanged"/>: the texts then change when the app is started again.</returns>
    public static bool Change(string? language)
    {
        Apply(language);
        var handlers = LanguageChanged;
        handlers?.Invoke(null, EventArgs.Empty);
        return handlers is not null;
    }

    /// <summary>The text for a key; the key itself when there is none, so a missing text is seen and does not crash the app.</summary>
    public static string Get(string key) => Manager.GetString(key, Culture) ?? key;

    /// <summary>The text for a key with the arguments put in its {0}, {1} ...</summary>
    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    /// <summary>The name of a kind of module ("Bible", "Dictionary" ...).</summary>
    public static string ClassName(InstallableBook.Classes kind) => Get("Class_" + kind);
}
