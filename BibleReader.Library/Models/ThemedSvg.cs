using System;
using Avalonia;
using Avalonia.Styling;

namespace BibleReader.Library;

/// <summary>
/// An icon of the app: an <see cref="Avalonia.Svg.Skia.Svg"/> that colours itself for the theme (dark gray in the light
/// theme, white in the dark one, also when the theme is switched) and whose size follows the interface size
/// (<see cref="UiScale"/>). Use it for all the icons, in AXAML as
/// <c>&lt;loc:ThemedSvg Path="/Assets/gear.svg" Size="18" /&gt;</c> with <c>xmlns:loc="using:BibleReader.Library"</c>.
/// </summary>
public class ThemedSvg : Avalonia.Svg.Skia.Svg
{
    // Only fill is set: the icons are filled shapes (a stroke would keep the colour of the file).
    const string LightCss = "path, rect { fill: #333333 !important; }";
    const string DarkCss = "path, rect { fill: #FFFFFF !important; }";

    /// <summary>
    /// The size of the (square) icon, before the interface size is applied: <c>Size="18"</c> is 18 at the usual size, 27 when
    /// the interface is 50% larger. Without it, <c>Width</c> and <c>Height</c> are used as they are.
    /// </summary>
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<ThemedSvg, double>(nameof(Size), double.NaN);

    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>
    /// Whether the icon is coloured for the theme (the default). <c>ThemeColor="False"</c> leaves the colours of the file as
    /// they are, for an icon that has colours of its own; the size still follows the interface size.
    /// </summary>
    public static readonly StyledProperty<bool> ThemeColorProperty =
        AvaloniaProperty.Register<ThemedSvg, bool>(nameof(ThemeColor), true);

    public bool ThemeColor { get => GetValue(ThemeColorProperty); set => SetValue(ThemeColorProperty, value); }

    static ThemedSvg()
    {
        SizeProperty.Changed.AddClassHandler<ThemedSvg>((svg, _) => svg.ApplySize());
        ThemeColorProperty.Changed.AddClassHandler<ThemedSvg>((svg, _) => svg.ApplyTheme());
    }

    /// <summary>For icons made in AXAML (the base address of the file names is taken from the AXAML file).</summary>
    public ThemedSvg(IServiceProvider serviceProvider) : base(serviceProvider) { Initialize(); }

    /// <summary>For icons made in code: the files are those of this assembly's Assets folder.</summary>
    public ThemedSvg() : base(new Uri("avares://BibleReader.Library/")) { Initialize(); }

    private void Initialize()
    {
        ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyTheme(); // the theme is known once the icon is in the tree
        ApplySize();  // so is the interface size, which may have changed since the icon was made
    }

    private void ApplyTheme()
    {
        if (!ThemeColor) ClearValue(CssProperty); // the colours of the file
        else SetValue(CssProperty, ActualThemeVariant == ThemeVariant.Dark ? DarkCss : LightCss);
    }

    private void ApplySize()
    {
        if (double.IsNaN(Size)) return;
        Width = Height = UiScale.Size(Size);
    }
}
