using Avalonia.Controls.Documents;

namespace BibleReader.Library.UI;

/// <summary>
/// Ends the current page of a <see cref="BibleTextBlock"/>: the text after it starts on a new page.
/// With columns a page is the set of columns visible at once (the first column of the next page gets the text,
/// columns skipped on the way stay empty). A single column has no pages, so there it leaves a gap.
/// </summary>
/// <example>
/// <code>&lt;Run&gt;End of the chapter.&lt;/Run&gt;&lt;local:PageBreak/&gt;&lt;Run&gt;Next chapter…&lt;/Run&gt;</code>
/// </example>
public class PageBreak : LineBreak
{
}
