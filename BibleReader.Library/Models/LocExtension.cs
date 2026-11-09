using System;
using Avalonia.Markup.Xaml;

namespace BibleReader.Library;

/// <summary>
/// A text of the app in the language of the reader, for the AXAML files: <c>Text="{loc:Loc Settings}"</c> with
/// <c>xmlns:loc="using:BibleReader.Library"</c>. The key is one of Resources/Strings.resx.
/// </summary>
public class LocExtension : MarkupExtension
{
    public LocExtension(string key) { Key = key; }

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Localizer.Get(Key);
}
