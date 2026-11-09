using System;
using Avalonia.Markup.Xaml;

namespace BibleReader.Library;

/// <summary>
/// A font size of the app scaled by the font size that the system has set (see <see cref="UiScale"/>), for the AXAML
/// files: <c>FontSize="{loc:Size 12}"</c> with <c>xmlns:loc="using:BibleReader.Library"</c>.
/// </summary>
public class SizeExtension : MarkupExtension
{
    public SizeExtension(double size) { Size = size; }

    public double Size { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => UiScale.Size(Size);
}
