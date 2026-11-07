using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;

namespace BibleReader.Library.UI;

/// <summary>
/// A <see cref="BibleToolbar"/> above a <see cref="BibleTextBlock"/>, ready to read: give it a folder with the
/// Bible's markdown files (<see cref="OpenAsync"/>) and it shows the first book. The layout is in BibleView.axaml.
/// </summary>
public partial class BibleView : UserControl
{
    public BibleView()
    {
        InitializeComponent();
    }

    /// <summary>The text block, for settings the view does not expose itself.</summary>
    public BibleTextBlock TextBlock => Block;

    /// <summary>Vertical (default): one column that scrolls up and down. Horizontal: columns scrolled sideways.</summary>
    public Orientation Orientation
    {
        get => Block.Orientation;
        set => Block.Orientation = value;
    }

    /// <summary>
    /// Reads the books in <paramref name="directory"/> and shows <paramref name="bookIndex"/> (default: the first
    /// real book, skipping the front matter such as the table of contents).
    /// </summary>
    public async Task OpenAsync(string directory, int bookIndex = -1)
    {
        // Listing the files and reading the book names is work for a worker thread: the first screen is waiting.
        var books = await Task.Run(() => new Bible().Open(directory).Books);
        Block.Books = books;
        if (bookIndex < 0)
        {
            bookIndex = books.FindIndex(book =>
                !System.IO.Path.GetFileName(book.File ?? string.Empty).StartsWith("00.", StringComparison.Ordinal));
            if (bookIndex < 0) bookIndex = 0;
        }
        await Block.ShowBookAsync(bookIndex);
    }
}
