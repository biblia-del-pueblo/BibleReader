using Avalonia.Controls.Documents;

namespace BibleReader.Library.UI;

/// <summary>
/// A word (or phrase) that has Strong's numbers, inside a <see cref="BibleTextBlock"/>. It is flowed like any other
/// span; the text block remembers where it is, and clicking it shows the entries of the Strong's dictionaries.
/// </summary>
public class StrongsSpan : Span
{
    /// <summary>The Strong's numbers, separated by spaces ("H7225 G746").</summary>
    public string Strong { get; set; } = "";
}
