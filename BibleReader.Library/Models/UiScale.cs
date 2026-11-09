using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace BibleReader.Library;

/// <summary>
/// How much larger (or smaller) the text of the interface is than usual: the size that the reader chose in the settings
/// (<see cref="Settings.UiSize"/>), or else the size of the system. Android has a "Font size" setting that Avalonia does
/// not apply by itself: the Android head reads it and gives it to <see cref="SetSystemFactor"/>. Sizes that are written
/// in the AXAML files are scaled with <c>FontSize="{loc:Size 12}"</c>, the ones in code with <see cref="Size"/>, and the
/// controls of the theme (buttons, check boxes, text boxes ...) with a style that is added to the application.
/// </summary>
public static class UiScale
{
    /// <summary>The size that the controls of the Fluent theme have: the sizes of the controls follow it.</summary>
    const double ThemeFontSize = 14;

    public const double Min = 0.8, Max = 2.0;

    /// <summary>1 for the usual size, 1.3 for a text that is 30% larger.</summary>
    public static double Factor { get; private set; } = 1;

    /// <summary>The size that the system has set (1 when it has none); the head of the app gives it.</summary>
    public static double SystemFactor { get; private set; } = 1;

    /// <summary>A font size, scaled.</summary>
    public static double Size(double size) => Math.Round(size * Factor, 1);

    static Style? style;

    /// <summary>
    /// Raised by <see cref="Change"/>. The head of the app answers it by opening its main window or view again, as it
    /// does for a change of language, because the sizes are read when a view is made.
    /// </summary>
    public static event EventHandler? Changed;

    /// <summary>Gives the size of the system, and takes the size that is in use then.</summary>
    public static void SetSystemFactor(Application app, double factor)
    {
        SystemFactor = factor;
        Apply(app);
    }

    /// <summary>Takes the size from the settings (or the system's) for the whole app. Views read it when they are made.</summary>
    public static void Apply(Application app)
    {
        Factor = Math.Clamp(Settings.Current.UiSize ?? SystemFactor, Min, Max);
        if (style is not null) { app.Styles.Remove(style); style = null; }
        if (Math.Abs(Factor - 1) < 0.01) return; // the usual size is the theme's own
        style = new Style(x => x.Is<TemplatedControl>())
        {
            Setters = { new Setter(TemplatedControl.FontSizeProperty, Math.Round(ThemeFontSize * Factor, 1)) },
        };
        app.Styles.Add(style);
    }

    /// <summary>The reader changed <see cref="Settings.UiSize"/>: it is taken and <see cref="Changed"/> is raised.</summary>
    /// <returns>False when nobody listens to <see cref="Changed"/>: the size is then taken when the app is started again.</returns>
    public static bool Change()
    {
        if (Application.Current is { } app) Apply(app);
        var handlers = Changed;
        handlers?.Invoke(null, EventArgs.Empty);
        return handlers is not null;
    }
}
