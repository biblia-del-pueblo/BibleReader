using Avalonia.Controls.Documents;

namespace BibleReader.Library.UI;

/// <summary>
/// Ends the current column of a <see cref="BibleTextBlock"/>: the text after it starts at the top of the next
/// column. A single column has nothing to break to, so there it leaves a gap instead.
/// </summary>
/// <example>
/// <code>&lt;Run&gt;End of the chapter.&lt;/Run&gt;&lt;local:PageBreak/&gt;&lt;Run&gt;Next chapter…&lt;/Run&gt;</code>
/// </example>
public class PageBreak : LineBreak
{
}
