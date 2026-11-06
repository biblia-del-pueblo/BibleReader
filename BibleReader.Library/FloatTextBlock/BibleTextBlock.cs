using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Metadata;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;

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

    public static readonly AttachedProperty<FloatSide> SideProperty =
        AvaloniaProperty.RegisterAttached<BibleTextBlock, Control, FloatSide>("Side");

    static BibleTextBlock()
    {
        AffectsMeasure<BibleTextBlock>(TextProperty, FontSizeProperty, FontFamilyProperty,
            TextAlignmentProperty, FloatSpacingProperty, ColumnCountProperty, ColumnWidthProperty, ColumnSpacingProperty, OrientationProperty);
        AffectsArrange<BibleTextBlock>(SideProperty);
        AffectsMeasure<BibleTextBlock>(ForegroundProperty);
        SideProperty.Changed.AddClassHandler<Control>((c, _) => (c.Parent as BibleTextBlock)?.MarkDirty());
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public TextAlignment TextAlignment { get => GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public double FloatSpacing { get => GetValue(FloatSpacingProperty); set => SetValue(FloatSpacingProperty, value); }

    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public int ColumnCount { get => GetValue(ColumnCountProperty); set => SetValue(ColumnCountProperty, value); }
    public double ColumnWidth { get => GetValue(ColumnWidthProperty); set => SetValue(ColumnWidthProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }

    public static FloatSide GetSide(Control control) => control.GetValue(SideProperty);
    public static void SetSide(Control control, FloatSide value) => control.SetValue(SideProperty, value);

    private readonly record struct PlacedLine(TextLine Line, Point Origin);

    private readonly List<PlacedLine> _lines = new();
    private readonly List<Rect> _floatRects = new();

    // ---- Scrolling across books ---------------------------------------------------------------------
    // With <see cref="Books"/> set, scrolling past the end of the shown book continues in the next one and
    // scrolling back past the start continues at the end of the previous one.

    /// <summary>The books to scroll through; use <see cref="ShowBookAsync"/> to show one.</summary>
    public IReadOnlyList<BibleText.Book>? Books { get; set; }

    /// <summary>Index in <see cref="Books"/> of the book being shown.</summary>
    public int BookIndex { get; private set; }

    /// <summary>The shown book changed (by scrolling across a boundary or by <see cref="ShowBookAsync"/>).</summary>
    public event EventHandler? BookChanged;

    /// <summary>The user scrolled past the first or last page. Raised whether or not <see cref="Books"/> is set.</summary>
    public event EventHandler<BoundaryEventArgs>? BoundaryReached;

    private bool _loadingBook, _startAtEnd;
    private readonly System.Diagnostics.Stopwatch _boundaryClock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastBoundary = TimeSpan.FromSeconds(-10);

    /// <summary>
    /// Shows <c>Books[index]</c>. Reading and parsing the book happens off the UI thread.
    /// <paramref name="startAtEnd"/> shows its last page instead of its first (used when scrolling back).
    /// </summary>
    public async Task ShowBookAsync(int index, bool startAtEnd = false)
    {
        if (Books is not { } books || index < 0 || index >= books.Count || _loadingBook) return;
        _loadingBook = true;
        try
        {
            var book = books[index];
            var runs = await Task.Run(() => book.Runs); // lazily reads and parses the book's markdown
            _startAtEnd = startAtEnd;
            BookIndex = index;
            Parse(runs);
            BookChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _loadingBook = false; }
    }

    private void RaiseBoundary(bool atEnd)
    {
        // A flick or a wheel burst raises this many times: act once, then ignore the rest for a moment.
        if (_loadingBook || _boundaryClock.Elapsed - _lastBoundary < TimeSpan.FromMilliseconds(700)) return;
        _lastBoundary = _boundaryClock.Elapsed;
        BoundaryReached?.Invoke(this, new BoundaryEventArgs(atEnd));
        if (Books is null) return;
        int next = BookIndex + (atEnd ? 1 : -1);
        if (next >= 0 && next < Books.Count) _ = ShowBookAsync(next, startAtEnd: !atEnd);
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
        _dirty = false;
        _lastAvailable = availableSize;
        var result = MeasureCore(availableSize);
        _lastTotal = result;
        _hasLayout = true;
        return result;
    }

    private Size MeasureCore(Size availableSize)
    {
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
        _width = width;
        double gap = FloatSpacing;

        // 1. Measure floats. Each one is anchored at the text index where it was declared and gets placed
        //    while flowing, on the line that contains that index (like CSS float).
        var (text, segments, anchors, notes) = Flatten();
        _notes = notes;
        _text = text;
        _anchor = Math.Min(_anchor, text.Length);
        _caret = Math.Min(_caret, text.Length);
        _floatRects.Clear();
        var pending = new List<(Control Child, Size Size, FloatSide Side, int Anchor)>();
        foreach (var child in Children.OfType<Control>().Where(c => c != _layer && c != _popup && c != _scrollBar && c != _vBar))
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            pending.Add((child, child.DesiredSize, GetSide(child), anchors.TryGetValue(child, out var a) ? a : 0));
        }
        pending = pending.OrderBy(p => p.Anchor).ToList(); // stable: keeps declaration order for equal anchors

        var placed = new List<(Control Child, Rect Slot)>();
        double leftBottom = 0, rightBottom = 0; // floats on one side stack; the gap is included
        void PlaceFloat((Control Child, Size Size, FloatSide Side, int Anchor) f, double lineTop)
        {
            bool isLeft = f.Side == FloatSide.Left;
            double y = Math.Max(lineTop, isLeft ? leftBottom : rightBottom);
            double x = isLeft ? 0 : width - f.Size.Width;
            placed.Add((f.Child, new Rect(x, y, f.Size.Width, f.Size.Height)));
            // Rect used for text avoidance: grown by the gap on the text-facing edges.
            _floatRects.Add(isLeft
                ? new Rect(x, y, f.Size.Width + gap, f.Size.Height + gap)
                : new Rect(x - gap, y, f.Size.Width + gap, f.Size.Height + gap));
            if (isLeft) leftBottom = y + f.Size.Height + gap; else rightBottom = y + f.Size.Height + gap;
        }

        // 2. Flow the text line by line, narrowing each line by the floats it overlaps.
        _lines.Clear();
        double textBottom = 0;
        double top = 0;
        if (text.Length > 0)
        {
            var baseProps = ToRunProperties(BaseStyle());
            var paraProps = new GenericTextParagraphProperties(FlowDirection.LeftToRight, Avalonia.Media.TextAlignment.Left,
                true, false, baseProps, TextWrapping.Wrap, 0, 0, 0);
            var source = new RichTextSource(text, segments);
            var formatter = TextFormatter.Current;

            // Formats the line starting at `from` from a window of text just past where it can end (see
            // RichTextSource.Limit). A line that ends close to the window's edge is formatted again with a
            // bigger window, so the break is always decided with enough text after it.
            TextLine? Format(int from, double lineWidth)
            {
                int window = Math.Max(200, (int)(lineWidth / Math.Max(1, FontSize * 0.25)) * 2);
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

            int index = 0;
            while (index < text.Length)
            {
                // Probe with full width to learn the line height, then recompute with the real band.
                var probe = Format(index, width);
                if (probe is null) break;

                var (left, right) = Insets(top, probe.Height);
                double lineWidth = width - left - right;

                // Float fills the row: drop below the next float edge instead of squeezing text.
                // Only skip when a float is actually responsible; otherwise (narrow window) the text must
                // be squeezed into whatever width exists, or the loop would never consume any text.
                if (lineWidth < FontSize * 3 && (left > 0 || right > 0))
                {
                    var blocking = _floatRects.Where(r => r.Bottom > top && r.Top < top + probe.Height).Select(r => r.Bottom).ToList();
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

                if (TextAlignment == Avalonia.Media.TextAlignment.Justify && !lastLine && line.NewLineLength == 0)
                    line = JustifyLine(line, index, text, segments, lineWidth, paraProps) ?? line;

                _lines.Add(new PlacedLine(line, new Point(left, top)));
                top += line.Height;
                index = lineEnd;
                textBottom = top;
            }
        }

        // Floats with no text to attach to (empty text) go at the top.
        foreach (var f in pending) PlaceFloat(f, top);
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
        if (_startAtEnd && !vertical)
        {
            if (_scrolling) _firstColumn = usedColumns - visible; // last page
            _startAtEnd = false;
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
        return total;
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
        if (_startAtEnd)
        {
            if (_vScrolling) _offsetY = _contentHeight - _viewHeight; // bottom
            _startAtEnd = false;
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
        if (HitFootnote(ToContent(position), out var bounds) is { } note)
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
        _firstColumn = column;
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
        if (t >= 1) { _offsetX = target; _anim?.Stop(); }
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

    /// <summary>Space taken from the left and right of a line occupying [top, top + height).</summary>
    private (double Left, double Right) Insets(double top, double height)
    {
        double left = 0, right = 0;
        foreach (var r in _floatRects)
        {
            if (r.Bottom <= top || r.Top >= top + height) continue;
            if (r.X <= 0.5) left = Math.Max(left, r.Right);
            else right = Math.Max(right, _width - r.X);
        }
        return (left, right);
    }

    private double _width;

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

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // ---- Text selection -------------------------------------------------------------------------------
    // SelectableTextBlock selects through its own TextLayout, which this control doesn't use (it formats lines
    // itself), so selection is implemented here on top of the laid-out lines.

    private string _text = string.Empty;
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

    private void ShowFootnote(Footnote note, Rect bounds)
    {
        _popup.IsOpen = false;
        _popup.PlacementRect = bounds;
        // Theme resources (not fixed colours) so the popup follows light/dark mode, including live theme switches.
        var text = new SelectableTextBlock // selectable so the footnote text can be copied
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = FontSize * 0.85,
        };
        foreach (var inline in note.CloneContent()) text.Inlines!.Add(inline);
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
        AddHandler(Gestures.ScrollGestureEvent, OnScrollGesture);
        AddHandler(Gestures.ScrollGestureInertiaStartingEvent, OnScrollInertiaStarting);
        AddHandler(Gestures.ScrollGestureEndedEvent, OnScrollGestureEnded);
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

    /// <summary>
    /// Replaces the content with the given Bible text runs. Sizes are derived from the current
    /// <see cref="FontSize"/> at the time of the call; call again if the font size changes.
    /// </summary>
    /// <remarks>
    /// Mapping: Bold/Italic/SmallCaps/WordsOfJesus/Link/Title/Book become styled spans, Chapter becomes a
    /// left-floating <see cref="FloatRun"/>, Verse becomes a bold number in the normal text size, Footnote becomes a
    /// <see cref="Footnote"/>, Paragraph becomes a blank line. Other classes (Greek, Hebrew, WordWithStrong…)
    /// are shown as plain text for now.
    /// </remarks>
    public void Parse(List<BibleText.Run> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // New content starts at its beginning (ShowBookAsync can ask for its end instead).
        StopAnimation();
        _panning = false;
        _firstColumn = 0;
        _offsetX = _offsetY = _overscroll = 0;
        _anchor = _caret = 0;
        Inlines.Clear();
        Inlines.AddRange(ToInlines(text, FontSize, upperCase: false));
    }

    private static IEnumerable<Inline> ToInlines(IEnumerable<BibleText.Run>? runs, double size, bool upperCase)
    {
        foreach (var run in runs ?? [])
        {
            foreach (var inline in ToInlines(run, size, upperCase))
                yield return inline;
        }
    }

    private static IEnumerable<Inline> ToInlines(BibleText.Run run, double size, bool upperCase)
    {
        var firstRun = run.Runs?.FirstOrDefault();
        switch (run.Class)
        {
            case BibleText.Run.Classes.Footnote:
            {
                // The popup content keeps its structure, so links/references inside a footnote stay styled.
                var note = new Footnote();
                    var runs = run.Runs;
                    if (runs?.Any() == true)
                    {
                        if (firstRun?.Class == BibleText.Run.Classes.Bold &&
                            firstRun.Text == $"{run.Position.Chapter}:{run.Position.Verse}")
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
                    else if (!string.IsNullOrEmpty(run.Text))
                        note.Inlines!.Add(new Avalonia.Controls.Documents.Run(run.Text));
                yield return note;
                yield break;
            }

            case BibleText.Run.Classes.Chapter:
                if (run.Runs.Any() && firstRun?.Class == BibleText.Run.Classes.Title)
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

            case BibleText.Run.Classes.Verse:
                yield return new Avalonia.Controls.Documents.Run(run.Position?.Verse.ToString() ?? "")
                {
                    FontWeight = FontWeight.Bold,
                    Foreground = run.MarkerColor.A != 0 ? new SolidColorBrush(run.MarkerColor) : null,
                };
                if (run.Runs?.Any() == true)
                    foreach (var child in ToInlines(run.Runs, size, upperCase)) yield return child;
                else if (!string.IsNullOrEmpty(run.Text))
                    yield return new Avalonia.Controls.Documents.Run(run.Text);

                yield break;

            case BibleText.Run.Classes.Paragraph:
                yield return new LineBreak();
                yield return new LineBreak();
                yield break;
            case BibleText.Run.Classes.Title:
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
            case BibleText.Run.Classes.Bold: span.FontWeight = FontWeight.Bold; break;
            case BibleText.Run.Classes.Italic: span.FontStyle = FontStyle.Italic; break;
            case BibleText.Run.Classes.WordsOfJesus: span.Foreground = WordsOfJesusBrush; break;
            case BibleText.Run.Classes.SmallCaps: upperCase = true; size *= 0.85; span.FontSize = size; break;
            case BibleText.Run.Classes.Link:
                span.TextDecorations = TextDecorations.Underline;
                span.Foreground = LinkBrush;
                break;
            case BibleText.Run.Classes.Title:
            case BibleText.Run.Classes.Book:
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
            span.Inlines!.Add(new Avalonia.Controls.Documents.Run(upperCase ? run.Text.ToUpperInvariant() : run.Text));
        }

        if (span.Inlines!.Count > 0) yield return span;
        if (lineAfter) yield return new LineBreak();
    }

    /// <summary>The run's text without markup: its children's text when it has any, else its own.</summary>
    private static string PlainText(BibleText.Run run) =>
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
    private (string Text, List<Segment> Segments, Dictionary<Control, int> Anchors, List<NoteRange> Notes) Flatten()
    {
        var sb = new StringBuilder();
        var segs = new List<Segment>();
        var anchors = new Dictionary<Control, int>();
        var notes = new List<NoteRange>();
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
        return (sb.ToString(), segs, anchors, notes);
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
