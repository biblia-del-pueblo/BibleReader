using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;

namespace BibleReader.Library.UI;

/// <summary>
/// A footnote inside a <see cref="BibleTextBlock"/>. Its content (the <see cref="Span.Inlines"/>: runs, nested
/// spans, links/references, line breaks) is not flowed; instead a small superscript marker appears at this
/// position, and clicking it opens a popup showing the content.
/// </summary>
/// <example>
/// <code>
/// &lt;Run&gt;In the beginning&lt;/Run&gt;
/// &lt;local:Footnote&gt;Or: "At first". See &lt;Span TextDecorations="Underline"&gt;Gen 2:4&lt;/Span&gt;.&lt;/local:Footnote&gt;
/// </code>
/// </example>
public class Footnote : Span
{
    /// <summary>Marker text; when null the footnotes are lettered a, b, c… in document order.</summary>
    public static readonly StyledProperty<string?> MarkerProperty =
        AvaloniaProperty.Register<Footnote, string?>(nameof(Marker));

    public string? Marker { get => GetValue(MarkerProperty); set => SetValue(MarkerProperty, value); }

    public Footnote() { }
    public Footnote(string text) { Inlines!.Add(new Run(text)); }

    /// <summary>
    /// A copy of the content that can be shown in the popup. An inline can only have one parent, so the
    /// footnote's own inlines stay put and the popup gets clones carrying the locally set formatting.
    /// </summary>
    internal IEnumerable<Inline> CloneContent() => (Inlines ?? []).Select(Clone);

    private static Inline Clone(Inline inline)
    {
        Inline copy = inline switch
        {
            LineBreak => new LineBreak(),
            Run run => new Run(run.Text),
            Span span => CloneSpan(span),
            _ => new Run(string.Empty), // InlineUIContainer & co. have no popup representation
        };
        if (inline.IsSet(TextElement.FontFamilyProperty)) copy.FontFamily = inline.FontFamily;
        if (inline.IsSet(TextElement.FontSizeProperty)) copy.FontSize = inline.FontSize;
        if (inline.IsSet(TextElement.FontStyleProperty)) copy.FontStyle = inline.FontStyle;
        if (inline.IsSet(TextElement.FontWeightProperty)) copy.FontWeight = inline.FontWeight;
        if (inline.IsSet(TextElement.ForegroundProperty)) copy.Foreground = inline.Foreground;
        if (inline.TextDecorations is { } d) copy.TextDecorations = d;
        return copy;
    }

    private static Span CloneSpan(Span span)
    {
        var copy = new Span();
        foreach (var child in span.Inlines ?? []) copy.Inlines!.Add(Clone(child));
        return copy;
    }
}
