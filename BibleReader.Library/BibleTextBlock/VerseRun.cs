using Avalonia.Controls.Documents;

namespace BibleReader.Library.UI;

/// <summary>
/// A run that marks where a verse (or a chapter, <c>Verse == 0</c>) starts in a <see cref="BibleTextBlock"/>:
/// the bold verse number, or an empty run in front of a chapter. The text block uses these marks to report and to
/// scroll to a <see cref="BibleTextBlock.Position"/>.
/// </summary>
public sealed class VerseRun : Run
{
    /// <summary>The verse this run starts.</summary>
    public BookText.VersePosition? Position { get; set; }

    public VerseRun() { }
    public VerseRun(string text) : base(text) { }
}
