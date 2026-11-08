using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Metadata;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using System.Text.RegularExpressions;

namespace BibleReader.Library.UI;

/// <summary>Raised when the user scrolls past the first or the last page of the shown text.</summary>
public sealed class BoundaryEventArgs(bool atEnd) : EventArgs
{
    /// <summary>True: scrolled past the end; false: scrolled back past the start.</summary>
    public bool AtEnd { get; } = atEnd;
}

/// <summary>Which side a child of <see cref="BibleTextBlock"/> floats to.</summary>
public enum FloatSide
{
    Left,
    Right,
}

/// <summary>
/// A panel that flows <see cref="Text"/> around its children, like CSS <c>float: left | right</c>.
/// Every child is a float; set <see cref="SideProperty"/> (FloatTextBlock.Side) on it to choose the side.
/// Floats on the same side stack vertically; text lines are narrowed to whatever space the floats leave.
/// </summary>
public class BibleTextBlock : Panel
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<BibleTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<BibleTextBlock>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<BibleTextBlock>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<BibleTextBlock>();

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<BibleTextBlock, TextAlignment>(nameof(TextAlignment), TextAlignment.Justify);

    /// <summary>Gap between a float and the text wrapping around it.</summary>
    public static readonly StyledProperty<double> FloatSpacingProperty =
        AvaloniaProperty.Register<BibleTextBlock, double>(nameof(FloatSpacing), 12);

    /// <summary>
    /// Number of text columns; 0 (default) means automatic: 1 column, or as many as fit when
    /// <see cref="ColumnWidth"/> is set.
    /// </summary>
    public static readonly StyledProperty<int> ColumnCountProperty =
        AvaloniaProperty.Register<BibleTextBlock, int>(nameof(ColumnCount), 0);

    /// <summary>
    /// Fixed width of each column (NaN = columns share the available width). With a bounded height the text
    /// fills a column to that height and then simply adds more columns, so the control grows wider.
    /// </summary>
    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<BibleTextBlock, double>(nameof(ColumnWidth), double.NaN);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<BibleTextBlock, double>(nameof(ColumnSpacing), 24);

    /// <summary>
    /// <see cref="Orientation.Horizontal"/> (default): columns side by side, scrolled one whole column at a time.
    /// <see cref="Orientation.Vertical"/>: a single column as wide as the control, scrolled vertically
    /// (touch drag with inertia, mouse wheel, scroll bar), meant for phones.
    /// </summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<BibleTextBlock, Orientation>(nameof(Orientation), Orientation.Horizontal);

    /// <summary>
    /// The verse at the start of the view (the first visible column, or the top edge in vertical mode).
    /// Setting it scrolls there, loading the verse's books first when they are not shown.
    /// </summary>
    public static readonly StyledProperty<Bible.VersePosition?> PositionProperty =
        AvaloniaProperty.Register<BibleTextBlock, Bible.VersePosition?>(nameof(Position),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// The Bible to read. Setting it (or binding it, e.g. to <see cref="BibleToolbar.Bible"/>) loads its books when
    /// they are not loaded yet and shows the first real book, skipping the front matter.
    /// </summary>
    public static readonly StyledProperty<Bible?> BibleProperty =
        AvaloniaProperty.Register<BibleTextBlock, Bible?>(nameof(Bible));

    /// <summary>
    /// True while the text asked for is still being prepared: a requested verse that is not on screen yet, or the very
    /// first text. Show a spinner while it is set.
    /// </summary>
    public static readonly DirectProperty<BibleTextBlock, bool> IsBusyProperty =
        AvaloniaProperty.RegisterDirect<BibleTextBlock, bool>(nameof(IsBusy), o => o.IsBusy);

    public static readonly AttachedProperty<FloatSide> SideProperty =
        AvaloniaProperty.RegisterAttached<BibleTextBlock, Control, FloatSide>("Side");

    static BibleTextBlock()
    {
        AffectsMeasure<BibleTextBlock>(TextProperty, FontSizeProperty, FontFamilyProperty,
            TextAlignmentProperty, FloatSpacingProperty, ColumnCountProperty, ColumnWidthProperty, ColumnSpacingProperty, OrientationProperty);
        AffectsArrange<BibleTextBlock>(SideProperty);
        PositionProperty.Changed.AddClassHandler<BibleTextBlock>((c, e) =>
        {
            if (!c._settingPosition) c.ScrollToPosition(e.NewValue as Bible.VersePosition);
        });
        BibleProperty.Changed.AddClassHandler<BibleTextBlock>((c, e) => c.OnBibleChanged(e.NewValue as Bible));
        AffectsMeasure<BibleTextBlock>(ForegroundProperty);
        SideProperty.Changed.AddClassHandler<Control>((c, _) => (c.Parent as BibleTextBlock)?.MarkDirty());
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public TextAlignment TextAlignment { get => GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public double FloatSpacing { get => GetValue(FloatSpacingProperty); set => SetValue(FloatSpacingProperty, value); }

    /// <summary>Whether the words of Jesus are coloured red. Call <see cref="RebuildAsync"/> after changing it.</summary>
    public bool ShowWordsOfJesus { get; set; } = true;
    /// <summary>Whether verse numbers are green; like <see cref="ShowWordsOfJesus"/> it applies when the text is rebuilt.</summary>
    public bool ShowVerseNumbersInGreen { get; set; }

    public Bible? Bible { get => GetValue(BibleProperty); set => SetValue(BibleProperty, value); }
    public Bible.VersePosition? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public int ColumnCount { get => GetValue(ColumnCountProperty); set => SetValue(ColumnCountProperty, value); }
    public double ColumnWidth { get => GetValue(ColumnWidthProperty); set => SetValue(ColumnWidthProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }

    public static FloatSide GetSide(Control control) => control.GetValue(SideProperty);
    public static void SetSide(Control control, FloatSide value) => control.SetValue(SideProperty, value);

    /// <param name="PageStart">The line follows a <see cref="PageBreak"/>: it starts a column.</param>
    private readonly record struct PlacedLine(TextLine Line, Point Origin, bool PageStart = false);

    /// <summary>Flattened-text index at which each line following a <see cref="PageBreak"/> starts.</summary>
    private HashSet<int> _breakStarts = new();

    private readonly List<PlacedLine> _lines = new();
    private readonly List<Rect> _floatRects = new();

    // ---- Position ------------------------------------------------------------------------------------
    // Verse (and chapter) starts are VerseRun inlines; Flatten records where each one lands in the flowed text.
    // Position is the last mark at or before the first visible line; setting it scrolls to the line of its mark.

    private List<(int Index, Bible.VersePosition Pos)> _markers = new();
    private Bible.VersePosition? _pendingPosition; // to scroll to once the books that contain it are laid out

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetAndRaise(IsBusyProperty, ref _isBusy, value); }

    /// <summary>Busy: a verse is wanted that is not shown yet, or books are set but no text has been laid out.</summary>
    private void UpdateBusy() => IsBusy = _pendingPosition is not null || (Books is not null && _lines.Count == 0);
    private bool _settingPosition, _positionQueued, _programmatic, _keepPosition;

    private void RequestPositionUpdate()
    {
        if (_positionQueued) return;
        _positionQueued = true;
        Dispatcher.UIThread.Post(UpdatePosition, DispatcherPriority.Background);
    }

    /// <summary>Publishes the verse at the start of the view as <see cref="Position"/>.</summary>
    private void UpdatePosition()
    {
        _positionQueued = false;
        // After scrolling to a requested verse the view may start a verse or two earlier (columns have fixed
        // tops); the requested value stays until the user scrolls on.
        if (_keepPosition || _markers.Count == 0 || !_hasLayout || _dirty || _pitch <= 0) return;

        PlacedLine? top = null;
        if (Orientation == Orientation.Vertical)
        {
            foreach (var pl in _lines)
                if (pl.Origin.Y + pl.Line.Height > _offsetY + 0.5) { top = pl; break; }
        }
        else
        {
            int column = _scrolling ? _firstColumn : 0;
            foreach (var pl in _lines)
                if (Math.Floor((pl.Origin.X + 1) / _pitch) >= column) { top = pl; break; }
        }
        if (top is not { } first) return;

        int index = first.Line.FirstTextSourceIndex;
        var best = _markers[0].Pos;
        foreach (var (at, pos) in _markers)
        {
            if (at > index) break;
            best = pos;
        }
        if (ReferenceEquals(best, Position)) return;
        _settingPosition = true;
        try { SetCurrentValue(PositionProperty, best); }
        finally { _settingPosition = false; }
    }

    /// <summary>
    /// The mark for <paramref name="pos"/> in the laid-out text: the exact verse, else the next verse of the
    /// chapter, else the chapter start, else the start of the book (also for 1:1, so the title shows).
    /// Null when the book is not in the text.
    /// </summary>
    private static (int Index, Bible.VersePosition Pos)? ResolveMarker(Bible.VersePosition pos,
        List<(int Index, Bible.VersePosition Pos)> markers)
    {
        (int, Bible.VersePosition)? nextVerse = null, chapterStart = null, bookStart = null;
        foreach (var m in markers)
        {
            if (!ReferenceEquals(m.Pos.Book, pos.Book)) continue;
            bookStart ??= m;
            // The beginning of a book (1:1, or the chapter 1 button) shows the book's title too, which sits above
            // chapter 1: the book's first mark is its start. This comes first, as chapter 1 has an exact mark.
            if (pos.Chapter <= 1 && pos.Verse <= 1 && bookStart.Value.Item2.Chapter == 0) return bookStart;
            if (m.Pos.Chapter != pos.Chapter) continue;
            chapterStart ??= m;
            if (m.Pos.Verse == pos.Verse) return m;
            if (m.Pos.Verse > pos.Verse) nextVerse ??= m;
        }
        return nextVerse ?? chapterStart ?? bookStart;
    }

    /// <summary>The first line that reaches the text at <paramref name="index"/>.</summary>
    private PlacedLine? LineAt(int index)
    {
        foreach (var pl in _lines)
            if (pl.Line.FirstTextSourceIndex + pl.Line.Length > index) return pl;
        return null;
    }

    private void ScrollToPosition(Bible.VersePosition? pos)
    {
        if (pos?.Book is null) return;
        if (TryScrollTo(pos)) return;
        if (Books is not { } books) return;

        // Not in the shown text: show the verse's book (and the one after it) and scroll there once laid out.
        int index = -1;
        for (int i = 0; i < books.Count; i++)
            if (ReferenceEquals(books[i], pos.Book)) { index = i; break; }
        if (index < 0) return;
        _pendingPosition = pos;
        _keepPosition = true;
        UpdateBusy();
        if (!_loadingBook) _ = SwitchWindowAsync(books, WindowStartFor(books, index), null);
    }

    private bool TryScrollTo(Bible.VersePosition pos)
    {
        if (!_hasLayout || _dirty || ResolveMarker(pos, _markers) is not { } mark || LineAt(mark.Index) is not { } line) return false;
        _keepPosition = true;
        _programmatic = true;
        try
        {
            if (Orientation == Orientation.Vertical) { if (_vScrolling) SetOffsetY(line.Origin.Y); }
            else if (_scrolling && _pitch > 0) ScrollToColumn((int)Math.Floor((line.Origin.X + 1) / _pitch));
        }
        finally { _programmatic = false; }
        return true;
    }

    /// <summary>Makes <paramref name="column"/> the first visible one: glides when near, jumps when far.</summary>
    private void ScrollToColumn(int column)
    {
        column = Math.Clamp(column, 0, Math.Max(0, (int)_scrollBar.Maximum));
        if (Math.Abs(column - _firstColumn) <= Math.Max(2, _visibleColumns * 2)) { ApplyColumn(column); return; }

        _scrollDirection = Math.Sign(column - _firstColumn);
        StopAnimation();
        _firstColumn = column;
        _offsetX = column * _pitch;
        _updatingBar = true;
        _scrollBar.Value = column;
        _updatingBar = false;
        _popup.IsOpen = false;
        InvalidateArrange();
        _layer.InvalidateVisual();
        RequestWindowCheck(); // the target may be deep in the second book
    }

    // ---- Scrolling across books ---------------------------------------------------------------------
    // With <see cref="Books"/> set, two books are always shown one after the other, separated by a PageBreak.
    // Once the view has passed more than half of the second book, the first book is dropped and the one after
    // the second is appended; scrolling back past half of the first book does the opposite. The scroll position
    // is carried over, so the text just keeps going.

    // Where the next Bible opens (see OpenNextAt); else it opens where the reader was in the Bible it replaces.
    private (int BookIndex, int Chapter, int Verse)? _openAt;

    /// <summary>
    /// The next Bible set (or bound) opens at this book (<c>Book.Index</c>), chapter and verse instead of where the
    /// reader is now, e.g. at the last position of a previous run.
    /// </summary>
    public void OpenNextAt(int bookIndex, int chapter, int verse) => _openAt = (bookIndex, chapter, verse);

    private async void OnBibleChanged(Bible? bible)
    {
        // Where the reader was in the Bible that is replaced: the new one opens there.
        (int BookIndex, int Chapter, int Verse)? previous = _openAt
            ?? (Position is { Book: { } shown } pos ? (shown.Index, pos.Chapter, pos.Verse) : null);
        _openAt = null;
        if (bible is null)
        {
            // No Bible: forget the books and empty the view (a book still loading is dropped when it arrives).
            Books = null;
            _pendingPosition = null;
            Inlines.Clear();
            return;
        }
        try
        {
            if (bible.Books.Count == 0)
            {
                // Reading the folder is work for a worker thread; the books belong to the chosen Bible, not the temporary one.
                var loaded = await Task.Run(() => Bible.OpenFrom(bible.ConfigPath));
                foreach (var book in loaded.Books) book.Bible = bible;
                bible.Books = loaded.Books;
            }
            if (!ReferenceEquals(Bible, bible)) return; // another Bible was chosen while this one loaded
            var books = bible.Books;
            Books = books;
            int index = books.FindIndex(book =>
                !System.IO.Path.GetFileName(book.File ?? string.Empty).StartsWith("00.", StringComparison.Ordinal));
            // Keep the position: the same book (by its number), chapter and verse of the new Bible, when it has them.
            int kept = previous is { } old ? books.FindIndex(book => book.Index == old.BookIndex) : -1;
            await ShowBookAsync(kept >= 0 ? kept : index < 0 ? 0 : index);
            if (kept >= 0 && previous is { } p && (p.Chapter > 0 || p.Verse > 0) && ReferenceEquals(Bible, bible))
                Position = new Bible.VersePosition { Book = books[kept], Chapter = p.Chapter, Verse = p.Verse };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not open {bible.EnglishName}: {ex}");
        }
    }

    /// <summary>The books to scroll through; use <see cref="ShowBookAsync"/> to start at one.</summary>
    public IReadOnlyList<Bible.Book>? Books { get; set; }

    /// <summary>Index in <see cref="Books"/> of the first of the two books shown.</summary>
    public int BookIndex { get; private set; }

    /// <summary>Index in <see cref="Books"/> of the book at the top/left edge of the view.</summary>
    public int CurrentBookIndex => _hasSplit && (Orientation == Orientation.Vertical ? _offsetY >= _splitY - 1 : _firstColumn >= _splitColumn)
        ? BookIndex + 1 : BookIndex;

    /// <summary>The two shown books changed (<see cref="ShowBookAsync"/>, or the window slid on while scrolling).</summary>
    public event EventHandler? BookChanged;

    /// <summary>The user scrolled past the first or last page. Raised whether or not <see cref="Books"/> is set.</summary>
    public event EventHandler<BoundaryEventArgs>? BoundaryReached;

    private bool _loadingBook, _checkQueued, _hasSplit;
    private int _splitIndex = -1, _scrollDirection, _splitColumn, _totalColumns;
    private double _splitY;
    private PageBreak? _bookBreak; // the break between the two books
    private (bool FromSplit, double Value)? _pendingAnchor; // scroll position to restore once the new window is laid out
    private readonly System.Diagnostics.Stopwatch _boundaryClock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastBoundary = TimeSpan.FromSeconds(-10);

    /// <summary>
    /// Shows <c>Books[index]</c> followed by <c>Books[index + 1]</c>, from the start of the first.
    /// Reading and parsing the books happens off the UI thread.
    /// </summary>
    public async Task ShowBookAsync(int index)
    {
        if (Books is not { } books || index < 0 || index >= books.Count || _loadingBook) return;
        int first = WindowStartFor(books, index);
        // The last book has no book after it, so it opens as the second book and is scrolled to.
        if (first != index) _pendingPosition = new Bible.VersePosition { Book = books[index], Chapter = 0, Verse = 0 };
        await SwitchWindowAsync(books, first, null);
    }

    /// <summary>
    /// The first book of the window that shows <paramref name="bookIndex"/> at its start: the book itself, except
    /// for the last book, which gets the one before it so there is always something to scroll back into.
    /// </summary>
    private static int WindowStartFor(IReadOnlyList<Bible.Book> books, int bookIndex) =>
        bookIndex == books.Count - 1 && bookIndex > 0 ? bookIndex - 1 : bookIndex;

    private async Task SwitchWindowAsync(IReadOnlyList<Bible.Book> books, int first, (bool FromSplit, double Value)? anchor)
    {
        _loadingBook = true;
        UpdateBusy();
        try
        {
            // The books' Runs are read and parsed lazily, so ask for them off the UI thread.
            var (a, b) = await Task.Run(() =>
            {
                _ = books[first].Runs; // reading and parsing the markdown is the slow part
                if (first + 1 < books.Count) _ = books[first + 1].Runs;
                return (books[first], first + 1 < books.Count ? books[first + 1] : null);
            });
            // Creating the inlines is UI-thread work: done in slices, so the app keeps answering meanwhile.
            var (content, bookBreak) = await BuildContentAsync(a, b);
            BookIndex = first;
            _pendingAnchor = anchor;
            SetContent(content, bookBreak);
            BookChanged?.Invoke(this, EventArgs.Empty);
            // Have the neighbours ready: the next switch then only has to lay out.
            _ = Task.Run(() =>
            {
                if (first + 2 < books.Count) _ = books[first + 2].Runs;
                if (first > 0) _ = books[first - 1].Runs;
            });
        }
        finally { _loadingBook = false; UpdateBusy(); }
    }

    private void RaiseBoundary(bool atEnd)
    {
        // A flick or a wheel burst raises this many times: act once, then ignore the rest for a moment.
        if (_boundaryClock.Elapsed - _lastBoundary < TimeSpan.FromMilliseconds(700)) return;
        _lastBoundary = _boundaryClock.Elapsed;
        BoundaryReached?.Invoke(this, new BoundaryEventArgs(atEnd));
        _scrollDirection = atEnd ? 1 : -1;
        RequestWindowCheck(); // pushing against the start of the window: bring in the previous book
    }

    /// <summary>Decides, once the scrolling has settled, whether the two-book window must slide.</summary>
    private void RequestWindowCheck()
    {
        if (_checkQueued || Books is null) return;
        _checkQueued = true;
        Dispatcher.UIThread.Post(CheckWindow, DispatcherPriority.Background);
    }

    private void CheckWindow()
    {
        _checkQueued = false;
        if (_loadingBook || _flowRunning is not null || Books is not { } books || !_hasSplit || _panning || _anim is { IsEnabled: true }) return;
        bool vertical = Orientation == Orientation.Vertical;
        double pos = vertical ? _offsetY : _firstColumn;       // where the view starts
        double split = vertical ? _splitY : _splitColumn;      // where the second book starts
        double end = vertical ? _contentHeight : _totalColumns;
        double view = vertical ? _viewHeight : _visibleColumns; // how much of the text is visible at once

        // Forward: the view has passed half of the second book. The view's start can never get beyond
        // (end - view), the last scroll position, and on a wide view a short book ends before half of it is
        // reachable; the last position (the end of the window) is then the trigger, or a short book could
        // never be scrolled past. When everything fits in one view that position is 0: any push forward counts.
        double length = end - split;
        double forwardAt = Math.Min(split + length / 2, Math.Max(0, end - view));
        // Backward: the view has come back into the first half of the first book. A short first book only
        // counts at its very start, so the switch right after a forward slide does not undo itself.
        double backwardBelow = split < 2 * view ? 0.5 : split / 2;

        if (_scrollDirection > 0 && pos >= forwardAt - 0.5 && BookIndex + 2 < books.Count)
            _ = SwitchWindowAsync(books, BookIndex + 1, (false, pos - split));   // first book dropped, next one appended
        else if (_scrollDirection < 0 && pos < backwardBelow && BookIndex > 0)
            _ = SwitchWindowAsync(books, BookIndex - 1, (true, pos));            // previous book prepended
    }

    // ---- Debounced re-layout ------------------------------------------------------------------------
    // Flowing a long text is slow, so while the available size keeps changing (window being dragged) the last
    // layout stays on screen and the real re-flow happens once the size has been stable for a moment.

    /// <summary>How long the size must stay unchanged before the text is flowed again.</summary>
    public static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(150);

    private DispatcherTimer? _resizeTimer;
    private Size _lastAvailable, _lastTotal;
    private bool _hasLayout, _dirty = true, _commitResize;

    /// <summary>Something other than the size changed (text, font, floats…): the next measure must re-flow.</summary>
    private void MarkDirty() { _dirty = true; InvalidateMeasure(); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == FontFamilyProperty
            || change.Property == ForegroundProperty || change.Property == TextAlignmentProperty
            || change.Property == FloatSpacingProperty || change.Property == ColumnCountProperty
            || change.Property == ColumnWidthProperty || change.Property == ColumnSpacingProperty
            || change.Property == OrientationProperty)
            _dirty = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Only a pure size change is deferred; with an unchanged size the measure always runs, because a float
        // child may have invalidated it.
        if (_hasLayout && !_dirty && !_commitResize && availableSize != _lastAvailable)
        {
            // Vertical mode: a height-only change just changes the viewport, no re-flow is needed.
            if (Orientation == Orientation.Vertical && availableSize.Width == _lastAvailable.Width)
            {
                _lastAvailable = availableSize;
                _lastTotal = UpdateVertical(availableSize, _lastTotal.Width);
                _layer.Measure(_lastTotal);
                _layer.InvalidateVisual();
                return _lastTotal;
            }
            // Resizing: keep showing the old layout and flow again when the size settles.
            _resizeTimer ??= new DispatcherTimer(ResizeDelay, DispatcherPriority.Background, (_, _) =>
            {
                _resizeTimer!.Stop();
                _commitResize = true;
                InvalidateMeasure();
            });
            _resizeTimer.Stop();
            _resizeTimer.Start();
            return _lastTotal;
        }
        _resizeTimer?.Stop();
        _commitResize = false;
        bool wasDirty = _dirty;
        _dirty = false;
        _lastAvailable = availableSize;
        var result = MeasureCore(availableSize, out bool awaiting);
        if (awaiting)
        {
            // The lines are being flowed on a worker thread: keep what is shown (nothing was changed) and measure
            // again when they arrive.
            _dirty = wasDirty;
            return result;
        }
        _lastTotal = result;
        _hasLayout = true;
        return result;
    }

    private Size MeasureCore(Size availableSize, out bool awaiting)
    {
        awaiting = false;
        bool widthFinite = !double.IsInfinity(availableSize.Width);
        bool heightFinite = !double.IsInfinity(availableSize.Height);
        double totalWidth = widthFinite ? availableSize.Width : 400;
        double spacing = ColumnSpacing;
        bool vertical = Orientation == Orientation.Vertical;
        bool fixedWidth = !vertical && !double.IsNaN(ColumnWidth) && ColumnWidth > 0;
        bool unbounded = false; // true: add as many columns as the text needs
        int columns;
        double width;
        if (fixedWidth)
        {
            width = widthFinite ? Math.Min(ColumnWidth, Math.Max(1, availableSize.Width)) : ColumnWidth;
            if (heightFinite) { unbounded = true; columns = int.MaxValue; }
            else if (ColumnCount > 0) columns = ColumnCount;
            else columns = widthFinite ? Math.Max(1, (int)((availableSize.Width + spacing) / (width + spacing))) : 1;
        }
        else
        {
            columns = vertical ? 1 : Math.Max(1, ColumnCount);
            width = Math.Max(1, (totalWidth - (columns - 1) * spacing) / columns);
        }
        // The text is first flowed as ONE tall column of this width; it is cut into columns afterwards.
        bool cutting = columns > 1 || unbounded; // false: one column, a page break is just a gap
        double gap = FloatSpacing;

        // 1. Measure floats. Each one is anchored at the text index where it was declared and gets placed
        //    while flowing, on the line that contains that index (like CSS float).
        var (text, segments, anchors, notes, breaks, splitIndex, markers) = Flatten();
        var pending = new List<(Control Child, Size Size, FloatSide Side, int Anchor)>();
        foreach (var child in Children.OfType<Control>().Where(c => c != _layer && c != _popup && c != _scrollBar && c != _vBar && c != _tail))
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            pending.Add((child, child.DesiredSize, GetSide(child), anchors.TryGetValue(child, out var a) ? a : 0));
        }
        pending = pending.OrderBy(p => p.Anchor).ToList(); // stable: keeps declaration order for equal anchors

        // 2. Flow the text line by line, narrowing each line by the floats it overlaps (RunFlow). A long text is
        //    flowed on a worker thread: the first screens are shown as soon as they are ready, the rest follows.
        bool justify = TextAlignment == Avalonia.Media.TextAlignment.Justify;
        var style = BaseStyle();
        var input = new FlowInput(text, segments, pending, breaks, width, gap, cutting, FontSize, style, justify);
        var segmentHash = new HashCode();
        foreach (var s in segments) { segmentHash.Add(s.Start); segmentHash.Add(s.End); segmentHash.Add(s.Style); }
        var floatHash = new HashCode();
        foreach (var p in pending) { floatHash.Add(p.Child); floatHash.Add(p.Size); floatHash.Add(p.Side); floatHash.Add(p.Anchor); }
        var key = new FlowKey(width, cutting, FontSize, FontFamily, style.Foreground, justify, gap,
            text.GetHashCode(), text.Length, segmentHash.ToHashCode(), floatHash.ToHashCode());

        // The same text flowed again in another width or orientation (window resized, device rotated): the reader
        // stays at the line at the top of the view, not at the same column or offset of a different layout.
        int? reflowKeep = null;
        if (_hasLayout && _pendingAnchor is null && _pendingPosition is null && _lines.Count > 0
            && (_appliedVertical != vertical || Math.Abs(_appliedWidth - width) > 0.5) && string.Equals(_text, text))
            reflowKeep = VisibleStartIndex();

        int partialLines = 0, partialFrom = 0;
        if (_pendingPosition is not null || reflowKeep is not null || (_lines.Count == 0 && _pendingAnchor is null))
        {
            if (reflowKeep is { } from) partialFrom = from;
            // Nothing is shown yet, or a verse is wanted: flow only down to it and a few screens beyond, and show that
            // right away; the rest follows.
            if (_pendingPosition is { } wantedAt && ResolveMarker(wantedAt, markers) is { } wantedMark)
                partialFrom = wantedMark.Index;
            int screen = heightFinite ? (int)(availableSize.Height / Math.Max(1, FontSize * 1.25)) + 1 : 60;
            int visibleColumns = vertical || !widthFinite ? 1
                : fixedWidth ? Math.Max(1, (int)((availableSize.Width + spacing) / (width + spacing))) : columns;
            partialLines = Math.Clamp(screen * visibleColumns * 3, 120, 3000);
        }
        var flow = GetFlow(key, input, partialLines, partialFrom);
        if (flow is null)
        {
            awaiting = true;
            return _hasLayout ? _lastTotal : new Size(totalWidth, heightFinite ? availableSize.Height : 0);
        }

        // 3. The lines are ready: only now is the control's state replaced, all at once, with what belongs together.
        bool partial = !flow.Complete;

        int? keepIndex = reflowKeep;
        _appliedVertical = vertical;
        _appliedWidth = width;
        _breakStarts = breaks;
        _splitIndex = splitIndex;
        _markers = markers;
        _notes = notes;
        _text = text;
        _anchor = Math.Min(_anchor, text.Length);
        _caret = Math.Min(_caret, text.Length);
        _floatRects.Clear();
        _floatRects.AddRange(flow.FloatRects);
        _lines.Clear();
        _lines.AddRange(flow.Lines);
        var placed = flow.Placed.ToList();
        double textBottom = flow.TextBottom;
        if (partial)
        {
            // Only the first lines are flowed so far: the height of the rest is estimated from them, and the floats
            // of the text that is not flowed yet wait outside the view.
            textBottom = flow.EndIndex > 0 ? flow.TextBottom * text.Length / flow.EndIndex : flow.TextBottom;
            var done = placed.Select(p => p.Child).ToHashSet();
            foreach (var f in pending.Where(u => !done.Contains(u.Child)))
                placed.Add((f.Child, new Rect(-100000 - f.Size.Width, 0, f.Size.Width, f.Size.Height)));
        }
        _arranged = placed;

        double floatsBottom = _floatRects.Count == 0 ? 0 : _floatRects.Max(r => r.Bottom - gap);
        double flowHeight = Math.Max(textBottom, floatsBottom);

        // 3. Cut the tall column into `columns` columns.
        double pitch = width + spacing;
        // With a fixed column width and a bounded size only the columns that fit are shown; the rest are
        // reached with the scroll bar, one whole column at a time.
        bool paged = fixedWidth && widthFinite && heightFinite;
        int visible = paged ? Math.Max(1, (int)((availableSize.Width + spacing) / pitch)) : int.MaxValue;

        var flowLines = _lines.ToList();
        var flowArranged = _arranged.ToList();

        (int Used, double Height) Cut(double columnHeight)
        {
            _lines.Clear();
            _lines.AddRange(flowLines);
            _arranged = flowArranged.ToList();
            if (!((columns > 1 || unbounded) && _lines.Count > 0)) return (1, flowHeight);

            double step = _lines.Min(l => l.Line.Height);
            // Finite height: newspaper-style, fill column 1 first. Infinite height: balance the columns.
            double h = Math.Max(columnHeight, step);
            List<double>? starts;
            while ((starts = SplitColumns(h, columns)) is null) h += step;
            int used = starts.Count(x => x < double.MaxValue);

            double Top(int col) => starts[col];
            int ColumnAt(double y)
            {
                int c = 0;
                for (int i = 1; i < starts.Count; i++) if (starts[i] <= y) c = i;
                return c;
            }

            double height = 0;
            var columned = new List<PlacedLine>(_lines.Count);
            foreach (var pl in _lines)
            {
                int c = ColumnAt(pl.Origin.Y);
                var origin = new Point(c * pitch + pl.Origin.X, pl.Origin.Y - Top(c));
                columned.Add(pl with { Origin = origin });
                height = Math.Max(height, origin.Y + pl.Line.Height);
            }
            _lines.Clear();
            _lines.AddRange(columned);

            for (int i = 0; i < _arranged.Count; i++)
            {
                var (child, slot) = _arranged[i];
                int c = ColumnAt(slot.Y);
                slot = new Rect(c * pitch + slot.X, slot.Y - Top(c), slot.Width, slot.Height);
                _arranged[i] = (child, slot);
                height = Math.Max(height, slot.Bottom);
            }
            return (used, height);
        }

        var (usedColumns, contentHeight) = Cut(heightFinite ? availableSize.Height : flowHeight / columns);
        double barHeight = 0;
        if (paged && usedColumns > visible)
        {
            // The scroll bar takes room from the bottom, so the columns are cut again at a lower height.
            _scrollBar.IsVisible = true; // a hidden control measures to zero
            _scrollBar.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            barHeight = Math.Max(_scrollBar.DesiredSize.Height, 12);
            (usedColumns, contentHeight) = Cut(Math.Max(1, availableSize.Height - barHeight));
        }

        _scrolling = paged && usedColumns > visible;
        _pitch = pitch;
        _barHeight = barHeight;
        // Where the second book starts (the first line after the book break), for the two-book window.
        _hasSplit = false;
        if (_splitIndex >= 0)
        {
            foreach (var pl in _lines)
            {
                if (pl.Line.FirstTextSourceIndex < _splitIndex) continue;
                _splitColumn = (int)Math.Floor((pl.Origin.X + 1) / pitch);
                _splitY = pl.Origin.Y;
                _hasSplit = true;
                break;
            }
        }
        if (partial) _hasSplit = false; // the second book is not flowed yet

        // While only the first lines are flowed, a spinner at the end of them shows that the rest is coming.
        _tailRect = null;
        if (partial && _lines.Count > 0)
        {
            var last = _lines[^1];
            double columnLeft = Math.Floor((last.Origin.X + 1) / pitch) * pitch;
            _tailRect = new Rect(columnLeft + (width - TailSize) / 2, last.Origin.Y + last.Line.Height + TailSize / 2, TailSize, TailSize);
        }
        if (_tail.IsVisible != (_tailRect is not null)) _tail.IsVisible = _tailRect is not null; // before measuring: hidden = zero size
        if (_tailRect is not null) _tail.Measure(new Size(TailSize, TailSize));
        _totalColumns = usedColumns;
        if (_pendingPosition is { } wanted && _markers.Count > 0)
        {
            // Content without the wanted book (a layout before the books arrived) must not use up the request.
            if (ResolveMarker(wanted, _markers) is { } found && LineAt(found.Index) is { } target)
            {
                _pendingPosition = null;
                _pendingAnchor = (false, vertical ? target.Origin.Y : Math.Floor((target.Origin.X + 1) / pitch));
            }
            else if (!partial && !_loadingBook) _pendingPosition = null; // a partial layout may simply not reach it yet
        }
        UpdateBusy();
        if (keepIndex is { } keep && _pendingAnchor is null && LineAt(keep) is { } keepLine)
            _pendingAnchor = (false, vertical ? keepLine.Origin.Y : Math.Floor((keepLine.Origin.X + 1) / pitch));
        if (_pendingAnchor is { } anchor && !vertical)
        {
            _firstColumn = (int)Math.Round((anchor.FromSplit ? _splitColumn : 0) + anchor.Value);
            _pendingAnchor = null;
        }
        _firstColumn = _scrolling ? Math.Clamp(_firstColumn, 0, usedColumns - visible) : 0;
        _visibleColumns = _scrolling ? visible : usedColumns;
        // An animation in progress keeps its current offset (it heads for the new target by itself).
        if (!_scrolling) StopAnimation();
        if (_anim is not { IsEnabled: true } && !_panning) _offsetX = _firstColumn * pitch;

        double contentWidth = fixedWidth ? _visibleColumns * width + (_visibleColumns - 1) * spacing : totalWidth;
        _viewWidth = contentWidth;
        if (_scrollBar.IsVisible != _scrolling) _scrollBar.IsVisible = _scrolling;
        if (_scrolling)
        {
            _updatingBar = true;
            _scrollBar.Minimum = 0;
            _scrollBar.Maximum = usedColumns - visible;
            _scrollBar.ViewportSize = visible;
            _scrollBar.LargeChange = visible;
            _scrollBar.SmallChange = 1;
            _scrollBar.Value = _firstColumn;
            _updatingBar = false;
            _scrollBar.Measure(new Size(contentWidth, barHeight));
        }

        _contentHeight = contentHeight;
        Size total;
        if (vertical)
        {
            if (_scrollBar.IsVisible) _scrollBar.IsVisible = false;
            total = UpdateVertical(availableSize, contentWidth);
        }
        else
        {
            _vScrolling = false;
            _offsetY = 0;
            if (_vBar.IsVisible) _vBar.IsVisible = false;
            // Scrolling fills the given height exactly, with the scroll bar along the bottom edge.
            total = new Size(contentWidth, _scrolling ? availableSize.Height : contentHeight);
        }
        _layer.Measure(total);
        _layer.InvalidateVisual();
        RequestPositionUpdate();
        return total;
    }

    // ---- Flowing the lines (on a worker thread for a long text) ---------------------------------------
    // Flowing two books takes seconds on a phone, and the UI thread must not be blocked that long (the system
    // reports "not responding"). RunFlow only works on the values in a FlowInput: Avalonia objects may only be
    // used on the UI thread, so everything it needs is read before it starts.

    private sealed record FlowInput(string Text, List<Segment> Segments,
        List<(Control Child, Size Size, FloatSide Side, int Anchor)> Pending, HashSet<int> Breaks,
        double Width, double Gap, bool Cutting, double FontSize, RunStyle Base, bool Justify);

    /// <param name="EndIndex">How much of the text is flowed; the text's length when <paramref name="Complete"/>.</param>
    private sealed record FlowResult(List<PlacedLine> Lines, List<(Control Child, Rect Slot)> Placed,
        List<Rect> FloatRects, double TextBottom, int EndIndex, bool Complete);

    /// <summary>Everything that decides how the lines come out; equal keys give the same lines.</summary>
    private readonly record struct FlowKey(double Width, bool Cutting, double FontSize, FontFamily Family, IBrush? Foreground,
        bool Justify, double Gap, int TextHash, int TextLength, int SegmentHash, int FloatHash);

    /// <summary>Texts shorter than this are flowed right away, on the UI thread.</summary>
    private const int AsyncFlowChars = 40_000;

    private (FlowKey Key, FlowResult Result)? _flowCache;
    private FlowKey? _flowRunning; // set while a worker has not delivered the complete result yet
    private CancellationTokenSource? _flowCts;
    private bool _flowFailed;      // flowing on a worker did not work on this platform: flow on the UI thread

    /// <summary>
    /// The lines for <paramref name="key"/>, or null while a worker is still busy with them (measure is repeated
    /// when it delivers). <paramref name="partialLines"/> &gt; 0 also delivers the lines early: down to the text index
    /// <paramref name="partialFrom"/> and that many lines beyond it.
    /// </summary>
    private FlowResult? GetFlow(FlowKey key, FlowInput input, int partialLines, int partialFrom)
    {
        if (_flowCache is { } cached && cached.Key.Equals(key)) return cached.Result;
        if (_flowRunning is { } running && running.Equals(key)) return null;

        _flowCts?.Cancel(); // the input changed: whatever is still being flowed is out of date
        _flowCache = null;
        _flowRunning = null;
        if (input.Text.Length < AsyncFlowChars || _flowFailed)
            return RunFlow(input, int.MaxValue, CancellationToken.None);

        var cts = _flowCts = new CancellationTokenSource();
        var token = cts.Token;
        _flowRunning = key;
        _ = Task.Run(() =>
        {
            try
            {
                if (partialLines > 0)
                {
                    var first = RunFlow(input, partialLines, token, partialFrom);
                    Dispatcher.UIThread.Post(() => FlowDelivered(key, first, token));
                    if (first.Complete) return;
                }
                var all = RunFlow(input, int.MaxValue, token);
                Dispatcher.UIThread.Post(() => FlowDelivered(key, all, token));
            }
            catch (OperationCanceledException) { }
            catch
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _flowFailed = true; // e.g. a platform whose text stack is not usable from other threads
                    _flowRunning = null;
                    InvalidateMeasure();
                });
            }
        });
        return null;
    }

    private void FlowDelivered(FlowKey key, FlowResult result, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        _flowCache = (key, result);
        if (result.Complete) _flowRunning = null;
        InvalidateMeasure();
    }

    /// <summary>
    /// Flows the text into one tall column, line by line, narrowing each line by the floats it overlaps. Stops after
    /// <paramref name="maxLines"/> lines beyond the text index <paramref name="minIndex"/> (an incomplete result).
    /// Touches no Avalonia object, so it can run anywhere.
    /// </summary>
    private static FlowResult RunFlow(FlowInput input, int maxLines, CancellationToken cancel, int minIndex = 0)
    {
        string text = input.Text;
        double width = input.Width, gap = input.Gap;
        var pending = input.Pending.ToList(); // floats not placed yet, in declaration order
        var lines = new List<PlacedLine>();
        var floatRects = new List<Rect>();
        var placed = new List<(Control Child, Rect Slot)>();
        double leftBottom = 0, rightBottom = 0; // floats on one side stack; the gap is included
        double textBottom = 0, top = 0;
        int index = 0;
        bool complete = true;

        void PlaceFloat((Control Child, Size Size, FloatSide Side, int Anchor) f, double lineTop)
        {
            bool isLeft = f.Side == FloatSide.Left;
            double y = Math.Max(lineTop, isLeft ? leftBottom : rightBottom);
            double x = isLeft ? 0 : width - f.Size.Width;
            placed.Add((f.Child, new Rect(x, y, f.Size.Width, f.Size.Height)));
            // Rect used for text avoidance: grown by the gap on the text-facing edges.
            floatRects.Add(isLeft
                ? new Rect(x, y, f.Size.Width + gap, f.Size.Height + gap)
                : new Rect(x - gap, y, f.Size.Width + gap, f.Size.Height + gap));
            if (isLeft) leftBottom = y + f.Size.Height + gap; else rightBottom = y + f.Size.Height + gap;
        }

        // Space taken from the left and right of a line occupying [lineTop, lineTop + height).
        (double Left, double Right) Insets(double lineTop, double height)
        {
            double left = 0, right = 0;
            foreach (var r in floatRects)
            {
                if (r.Bottom <= lineTop || r.Top >= lineTop + height) continue;
                if (r.X <= 0.5) left = Math.Max(left, r.Right);
                else right = Math.Max(right, width - r.X);
            }
            return (left, right);
        }

        if (text.Length > 0)
        {
            var baseProps = ToRunProperties(input.Base);
            var paraProps = new GenericTextParagraphProperties(FlowDirection.LeftToRight, Avalonia.Media.TextAlignment.Left,
                true, false, baseProps, TextWrapping.Wrap, 0, 0, 0);
            var source = new RichTextSource(text, input.Segments);
            var formatter = TextFormatter.Current;

            // Formats the line starting at `from` from a window of text just past where it can end (see
            // RichTextSource.Limit). A line that ends close to the window's edge is formatted again with a
            // bigger window, so the break is always decided with enough text after it.
            TextLine? Format(int from, double lineWidth)
            {
                int window = Math.Max(200, (int)(lineWidth / Math.Max(1, input.FontSize * 0.25)) * 2);
                while (true)
                {
                    source.Limit = Math.Min(text.Length, from + window);
                    var l = formatter.FormatLine(source, from, lineWidth, paraProps);
                    if (l is null || source.Limit >= text.Length || from + l.Length <= source.Limit - 80)
                    {
                        source.Limit = int.MaxValue;
                        return l;
                    }
                    window *= 2;
                }
            }

            int gapIndex = -1, stopAfter = int.MaxValue;
            while (index < text.Length)
            {
                cancel.ThrowIfCancellationRequested();
                // Stops maxLines lines after the text index minIndex has been reached (at the start: after maxLines lines).
                if (stopAfter == int.MaxValue && maxLines != int.MaxValue && index >= minIndex) stopAfter = lines.Count + maxLines;
                if (lines.Count >= stopAfter) { complete = false; break; }

                // Probe with full width to learn the line height, then recompute with the real band.
                var probe = Format(index, width);
                if (probe is null) break;
                if (!input.Cutting && top > 0 && gapIndex != index && input.Breaks.Contains(index))
                {
                    top += probe.Height * 2; // no next column to break to: leave a visible gap instead
                    gapIndex = index;
                }

                var (left, right) = Insets(top, probe.Height);
                double lineWidth = width - left - right;

                // Float fills the row: drop below the next float edge instead of squeezing text.
                // Only skip when a float is actually responsible; otherwise (narrow window) the text must
                // be squeezed into whatever width exists, or the loop would never consume any text.
                if (lineWidth < input.FontSize * 3 && (left > 0 || right > 0))
                {
                    var blocking = floatRects.Where(r => r.Bottom > top && r.Top < top + probe.Height).Select(r => r.Bottom).ToList();
                    if (blocking.Count > 0)
                    {
                        top = Math.Max(blocking.Min(), top + 1);
                        continue;
                    }
                }
                lineWidth = Math.Max(lineWidth, 1);

                // No float beside this line: the probe is already the line.
                var line = lineWidth == width ? probe : Format(index, lineWidth);
                if (line is null || line.Length == 0) break;

                // Floats declared inside this line start here: place them at this line's top, then lay the
                // line out again, now narrowed by them. (Floats are removed from `pending`, so this happens once.)
                int lineEnd = index + line.Length;
                bool lastLine = lineEnd >= text.Length;
                if (pending.Count > 0 && (pending[0].Anchor < lineEnd || lastLine))
                {
                    while (pending.Count > 0 && (pending[0].Anchor < lineEnd || lastLine))
                    {
                        PlaceFloat(pending[0], top);
                        pending.RemoveAt(0);
                    }
                    continue;
                }

                if (input.Justify && !lastLine && line.NewLineLength == 0)
                    line = JustifyLine(line, index, text, input.Segments, lineWidth, paraProps) ?? line;

                lines.Add(new PlacedLine(line, new Point(left, top), input.Breaks.Contains(index)));
                top += line.Height;
                index = lineEnd;
                textBottom = top;
            }
        }

        // Floats with no text to attach to (empty text) go at the top.
        if (complete) foreach (var f in pending) PlaceFloat(f, top);
        return new FlowResult(lines, placed, floatRects, textBottom, index, complete);
    }

    // ---- Vertical scrolling -------------------------------------------------------------------------

    private readonly ScrollBar _vBar;
    private bool _vScrolling;
    private double _offsetY, _viewHeight, _contentHeight, _barWidth;

    /// <summary>Sizes the viewport of the single column and configures the vertical scroll bar.</summary>
    private Size UpdateVertical(Size available, double width)
    {
        bool heightFinite = !double.IsInfinity(available.Height);
        _vScrolling = heightFinite && _contentHeight > available.Height + 0.5;
        _viewHeight = _vScrolling ? available.Height : _contentHeight;
        if (_pendingAnchor is { } anchor)
        {
            _offsetY = (anchor.FromSplit ? _splitY : 0) + anchor.Value;
            _pendingAnchor = null;
        }
        _offsetY = _vScrolling ? Math.Clamp(_offsetY, 0, _contentHeight - _viewHeight) : 0;
        if (_vBar.IsVisible != _vScrolling) _vBar.IsVisible = _vScrolling; // visible before measuring: hidden = zero size
        if (_vScrolling)
        {
            _updatingBar = true;
            _vBar.Minimum = 0;
            _vBar.Maximum = _contentHeight - _viewHeight;
            _vBar.ViewportSize = _viewHeight;
            _vBar.LargeChange = _viewHeight * 0.9;
            _vBar.SmallChange = FontSize * 2.5;
            _vBar.Value = _offsetY;
            _updatingBar = false;
            _vBar.Measure(new Size(double.PositiveInfinity, _viewHeight));
            _barWidth = Math.Max(_vBar.DesiredSize.Width, 8);
        }
        return new Size(width, _viewHeight);
    }

    private void SetOffsetY(double y)
    {
        y = Math.Clamp(y, 0, Math.Max(0, _contentHeight - _viewHeight));
        if (Math.Abs(y - _offsetY) < 0.01) return;
        _scrollDirection = Math.Sign(y - _offsetY);
        if (!_programmatic) _keepPosition = false;
        _offsetY = y;
        if (Math.Abs(_vBar.Value - y) > 0.01)
        {
            _updatingBar = true;
            _vBar.Value = y;
            _updatingBar = false;
        }
        _popup.IsOpen = false;
        InvalidateArrange(); // floats follow the offset
        _layer.InvalidateVisual();
        RequestWindowCheck();
        RequestPositionUpdate();
    }

    // Touch panning: the scroll gesture recognizer turns a finger drag (and its inertia afterwards) into deltas.
    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        if (_vScrolling)
        {
            double max = Math.Max(0, _contentHeight - _viewHeight);
            double target = _offsetY + e.Delta.Y;
            bool beyond = (target > max + 0.5 && _offsetY >= max - 0.5) || (target < -0.5 && _offsetY <= 0.5);
            if (beyond)
            {
                _overscroll += e.Delta.Y; // keep pushing against the edge to move on to the next/previous book
                if (Math.Abs(_overscroll) > 100)
                {
                    RaiseBoundary(atEnd: _overscroll > 0);
                    _overscroll = 0;
                }
            }
            else _overscroll = 0;
            SetOffsetY(target);
            e.Handled = true;
        }
        else if (_scrolling && _touchDown)
        {
            // Horizontal mode: the columns follow the finger; they settle on a whole column when it lifts.
            StopAnimation();
            _panning = true;
            double max = Math.Max(0, _scrollBar.Maximum * _pitch);
            // A little rubber band past both ends: letting go there moves on to the neighbouring book.
            _offsetX = Math.Clamp(_offsetX + e.Delta.X, -0.4 * _pitch, max + 0.4 * _pitch);
            _popup.IsOpen = false;
            InvalidateArrange();
            _layer.InvalidateVisual();
            e.Handled = true;
        }
    }

    private bool _touchDown, _panning;
    private double _overscroll;

    /// <summary>Finger lifted while panning: choose the column to settle on (a short flick is enough).</summary>
    private void OnScrollInertiaStarting(object? sender, ScrollGestureInertiaStartingEventArgs e)
    {
        // Vertical mode: Avalonia's own inertia runs on (the same as in a ScrollViewer): the gesture events it
        // raises carry on arriving in OnScrollGesture, so a fast swipe travels further than a slow one.
        if (!_panning) return;
        SettlePan(e.Inertia.X);
        e.Handled = true; // no free-running inertia: the columns snap
    }

    private void OnScrollGestureEnded(object? sender, ScrollGestureEndedEventArgs e)
    {
        if (_panning) SettlePan(0);
    }

    private void SettlePan(double velocityX)
    {
        _panning = false;
        double moved = _offsetX / _pitch - _firstColumn; // columns dragged, signed (positive = towards later columns)
        int sign = Math.Sign(moved);
        bool flick = sign != 0 && Math.Sign(velocityX) == sign;
        double threshold = flick ? 0.1 : 0.35;
        int target = sign == 0 ? _firstColumn : _firstColumn + sign * (int)Math.Round(Math.Abs(moved) + 0.5 - threshold);
        ApplyColumn(target, force: true);
    }

    // A tap (touch) opens a footnote; for touch the press can't, because it may be the start of a pan.
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (e.Pointer?.Type != PointerType.Touch) return;
        var position = e.GetPosition(this);
        if (OverScrollBar(position)) return;
        if (HitFootnoteNear(ToContent(position), out var bounds) is { } note)
        {
            bounds = bounds.Translate(new Vector(-_offsetX, -_offsetY));
            e.Handled = true;
            Dispatcher.UIThread.Post(() => ShowFootnote(note, bounds), DispatcherPriority.Background);
        }
    }

    // ---- Column scrolling ---------------------------------------------------------------------------

    private readonly ScrollBar _scrollBar;
    private bool _scrolling, _updatingBar;
    private int _firstColumn, _visibleColumns;
    private double _pitch, _offsetX, _viewWidth, _barHeight;

    /// <summary>Shows column <paramref name="column"/> as the first visible one (snapped to a whole column).</summary>
    private void ApplyColumn(int column, bool force = false)
    {
        if (_scrolling)
        {
            int last = Math.Max(0, (int)_scrollBar.Maximum);
            if (column > last) RaiseBoundary(atEnd: true); // asked for a page after the last one
            else if (column < 0) RaiseBoundary(atEnd: false);
            column = Math.Clamp(column, 0, last);
        }
        if (Math.Abs(_scrollBar.Value - column) > 1e-6)
        {
            _updatingBar = true;
            _scrollBar.Value = column;
            _updatingBar = false;
        }
        if (column == _firstColumn && !force) return;
        if (!_programmatic) _keepPosition = false; // the user took over
        if (column != _firstColumn) _scrollDirection = Math.Sign(column - _firstColumn);
        _firstColumn = column;
        RequestPositionUpdate();
        _popup.IsOpen = false;
        AnimateScroll();
    }

    // Soft scrolling: the offset eases toward the target column instead of jumping. Each new target (wheel
    // notches in quick succession, thumb dragging) restarts the ease from wherever the offset currently is.
    private static readonly TimeSpan ScrollDuration = TimeSpan.FromMilliseconds(280);
    private DispatcherTimer? _anim;
    private readonly System.Diagnostics.Stopwatch _animClock = new();
    private double _animFrom;

    private void AnimateScroll()
    {
        _animFrom = _offsetX;
        _animClock.Restart();
        _anim ??= new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => AnimationTick());
        _anim.Start();
    }

    private void AnimationTick()
    {
        double target = _firstColumn * _pitch;
        double t = Math.Min(1, _animClock.Elapsed / ScrollDuration);
        double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        _offsetX = _animFrom + (target - _animFrom) * eased;
        if (t >= 1) { _offsetX = target; _anim?.Stop(); RequestWindowCheck(); }
        InvalidateArrange(); // floats follow the offset
        _layer.InvalidateVisual();
    }

    private void StopAnimation()
    {
        _anim?.Stop();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (_vScrolling && e.Delta.Y != 0)
        {
            double max = Math.Max(0, _contentHeight - _viewHeight);
            double target = _offsetY - e.Delta.Y * FontSize * 3;
            if (target > max + 0.5 && _offsetY >= max - 0.5) RaiseBoundary(atEnd: true);
            else if (target < -0.5 && _offsetY <= 0.5) RaiseBoundary(atEnd: false);
            SetOffsetY(target);
            e.Handled = true;
            return;
        }
        if (delta == 0) return;
        if (!_scrolling)
        {
            if (!_vScrolling) RaiseBoundary(atEnd: delta < 0); // everything fits: any wheel turn is "past the edge"
            return;
        }
        ApplyColumn(_firstColumn + (delta < 0 ? 1 : -1));
        e.Handled = true;
    }

    /// <summary>Control coordinates → coordinates of the laid-out columns (which are all positioned side by side).</summary>
    private Point ToContent(Point point) => new(point.X + _offsetX, point.Y + _offsetY);

    /// <summary>False for lines in columns scrolled out of view, so they are not drawn.</summary>
    private bool IsInView(PlacedLine pl) =>
        _vScrolling ? pl.Origin.Y + pl.Line.Height > _offsetY && pl.Origin.Y < _offsetY + _viewHeight
        : !_scrolling || (pl.Origin.X + pl.Line.WidthIncludingTrailingWhitespace > _offsetX && pl.Origin.X < _offsetX + _viewWidth);

    /// <summary>Arrow, page and Home/End keys. Going on past the first or last page continues in the next/previous book.</summary>
    private bool NavigateByKey(Key key)
    {
        int direction = key switch
        {
            Key.Right or Key.Down or Key.PageDown or Key.Space => 1,
            Key.Left or Key.Up or Key.PageUp => -1,
            Key.Home => -2,
            Key.End => 2,
            _ => 0,
        };
        if (direction == 0) return false;
        bool page = key is Key.PageDown or Key.PageUp or Key.Space;

        if (_vScrolling)
        {
            double max = Math.Max(0, _contentHeight - _viewHeight);
            double step = page ? _viewHeight * 0.9 : FontSize * 3;
            double target = direction switch { -2 => 0, 2 => max, _ => _offsetY + direction * step };
            if (Math.Abs(direction) == 1)
            {
                if (target > max + 0.5 && _offsetY >= max - 0.5) RaiseBoundary(atEnd: true);
                else if (target < -0.5 && _offsetY <= 0.5) RaiseBoundary(atEnd: false);
            }
            SetOffsetY(target);
        }
        else if (_scrolling)
        {
            int last = Math.Max(0, (int)_scrollBar.Maximum);
            int step = page ? Math.Max(1, _visibleColumns) : 1;
            ApplyColumn(direction switch { -2 => 0, 2 => last, _ => _firstColumn + direction * step });
        }
        else if (Math.Abs(direction) == 1)
        {
            RaiseBoundary(atEnd: direction > 0); // everything fits on one page
        }
        return true;
    }

    private bool OverScrollBar(Point point) =>
        (_scrollBar.IsVisible && point.Y >= _scrollBar.Bounds.Top) || (_vBar.IsVisible && point.X >= _vBar.Bounds.Left);

    /// <summary>
    /// Greedily breaks the flowed lines into at most <paramref name="columns"/> columns of height
    /// <paramref name="height"/>. Returns each column's start offset in flow coordinates, or null if it doesn't fit.
    /// </summary>
    private List<double>? SplitColumns(double height, int columns)
    {
        var starts = new List<double> { 0 };
        foreach (var pl in _lines)
        {
            if (pl.PageStart)
            {
                // A page break puts this line at the top of the next column.
                if (pl.Origin.Y <= starts[^1] + 0.01) continue; // it already is the first line of its column
                // With a fixed number of columns a break that has no room is ignored (a taller column can't help).
                if (starts.Count < columns)
                {
                    starts.Add(pl.Origin.Y);
                    continue;
                }
            }
            if (pl.Origin.Y + pl.Line.Height - starts[^1] <= height + 0.01) continue;
            if (starts.Count == columns) return null;
            starts.Add(pl.Origin.Y);
        }
        if (columns < int.MaxValue)
            while (starts.Count < columns) starts.Add(double.MaxValue); // unused columns
        return starts;
    }

    /// <summary>
    /// Justifies by hand. Avalonia's own TextLine.Justify spreads the extra space over text-run boundaries
    /// instead of over the spaces (visible as a gap between a word and its footnote marker, or around any
    /// differently styled run). Instead the line is formatted again with every inner space replaced by an
    /// invisible <see cref="GapRun"/> that is wider by an equal share of the leftover width.
    /// Each gap is one character, so text indices (selection, hit testing, footnotes) stay valid.
    /// </summary>
    private static TextLine? JustifyLine(TextLine line, int start, string text, List<Segment> segments,
        double width, TextParagraphProperties paragraph)
    {
        int end = start + line.Length - line.NewLineLength;
        while (end > start && text[end - 1] == ' ') end--; // trailing spaces keep their natural width

        var spaces = new Dictionary<int, double>();
        for (int i = start; i < end; i++)
        {
            if (text[i] != ' ') continue;
            var bounds = line.GetTextBounds(i, 1);
            if (bounds.Count == 0) return null;
            spaces[i] = bounds[0].Rectangle.Width;
        }
        if (spaces.Count == 0) return null;

        double extra = (width - line.Width) / spaces.Count;
        if (extra < 0.01) return null;

        var source = new JustifiedSource(text, segments, start + line.Length, spaces, extra, line.Height, line.Baseline);
        var justified = TextFormatter.Current.FormatLine(source, start, 100_000, paragraph);
        return justified is not null && justified.Length == line.Length ? justified : null;
    }

    /// <summary>An invisible, fixed-width stand-in for one space character.</summary>
    private sealed class GapRun(double width, double height, double baseline, TextRunProperties properties) : DrawableTextRun
    {
        public override TextRunProperties? Properties => properties; // required: Avalonia reads BaselineAlignment from it
        public override int Length => 1;
        public override Size Size => new(width, height);
        public override double Baseline => baseline;
        public override void Draw(DrawingContext drawingContext, Point origin) { }
    }

    /// <summary>Like <see cref="RichTextSource"/> for one line, with its inner spaces widened.</summary>
    private sealed class JustifiedSource(string text, List<Segment> segments, int lineEnd,
        Dictionary<int, double> spaces, double extra, double height, double baseline) : ITextSource
    {
        public TextRun? GetTextRun(int index)
        {
            if (index >= lineEnd) return null;
            if (spaces.TryGetValue(index, out double natural))
            {
                var style = segments[FindSegment(segments, index)].Style with { Baseline = BaselineAlignment.Baseline };
                return new GapRun(natural + extra, height, baseline, ToRunProperties(style));
            }

            int found = FindSegment(segments, index);
            if (found < 0) return null;
            var seg = segments[found];
            int stop = Math.Min(seg.End, lineEnd);
            for (int i = index; i < stop; i++)
                if (spaces.ContainsKey(i)) { stop = i; break; }
            return new TextCharacters(text.AsMemory(index, stop - index), ToRunProperties(seg.Style));
        }
    }

    private List<(Control Child, Rect Slot)> _arranged = new();

    protected override Size ArrangeOverride(Size finalSize)
    {
        _layer.Arrange(new Rect(finalSize));
        foreach (var (child, slot) in _arranged)
        {
            if (!_scrolling && !_vScrolling) { child.Arrange(slot); continue; }
            bool hidden = _scrolling
                ? slot.Right <= _offsetX || slot.X >= _offsetX + _viewWidth
                : slot.Bottom <= _offsetY || slot.Y >= _offsetY + _viewHeight;
            if (hidden)
                child.Arrange(new Rect(-100000 - slot.Width, slot.Y, slot.Width, slot.Height)); // outside the viewport
            else
                child.Arrange(slot.Translate(new Vector(-_offsetX, -_offsetY)));
        }
        if (_tailRect is { } tail)
        {
            bool scrolling = _scrolling || _vScrolling;
            bool hidden = _scrolling ? tail.Right <= _offsetX || tail.X >= _offsetX + _viewWidth
                : _vScrolling && (tail.Bottom <= _offsetY || tail.Y >= _offsetY + _viewHeight);
            _tail.Arrange(hidden ? new Rect(-100000, 0, tail.Width, tail.Height)
                : scrolling ? tail.Translate(new Vector(-_offsetX, -_offsetY)) : tail);
        }
        if (_scrollBar.IsVisible)
            _scrollBar.Arrange(new Rect(0, Math.Max(0, finalSize.Height - _barHeight), _viewWidth, _barHeight));
        if (_vBar.IsVisible)
            _vBar.Arrange(new Rect(Math.Max(0, finalSize.Width - _barWidth), 0, _barWidth, finalSize.Height));
        return finalSize;
    }

    /// <summary>Draws the already-laid-out lines. Panel.Render is sealed, so a child control does the painting.</summary>
    private sealed class TextLayer(BibleTextBlock owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            // Only whole columns are visible: clip to them and slide the row of columns by the scroll offset.
            double width = owner._scrolling ? owner._viewWidth : Bounds.Width;
            using var clip = context.PushClip(new Rect(0, 0, width, Bounds.Height));
            using var shift = context.PushTransform(Matrix.CreateTranslation(-owner._offsetX, -owner._offsetY));
            owner.DrawSelection(context);
            foreach (var placed in owner._lines)
                if (owner.IsInView(placed)) placed.Line.Draw(context, placed.Origin);
        }
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private readonly TextLayer _layer;
    private readonly Popup _popup;

    private const double TailSize = 32;
    private readonly BusySpinner _tail; // after the lines flowed so far, while the rest is still being flowed
    private Rect? _tailRect;            // where it sits, in the coordinates of the text
    private List<NoteRange> _notes = new();

    /// <summary>Finds the footnote marker under <paramref name="point"/>, and the marker's bounds.</summary>
    private Footnote? HitFootnote(Point point, out Rect bounds)
    {
        bounds = default;
        if (_notes.Count == 0) return null;
        foreach (var pl in _lines)
        {
            var line = pl.Line;
            if (point.Y < pl.Origin.Y || point.Y >= pl.Origin.Y + line.Height) continue;
            double x = point.X - pl.Origin.X;
            if (x < 0 || x > line.WidthIncludingTrailingWhitespace) continue; // another column's line may match

            // Character indices from TextLine are absolute indices into the flattened text.
            int index = line.GetCharacterHitFromDistance(x).FirstCharacterIndex;
            foreach (var n in _notes)
            {
                if (index < n.Start || index >= n.End) continue;
                var rects = line.GetTextBounds(n.Start, n.End - n.Start);
                bounds = rects.Count > 0
                    ? rects[0].Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y))
                    : new Rect(point, new Size(1, line.Height));
                return n.Note;
            }
        }
        return null;
    }

    /// <summary>How far (in device-independent pixels) from a footnote marker a finger still counts as hitting it.</summary>
    private const double TouchSlop = 24;

    /// <summary>
    /// Like <see cref="HitFootnote"/>, but with a fat finger: the marker's bounds are grown by
    /// <see cref="TouchSlop"/> on every side (about a 48 dp target), and when several markers are in reach the one
    /// whose centre is nearest wins.
    /// </summary>
    private Footnote? HitFootnoteNear(Point point, out Rect bounds)
    {
        bounds = default;
        if (_notes.Count == 0) return null;
        Footnote? best = null;
        double bestDistance = double.MaxValue;
        foreach (var pl in _lines)
        {
            var line = pl.Line;
            if (point.Y < pl.Origin.Y - TouchSlop || point.Y >= pl.Origin.Y + line.Height + TouchSlop) continue;
            int first = line.FirstTextSourceIndex, end = first + line.Length;
            foreach (var n in _notes)
            {
                int start = Math.Max(n.Start, first), stop = Math.Min(n.End, end);
                if (stop <= start) continue; // the marker is not on this line
                var rects = line.GetTextBounds(start, stop - start);
                if (rects.Count == 0) continue;
                var rect = rects[0].Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y));
                if (!rect.Inflate(TouchSlop).Contains(point)) continue;
                double distance = Math.Abs(rect.Center.X - point.X) + Math.Abs(rect.Center.Y - point.Y);
                if (distance >= bestDistance) continue;
                best = n.Note;
                bestDistance = distance;
                bounds = rect;
            }
        }
        return best;
    }

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // ---- Text selection -------------------------------------------------------------------------------
    // SelectableTextBlock selects through its own TextLayout, which this control doesn't use (it formats lines
    // itself), so selection is implemented here on top of the laid-out lines.

    private string _text = string.Empty;

    private bool _appliedVertical; // how the lines now in _lines are arranged
    private double _appliedWidth;  // and how wide their column is

    /// <summary>Text index of the line at the top of the view (the first column shown), in the layout now applied.</summary>
    private int? VisibleStartIndex()
    {
        if (_pitch <= 0) return null;
        if (_appliedVertical)
        {
            foreach (var pl in _lines)
                if (pl.Origin.Y + pl.Line.Height > _offsetY + 0.5) return pl.Line.FirstTextSourceIndex;
            return null;
        }
        int column = _scrolling ? _firstColumn : 0;
        foreach (var pl in _lines)
            if (Math.Floor((pl.Origin.X + 1) / _pitch) >= column) return pl.Line.FirstTextSourceIndex;
        return null;
    }
    private int _anchor, _caret; // selection is [min, max) in flattened-text indices
    private bool _selecting;

    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(110, 0x33, 0x8F, 0xFF));
    private static readonly Cursor IBeamCursor = new(StandardCursorType.Ibeam);

    private int SelStart => Math.Min(_anchor, _caret);
    private int SelEnd => Math.Max(_anchor, _caret);
    public bool HasSelection => SelEnd > SelStart;

    /// <summary>Caret index (0..text length) nearest to <paramref name="point"/>, or null when there is no text.</summary>
    private int? IndexAt(Point point)
    {
        PlacedLine? best = null;
        double bestDistance = double.MaxValue;
        foreach (var pl in _lines)
        {
            double w = pl.Line.WidthIncludingTrailingWhitespace, h = pl.Line.Height;
            double dy = point.Y < pl.Origin.Y ? pl.Origin.Y - point.Y : point.Y >= pl.Origin.Y + h ? point.Y - (pl.Origin.Y + h) : 0;
            double dx = point.X < pl.Origin.X ? pl.Origin.X - point.X : point.X > pl.Origin.X + w ? point.X - (pl.Origin.X + w) : 0;
            double d = dy * dy + dx * dx;
            if (d < bestDistance) { bestDistance = d; best = pl; }
        }
        if (best is not { } b) return null;

        var line = b.Line;
        double x = Math.Clamp(point.X - b.Origin.X, 0, line.WidthIncludingTrailingWhitespace);
        var hit = line.GetCharacterHitFromDistance(x);
        int index = hit.FirstCharacterIndex + hit.TrailingLength;
        int first = line.FirstTextSourceIndex;
        return Math.Clamp(index, first, first + line.Length - line.NewLineLength);
    }

    private bool IsOverText(Point point) =>
        _lines.Any(pl => point.Y >= pl.Origin.Y && point.Y < pl.Origin.Y + pl.Line.Height
                         && point.X >= pl.Origin.X && point.X <= pl.Origin.X + pl.Line.WidthIncludingTrailingWhitespace);

    private void SetSelection(int anchor, int caret)
    {
        int max = _text.Length;
        (_anchor, _caret) = (Math.Clamp(anchor, 0, max), Math.Clamp(caret, 0, max));
        _layer.InvalidateVisual();
    }

    private void SelectWordAt(int index)
    {
        static bool InWord(char c) => char.IsLetterOrDigit(c) || c == '­' || c == '\'';
        int start = Math.Min(index, _text.Length), end = start;
        while (start > 0 && InWord(_text[start - 1])) start--;
        while (end < _text.Length && InWord(_text[end])) end++;
        SetSelection(start, end);
    }

    /// <summary>The selected text as plain text: soft hyphens and footnote markers are left out.</summary>
    public string GetSelectedText()
    {
        var sb = new StringBuilder();
        for (int i = SelStart; i < SelEnd && i < _text.Length; i++)
        {
            if (_text[i] == '­' || _notes.Any(n => i >= n.Start && i < n.End)) continue;
            sb.Append(_text[i]);
        }
        return sb.ToString();
    }

    public void SelectAll() => SetSelection(0, _text.Length);

    public async void Copy()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && HasSelection)
            await clipboard.SetTextAsync(GetSelectedText());
    }

    private void DrawSelection(DrawingContext context)
    {
        if (!HasSelection) return;
        foreach (var pl in _lines)
        {
            if (!IsInView(pl)) continue;
            int first = pl.Line.FirstTextSourceIndex;
            int start = Math.Max(SelStart, first), end = Math.Min(SelEnd, first + pl.Line.Length - pl.Line.NewLineLength);
            if (end <= start) continue;
            foreach (var box in pl.Line.GetTextBounds(start, end - start))
                context.DrawRectangle(SelectionBrush, null, box.Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y)));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers == KeyModifiers.None && NavigateByKey(e.Key)) { e.Handled = true; return; }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key == Key.C) { Copy(); e.Handled = true; }
        else if (e.Key == Key.A) { SelectAll(); e.Handled = true; }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (OverScrollBar(position)) { Cursor = null; return; }
        var point = ToContent(position);
        if (_selecting)
        {
            if (IndexAt(point) is { } index) SetSelection(_anchor, index);
            return;
        }
        Cursor = HitFootnote(point, out _) is not null ? HandCursor : IsOverText(point) ? IBeamCursor : null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _touchDown = false;
        if (!_selecting) return;
        _selecting = false;
        e.Pointer.Capture(null);
    }

    // Light dismiss closes a popup that opens during the very press that is being handled, so the popup is
    // shown from a posted callback that runs after this press has been fully processed.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Pointer.Type == PointerType.Touch) { _touchDown = true; return; } // touch pans (and taps open footnotes); it does not select
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var position = e.GetPosition(this);
        if (OverScrollBar(position)) return; // the scroll bar is a child; its presses bubble up to here
        var point = ToContent(position);
        if (HitFootnote(point, out var bounds) is { } note)
        {
            bounds = bounds.Translate(new Vector(-_offsetX, -_offsetY)); // popup placement uses control coordinates
            e.Handled = true;
            Dispatcher.UIThread.Post(() => ShowFootnote(note, bounds), DispatcherPriority.Background);
            return;
        }

        // Start a selection: click collapses it, double-click selects a word, dragging extends it.
        Focus();
        if (IndexAt(point) is not { } index) return;
        if (e.ClickCount == 2)
        {
            SelectWordAt(index);
        }
        else
        {
            SetSelection(index, index);
            _selecting = true;
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    /// <summary>The popup's text is this much smaller than the text of the reader.</summary>
    private const double FootnoteScale = 0.85;

    private void ShowFootnote(Footnote note, Rect bounds)
    {
        _popup.IsOpen = false;
        _popup.PlacementRect = bounds;
        // Theme resources (not fixed colours) so the popup follows light/dark mode, including live theme switches.
        var text = new SelectableTextBlock // selectable so the footnote text can be copied
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = FontSize * FootnoteScale,
            FontFamily = FontFamily, // a popup is its own visual root: it would not inherit the font from this control
        };
        foreach (var inline in note.CloneContent(FootnoteScale)) text.Inlines!.Add(inline);
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseHighBrush");

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            MaxWidth = 320,
            Child = text,
        };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundAltHighBrush");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseLowBrush");
        _popup.Child = card;
        _popup.IsOpen = true;
    }

    public BibleTextBlock()
    {
        _layer = new TextLayer(this) { IsHitTestVisible = false };
        Children.Add(_layer);
        _popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            PlacementTarget = this,
        };
        Children.Add(_popup);
        _tail = new BusySpinner { IsVisible = false, IsHitTestVisible = false, Thickness = 3 };
        _tail[!BusySpinner.BrushProperty] = new DynamicResourceExtension("SystemControlHighlightAccentBrush");
        Children.Add(_tail);
        _scrollBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false, AllowAutoHide = false };
        _scrollBar.ValueChanged += (_, e) =>
        {
            if (!_updatingBar) ApplyColumn((int)Math.Round(e.NewValue)); // snap to whole columns
        };
        // The bar can't move past its ends, so pressing an end button while already there asks for the next/previous book.
        _scrollBar.AddHandler(PointerPressedEvent, (_, e) =>
        {
            double x = e.GetPosition(_scrollBar).X, width = _scrollBar.Bounds.Width;
            if (x > width - 24 && _scrollBar.Value >= _scrollBar.Maximum - 0.5) RaiseBoundary(atEnd: true);
            else if (x < 24 && _scrollBar.Value <= 0.5) RaiseBoundary(atEnd: false);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Children.Add(_scrollBar);
        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _vBar.ValueChanged += (_, e) => { if (!_updatingBar) SetOffsetY(e.NewValue); };
        Children.Add(_vBar);
        GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = true });
        AddHandler(InputElement.ScrollGestureEvent, OnScrollGesture);
        AddHandler(InputElement.ScrollGestureInertiaStartingEvent, OnScrollInertiaStarting);
        AddHandler(InputElement.ScrollGestureEndedEvent, OnScrollGestureEnded);
        Tapped += OnTapped;
        ClipToBounds = true;
        Focusable = true;
        ContextMenu = new ContextMenu
        {
            ItemsSource = new[]
            {
                CreateMenuItem("Copy", () => Copy()),
                CreateMenuItem("Select all", SelectAll),
            },
        };
        Background = Brushes.Transparent; // a Panel only receives pointer input where it has a background
        Inlines = new InlineCollection();
        Inlines.CollectionChanged += (_, _) => OnInlinesChanged();
    }

    /// <summary>
    /// Rich text content: <c>&lt;Run&gt;</c>, <c>&lt;Span&gt;</c>, <c>&lt;LineBreak/&gt;</c>. Controls placed among
    /// them are wrapped in an <c>InlineUIContainer</c> by Avalonia and become floats.
    /// </summary>
    [Content]
    public InlineCollection Inlines { get; }

    bool NewChapter = false;
    private readonly HashSet<Control> _ownedFloats = new();

    private void OnInlinesChanged()
    {
        var current = new HashSet<Control>();
        foreach (var inline in Inlines) Collect(inline);

        void Collect(Inline inline)
        {
            switch (inline)
            {
                case FloatRun fr: current.Add(fr.Visual); break;
                case InlineUIContainer { Child: { } child }: current.Add(child); break;
                case Span span: foreach (var inner in span.Inlines ?? []) Collect(inner); break;
            }
        }

        // Drop floats whose inline was removed, add new ones.
        foreach (var stale in _ownedFloats.Where(c => !current.Contains(c)).ToList())
        {
            Children.Remove(stale);
            _ownedFloats.Remove(stale);
        }
        foreach (var c in current)
            if (_ownedFloats.Add(c) && !Children.Contains(c))
                Children.Add(c);
        MarkDirty();
    }

    // ---- Parsing BibleText runs ---------------------------------------------------------------------

    private static readonly IBrush WordsOfJesusBrush = new SolidColorBrush(Color.Parse("#B22222"));
    private static bool _buildWordsOfJesus = true;
    private static readonly IBrush VerseNumberBrush = new SolidColorBrush(Color.Parse("#2E8B57"));
    private static bool _buildGreenVerses;

    /// <summary>
    /// Builds the books of the window again, e.g. after <see cref="FontSize"/> or <see cref="ShowWordsOfJesus"/>
    /// changed (the inlines carry the size and colours they were built with). The reader stays where they are.
    /// </summary>
    public async Task RebuildAsync()
    {
        if (Books is not { } books || _loadingBook) return;
        // The new content starts at the top of the window unless a target is pending: ask for the verse the reader is
        // at. It is resolved when the new text is laid out (which may only happen once the control is visible again),
        // and Position stays as it is until then. Assigning Position to its own value would do nothing.
        if (Position is { Book: not null } position)
        {
            _pendingPosition = position;
            _keepPosition = true;
        }
        await SwitchWindowAsync(books, BookIndex, null);
    }

    /// <summary>How long the content is built before the UI gets a turn (input, drawing) in between.</summary>
    private const int BuildSliceMs = 8;

    /// <summary>
    /// Turns one or two books into inlines (separated by a book <see cref="PageBreak"/>). Inlines are Avalonia objects,
    /// so this has to happen on the UI thread; it is done in short slices, with the UI free in between, and the
    /// inlines are not attached to the control until they are all built.
    /// </summary>
    private async Task<(List<Inline> Content, PageBreak? Break)> BuildContentAsync(Bible.Book first, Bible.Book? second)
    {
        double size = FontSize;
        _buildGreenVerses = ShowVerseNumbersInGreen;
        _buildWordsOfJesus = ShowWordsOfJesus; // read by the static ToInlines; building happens on the UI thread only
        var content = new List<Inline>();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        async Task AddAsync(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines) // the work happens while this enumerates
            {
                content.Add(inline);
                if (clock.ElapsedMilliseconds < BuildSliceMs) continue;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); // pending input and drawing go first
                clock.Restart();
            }
        }

        await AddAsync(ToInlines(first, size, upperCase: false));
        PageBreak? bookBreak = null;
        if (second is not null)
        {
            bookBreak = new PageBreak();
            content.Add(bookBreak);
            await AddAsync(ToInlines(second, size, upperCase: false));
        }
        return (content, bookBreak);
    }

    /// <summary>Replaces the content with built inlines (see <see cref="BuildContentAsync"/>).</summary>
    private void SetContent(List<Inline> content, PageBreak? bookBreak)
    {
        StopAnimation();
        _panning = false;
        _overscroll = 0;
        // When a position is already known (the window slides on while scrolling, or a verse is wanted) the shown
        // layout stays exactly where it is until the new lines are flowed, and the position is then set from the
        // anchor. Only a plain start from the top resets the scrolling right away.
        if (_pendingAnchor is null && _pendingPosition is null)
        {
            _firstColumn = 0;
            _offsetX = _offsetY = 0;
        }
        _anchor = _caret = 0;
        _hasSplit = false;
        _bookBreak = bookBreak;
        // One change notification each: every one walks all the inlines.
        Inlines.Clear();
        Inlines.AddRange(content);
    }
    private static IEnumerable<Inline> ToInlines(Bible.Book book, double size, bool upperCase)
    {
        // Mark the book start (chapter 0), so Position names the new book as soon as its heading is in view.
        yield return new VerseRun { Position = new Bible.VersePosition { Book = book, Chapter = 0, Verse = 0 } };
        yield return new Run(book.Name)
        {
            FontSize = size * 2.4,
            FontWeight = FontWeight.Bold,
        };
        yield return new LineBreak();
        yield return new LineBreak();
        foreach (var inline in ToInlines(book.Runs, size, upperCase)) yield return inline;
    }

    private static IEnumerable<Inline> ToInlines(IEnumerable<Bible.Run>? runs, double size, bool upperCase)
    {
        foreach (var run in runs ?? [])
        {
            foreach (var inline in ToInlines(run, size, upperCase))
                yield return inline;
        }
    }

    private static IEnumerable<Inline> ToInlines(Bible.Run run, double size, bool upperCase)
    {
        var firstRun = run.Runs?.FirstOrDefault();
        switch (run.Class)
        {
            case Bible.Run.Classes.Footnote:
            {
                // The popup content keeps its structure, so links/references inside a footnote stay styled.
                var note = new Footnote();
                    var runs = run.Runs;
                    if (runs?.Any() == true)
                    {
                        if (firstRun?.Class == Bible.Run.Classes.Bold &&
                            firstRun.Text == $"{run.Position.Chapter}:{run.Position.Verse}")
                        {
                            foreach (var child in ToInlines(run.Runs.Skip(1), size, upperCase))
                                note.Inlines!.Add(child);
                        }
                        else 
                        {
                            if (firstRun.Text.StartsWith($"{run.Position.Chapter}:{run.Position.Verse}"))
                            {
                                firstRun.Text = Regex.Replace(firstRun.Text, $@"{run.Position.Chapter}:{run.Position.Verse}\s*", "");
                            }
                            if (string.IsNullOrWhiteSpace(firstRun.Text))
                            {
                                foreach (var child in ToInlines(run.Runs.Skip(1), size, upperCase))
                                    note.Inlines!.Add(child);
                            }
                            else
                            {
                                foreach (var child in ToInlines(run.Runs, size, upperCase))
                                    note.Inlines!.Add(child);
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(run.Text))
                        note.Inlines!.Add(new Avalonia.Controls.Documents.Run(run.Text));
                yield return note;
                yield break;
            }

            case Bible.Run.Classes.Chapter:
                if (run.Position is not null) yield return new VerseRun { Position = run.Position }; // chapter start (verse 0)
                if (run.Runs.Any() && firstRun?.Class == Bible.Run.Classes.Title)
                { // Swap chapter & first title
                    foreach (var child in ToInlines(firstRun, size, upperCase)) yield return child;
                    yield return new FloatRun(run.Chapter.ToString())
                    {
                        Side = FloatSide.Left,
                        FontSize = size * 2.4,
                        FontWeight = FontWeight.Bold,
                    };
                    // The chapter number is already shown as a left float, so don't repeat it in the text.
                    foreach (var child in ToInlines(run.Runs.Skip(1), size, upperCase)) yield return child;
                    yield break;
                }
                else
                {
                    yield return new FloatRun(run.Chapter.ToString())
                    {
                        Side = FloatSide.Left,
                        FontSize = size * 2.4,
                        FontWeight = FontWeight.Bold,
                    };
                    if (run.Runs?.Any() == true)
                    {
                        foreach (var child in ToInlines(run.Runs, size, upperCase)) yield return child;
                    }
                }
                yield break;

            case Bible.Run.Classes.Verse:
                yield return new VerseRun(run.Position?.Verse.ToString() ?? "")
                {
                    Position = run.Position,
                    FontWeight = FontWeight.Bold,
                    Foreground = _buildGreenVerses ? VerseNumberBrush
                        : run.MarkerColor.A != 0 ? new SolidColorBrush(run.MarkerColor) : null,
                };
                if (run.Runs?.Any() == true)
                    foreach (var child in ToInlines(run.Runs, size, upperCase)) yield return child;
                else if (!string.IsNullOrEmpty(run.Text))
                    yield return new Avalonia.Controls.Documents.Run(run.Text);

                yield break;

            case Bible.Run.Classes.Paragraph:
                yield return new LineBreak();
                yield return new LineBreak();
                yield break;
            case Bible.Run.Classes.Title:
                if (!run.First)
                {
                    yield return new LineBreak();
                    yield return new LineBreak();
                }
                break;
        }

        // Everything else is a styled span around the run's text or its parsed children.
        var span = new Span();
        bool lineAfter = false;
        switch (run.Class)
        {
            case Bible.Run.Classes.Bold: span.FontWeight = FontWeight.Bold; break;
            case Bible.Run.Classes.Italic: span.FontStyle = FontStyle.Italic; break;
            case Bible.Run.Classes.WordWithStrong: span.TextDecorations = StrongsUnderline; break;
            case Bible.Run.Classes.Greek: span.FontFamily = GreekFont; break;
            case Bible.Run.Classes.Hebrew:
                span.FontFamily = HebrewFont;
                span.FontSize = size * HebrewScale; // Frank Ruehl CLM looks small next to the Latin fonts
                break;
            case Bible.Run.Classes.WordsOfJesus: if (_buildWordsOfJesus) span.Foreground = WordsOfJesusBrush; break;
            case Bible.Run.Classes.SmallCaps: upperCase = true; break; // the size is reduced per letter, see SmallCapsRuns
            case Bible.Run.Classes.Link:
                span.TextDecorations = TextDecorations.Underline;
                span.Foreground = LinkBrush;
                break;
            case Bible.Run.Classes.Title:
            case Bible.Run.Classes.Book:
                size *= 1.25;
                span.FontSize = size;
                span.FontWeight = FontWeight.Bold;
                lineAfter = true;
                break;
        }

        bool hasChildren = run.Runs?.Any() == true;
        if (hasChildren)
        {
            foreach (var child in ToInlines(run.Runs, size, upperCase))
                span.Inlines!.Add(child);
        }
        else if (!string.IsNullOrEmpty(run.Text))
        {
            if (upperCase) foreach (var part in SmallCapsRuns(run.Text, size)) span.Inlines!.Add(part);
            else span.Inlines!.Add(new Avalonia.Controls.Documents.Run(run.Text));
        }

        if (span.Inlines!.Count > 0) yield return span;
        if (lineAfter) yield return new LineBreak();
    }

    private const double SmallCapsScale = 0.85;

    /// <summary>The dotted gray underline of words that have a Strong's number.</summary>
    private static readonly TextDecorationCollection StrongsUnderline = new()
    {
        new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            Stroke = new SolidColorBrush(Colors.Gray),
            StrokeThickness = 1.2,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 1, 2 },
            StrokeLineCap = PenLineCap.Round,
        },
    };

    // Greek text is set in Cardo and Hebrew in Frank Ruehl CLM, whatever font the reader chose for the rest.
    private static readonly FontFamily GreekFont = new("avares://BibleReader.Library/Assets#Cardo");
    private static readonly FontFamily HebrewFont = new("avares://BibleReader.Library/Assets#Frank Ruehl CLM");
    private const double HebrewScale = 1.5;

    /// <summary>
    /// Small capitals: the first letter of every word is a capital of the regular size, the other letters are
    /// capitals of a smaller size ("Lord" becomes L + ORD).
    /// </summary>
    private static IEnumerable<Avalonia.Controls.Documents.Run> SmallCapsRuns(string text, double size)
    {
        bool wordStart = true;
        var small = new StringBuilder();
        foreach (var ch in text)
        {
            if (wordStart && !char.IsWhiteSpace(ch))
            {
                if (small.Length > 0) { yield return SmallRun(small.ToString()); small.Clear(); }
                yield return new Avalonia.Controls.Documents.Run(char.ToUpperInvariant(ch).ToString());
                wordStart = false;
            }
            else
            {
                small.Append(char.IsWhiteSpace(ch) ? ch : char.ToUpperInvariant(ch));
                wordStart = char.IsWhiteSpace(ch);
            }
        }
        if (small.Length > 0) yield return SmallRun(small.ToString());

        Avalonia.Controls.Documents.Run SmallRun(string t) => new(t) { FontSize = size * SmallCapsScale };
    }

    /// <summary>The run's text without markup: its children's text when it has any, else its own.</summary>
    private static string PlainText(Bible.Run run) =>
        run.Runs?.Any() == true ? string.Concat(run.Runs.Select(PlainText)) : run.Text ?? string.Empty;

    private readonly record struct RunStyle(FontFamily Family, double Size, FontStyle Style, FontWeight Weight,
        IBrush? Foreground, TextDecorationCollection? Decorations,
        BaselineAlignment Baseline = BaselineAlignment.Baseline);

    /// <summary>1 → "a", 26 → "z", 27 → "aa", …</summary>
    private static string Letters(int number)
    {
        var sb = new StringBuilder();
        for (; number > 0; number = (number - 1) / 26)
            sb.Insert(0, (char)('a' + (number - 1) % 26));
        return sb.ToString();
    }

    private readonly record struct NoteRange(int Start, int End, Footnote Note);

    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#1A6FD1"));

    private readonly record struct Segment(int Start, int End, RunStyle Style);

    private RunStyle BaseStyle() => new(FontFamily, FontSize, FontStyle.Normal, FontWeight.Normal,
        Foreground ?? Brushes.Black, null);

    private static GenericTextRunProperties ToRunProperties(RunStyle s) =>
        new(new Typeface(s.Family, s.Style, s.Weight), s.Size, s.Decorations, s.Foreground,
            baselineAlignment: s.Baseline);

    /// <summary>Inherit from <paramref name="parent"/>, overriding only what the inline sets locally.</summary>
    private static RunStyle Derive(Inline inline, RunStyle parent)
    {
        var r = parent;
        if (inline.IsSet(TextElement.FontFamilyProperty)) r = r with { Family = inline.FontFamily };
        if (inline.IsSet(TextElement.FontSizeProperty)) r = r with { Size = inline.FontSize };
        if (inline.IsSet(TextElement.FontStyleProperty)) r = r with { Style = inline.FontStyle };
        if (inline.IsSet(TextElement.FontWeightProperty)) r = r with { Weight = inline.FontWeight };
        if (inline.IsSet(TextElement.ForegroundProperty) && inline.Foreground is { } fg) r = r with { Foreground = fg };
        if (inline.TextDecorations is { } d) r = r with { Decorations = d };
        if (inline.IsSet(Inline.BaselineAlignmentProperty)) r = r with { Baseline = inline.BaselineAlignment };
        return r;
    }

    /// <summary>
    /// Concatenates <see cref="Inlines"/> (or <see cref="Text"/> when there are none) into one string plus
    /// per-run styles. Whitespace is collapsed like HTML so indented XAML text doesn't show its indentation.
    /// </summary>
    private (string Text, List<Segment> Segments, Dictionary<Control, int> Anchors, List<NoteRange> Notes,
        HashSet<int> Breaks, int SplitIndex, List<(int Index, Bible.VersePosition Pos)> Markers) Flatten()
    {
        var sb = new StringBuilder();
        var segs = new List<Segment>();
        var anchors = new Dictionary<Control, int>();
        var notes = new List<NoteRange>();
        var breaks = new HashSet<int>();
        var markers = new List<(int Index, Bible.VersePosition Pos)>();
        int splitIndex = -1;
        int noteNumber = 0;
        bool lastSpace = true; // trims leading whitespace
        var root = BaseStyle();

        void Append(string raw, RunStyle style, bool verbatim = false)
        {
            int start = sb.Length;
            foreach (char c in raw)
            {
                if (!verbatim && char.IsWhiteSpace(c))
                {
                    if (lastSpace) continue;
                    sb.Append(' ');
                    lastSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastSpace = verbatim && c == '\n';
                }
            }
            if (sb.Length > start) segs.Add(new Segment(start, sb.Length, style));
        }

        // Avalonia turns the line break between XAML elements into a one-character whitespace Run. Next to a
        // footnote that would be a (justify-stretched) gap, so it is dropped: the marker belongs to its word.
        bool afterFootnote = false;

        void TrimTrailingSpace()
        {
            if (sb.Length == 0 || sb[^1] != ' ') return;
            sb.Length--;
            if (segs.Count > 0)
            {
                var last = segs[^1];
                if (last.End - 1 <= last.Start) segs.RemoveAt(segs.Count - 1);
                else segs[^1] = last with { End = last.End - 1 };
            }
            lastSpace = false;
        }

        void Walk(IEnumerable<Inline> inlines, RunStyle parent)
        {
            foreach (var inline in inlines)
            {
                var style = Derive(inline, parent);
                if (inline is VerseRun { Position: { } versePosition }) markers.Add((sb.Length, versePosition));
                if (inline is Run { Text: { } t } && inline is not FloatRun && inline is not Footnote
                    && afterFootnote && string.IsNullOrWhiteSpace(t))
                    continue; // formatting whitespace right after a footnote marker
                if (inline is not FloatRun && inline is not Footnote) afterFootnote = false;
                switch (inline)
                {
                    case FloatRun fr: anchors[fr.Visual] = sb.Length; break; // floated, not flowed
                    case InlineUIContainer { Child: { } child }: anchors[child] = sb.Length; break;
                    case Footnote fn:
                    {
                        string marker = fn.Marker ?? Letters(++noteNumber);
                        var markerStyle = parent with
                        {
                            Size = parent.Size * 0.7,
                            Baseline = BaselineAlignment.Superscript,
                            Foreground = fn.IsSet(TextElement.ForegroundProperty) ? fn.Foreground : LinkBrush,
                            Decorations = null,
                        };
                        TrimTrailingSpace(); // no gap between the word and its marker
                        int markerStart = sb.Length;
                        Append(marker, markerStyle, verbatim: true);
                        if (sb.Length > markerStart) notes.Add(new NoteRange(markerStart, sb.Length, fn));
                        afterFootnote = true;
                        break;
                    }
                    case Run run: Append(run.Text ?? string.Empty, style); break;
                    case PageBreak pageBreak:
                        Append("\n", style, verbatim: true);
                        breaks.Add(sb.Length); // the next line starts a column
                        if (ReferenceEquals(pageBreak, _bookBreak)) splitIndex = sb.Length; // the second book starts here
                        break;
                    case LineBreak: Append("\n", style, verbatim: true); break;
                    case Span span when span.Inlines is { } children: Walk(children, style); break;
                }
            }
        }

        if (Inlines.Count > 0) Walk(Inlines, root);
        else if (!string.IsNullOrEmpty(Text)) Append(Text, root);

        // Trim one trailing collapsed space.
        if (sb.Length > 0 && sb[^1] == ' ' && segs.Count > 0)
        {
            sb.Length--;
            var last = segs[^1];
            if (last.End - 1 <= last.Start) segs.RemoveAt(segs.Count - 1);
            else segs[^1] = last with { End = last.End - 1 };
        }
        // The caller stores breaks, split and markers together with the lines they belong to (see MeasureCore).
        return (sb.ToString(), segs, anchors, notes, breaks, splitIndex, markers);
    }

    /// <summary>Index of the segment containing <paramref name="index"/> (segments are contiguous and sorted), or -1.</summary>
    private static int FindSegment(List<Segment> segments, int index)
    {
        int lo = 0, hi = segments.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var seg = segments[mid];
            if (index < seg.Start) hi = mid - 1;
            else if (index >= seg.End) lo = mid + 1;
            else return mid;
        }
        return -1;
    }

    /// <summary>
    /// Serves the flattened text to the formatter one styled run at a time. <see cref="Limit"/> makes the text
    /// appear to end early: the formatter fetches runs up to the end of the paragraph for every line it breaks,
    /// which is quadratic for a long paragraph, so each line is formatted from a window just past its end.
    /// </summary>
    private sealed class RichTextSource(string text, List<Segment> segments) : ITextSource
    {
        public int Limit { get; set; } = int.MaxValue;

        public TextRun? GetTextRun(int textSourceIndex)
        {
            int end = Math.Min(text.Length, Limit);
            if (textSourceIndex >= end) return null;
            int i = FindSegment(segments, textSourceIndex);
            if (i < 0) return null;
            var seg = segments[i];
            return new TextCharacters(text.AsMemory(textSourceIndex, Math.Min(seg.End, end) - textSourceIndex), ToRunProperties(seg.Style));
        }
    }
}
