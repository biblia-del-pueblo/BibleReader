using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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
        SizeChanged += (_, e) => ApplyColumns(e.NewSize);
    }

    /// <summary>
    /// Screens whose shorter side is at least this many device-independent pixels (a tablet; a phone has about 360-430)
    /// show several columns that are paged sideways; narrower ones show one column that scrolls up and down.
    /// <see cref="double.PositiveInfinity"/> keeps one column everywhere.
    /// </summary>
    public double TwoColumnsMinSize { get; set; } = 600;

    /// <summary>
    /// The least width of a column on a tablet, in device-independent units (the 96 dpi measure of Avalonia, which
    /// the platform's density scaling is applied to: a 1280 wide tablet screen is 1280 of them). As many columns as
    /// fit at this width are shown (two or more), and they are then widened to fill the screen: 1280 gives three.
    /// When not even two fit, one scrolling column is shown instead.
    /// </summary>
    public double MinColumnWidth { get; set; } = 350;

    private void ApplyColumns(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;

        const double spacing = 32;
        double available = size.Width - Block.Margin.Left - Block.Margin.Right;
        double minColumn = Math.Max(1, MinColumnWidth);
        int columns = Math.Min(size.Width, size.Height) >= TwoColumnsMinSize
            ? (int)Math.Floor((available + spacing) / (minColumn + spacing))
            : 1;
        if (columns >= 2)
        {
            // A fixed column width makes the text block page its columns: it shows as many whole columns as fit.
            // The width is the share of the screen for that many columns (a pixel less, against rounding).
            Block.ColumnSpacing = spacing;
            Block.ColumnWidth = Math.Floor((available - (columns - 1) * spacing) / columns) - 1;
            Block.Orientation = Orientation.Horizontal;
        }
        else
        {
            Block.Orientation = Orientation.Vertical;
            Block.ColumnWidth = double.NaN;
        }
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
