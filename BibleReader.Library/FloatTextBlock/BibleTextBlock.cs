using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Metadata;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;

namespace BibleReader.Library.UI;

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

    public static readonly AttachedProperty<FloatSide> SideProperty =
        AvaloniaProperty.RegisterAttached<BibleTextBlock, Control, FloatSide>("Side");

    static BibleTextBlock()
    {
        AffectsMeasure<BibleTextBlock>(TextProperty, FontSizeProperty, FontFamilyProperty,
            TextAlignmentProperty, FloatSpacingProperty, ColumnCountProperty, ColumnWidthProperty, ColumnSpacingProperty);
        AffectsArrange<BibleTextBlock>(SideProperty);
        AffectsMeasure<BibleTextBlock>(ForegroundProperty);
        SideProperty.Changed.AddClassHandler<Control>((c, _) => (c.Parent as BibleTextBlock)?.InvalidateMeasure());
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public TextAlignment TextAlignment { get => GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public double FloatSpacing { get => GetValue(FloatSpacingProperty); set => SetValue(FloatSpacingProperty, value); }

    public int ColumnCount { get => GetValue(ColumnCountProperty); set => SetValue(ColumnCountProperty, value); }
    public double ColumnWidth { get => GetValue(ColumnWidthProperty); set => SetValue(ColumnWidthProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }

    public static FloatSide GetSide(Control control) => control.GetValue(SideProperty);
    public static void SetSide(Control control, FloatSide value) => control.SetValue(SideProperty, value);

    private readonly record struct PlacedLine(TextLine Line, Point Origin);

    private readonly List<PlacedLine> _lines = new();
    private readonly List<Rect> _floatRects = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        bool widthFinite = !double.IsInfinity(availableSize.Width);
        bool heightFinite = !double.IsInfinity(availableSize.Height);
        double totalWidth = widthFinite ? availableSize.Width : 400;
        double spacing = ColumnSpacing;
        bool fixedWidth = !double.IsNaN(ColumnWidth) && ColumnWidth > 0;
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
            columns = Math.Max(1, ColumnCount);
            width = Math.Max(1, (totalWidth - (columns - 1) * spacing) / columns);
        }
        // The text is first flowed as ONE tall column of this width; it is cut into columns afterwards.
        _width = width;
        int usedColumns = 1;
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
        foreach (var child in Children.OfType<Control>().Where(c => c != _layer && c != _popup))
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

            int index = 0;
            while (index < text.Length)
            {
                // Probe with full width to learn the line height, then recompute with the real band.
                var probe = formatter.FormatLine(source, index, width, paraProps);
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

                var line = formatter.FormatLine(source, index, lineWidth, paraProps);
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
        double contentHeight = flowHeight;
        if ((columns > 1 || unbounded) && _lines.Count > 0)
        {
            double step = _lines.Min(l => l.Line.Height);
            // Finite height: newspaper-style, fill column 1 first. Infinite height: balance the columns.
            double h = heightFinite ? availableSize.Height : flowHeight / columns;
            h = Math.Max(h, step);
            List<double>? starts;
            while ((starts = SplitColumns(h, columns)) is null) h += step;
            usedColumns = starts.Count(x => x < double.MaxValue);

            double pitch = width + spacing;
            double Top(int col) => starts[col];
            int ColumnAt(double y)
            {
                int c = 0;
                for (int i = 1; i < starts.Count; i++) if (starts[i] <= y) c = i;
                return c;
            }

            contentHeight = 0;
            var columned = new List<PlacedLine>(_lines.Count);
            foreach (var pl in _lines)
            {
                int c = ColumnAt(pl.Origin.Y);
                var origin = new Point(c * pitch + pl.Origin.X, pl.Origin.Y - Top(c));
                columned.Add(pl with { Origin = origin });
                contentHeight = Math.Max(contentHeight, origin.Y + pl.Line.Height);
            }
            _lines.Clear();
            _lines.AddRange(columned);

            for (int i = 0; i < _arranged.Count; i++)
            {
                var (child, slot) = _arranged[i];
                int c = ColumnAt(slot.Y);
                slot = new Rect(c * pitch + slot.X, slot.Y - Top(c), slot.Width, slot.Height);
                _arranged[i] = (child, slot);
                contentHeight = Math.Max(contentHeight, slot.Bottom);
            }
        }

        double contentWidth = fixedWidth ? usedColumns * width + (usedColumns - 1) * spacing : totalWidth;
        var total = new Size(contentWidth, contentHeight);
        _layer.Measure(total);
        _layer.InvalidateVisual();
        return total;
    }

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
                var style = segments.First(seg => index >= seg.Start && index < seg.End).Style with { Baseline = BaselineAlignment.Baseline };
                return new GapRun(natural + extra, height, baseline, ToRunProperties(style));
            }

            foreach (var seg in segments)
            {
                if (index < seg.Start || index >= seg.End) continue;
                int stop = Math.Min(seg.End, lineEnd);
                for (int i = index; i < stop; i++)
                    if (spaces.ContainsKey(i)) { stop = i; break; }
                return new TextCharacters(text.AsMemory(index, stop - index), ToRunProperties(seg.Style));
            }
            return null;
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
            child.Arrange(slot);
        return finalSize;
    }

    /// <summary>Draws the already-laid-out lines. Panel.Render is sealed, so a child control does the painting.</summary>
    private sealed class TextLayer(BibleTextBlock owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            owner.DrawSelection(context);
            foreach (var placed in owner._lines)
                placed.Line.Draw(context, placed.Origin);
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
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key == Key.C) { Copy(); e.Handled = true; }
        else if (e.Key == Key.A) { SelectAll(); e.Handled = true; }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
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
        if (!_selecting) return;
        _selecting = false;
        e.Pointer.Capture(null);
    }

    // Light dismiss closes a popup that opens during the very press that is being handled, so the popup is
    // shown from a posted callback that runs after this press has been fully processed.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(this);
        if (HitFootnote(point, out var bounds) is { } note)
        {
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
        InvalidateMeasure();
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
    public void Parse(IEnumerable<BibleText.Run> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Inlines.Clear();
        foreach (var inline in ToInlines(text, FontSize, upperCase: false))
            Inlines.Add(inline);
    }

    private static IEnumerable<Inline> ToInlines(IEnumerable<BibleText.Run>? runs, double size, bool upperCase)
    {
        foreach (var run in runs ?? [])
            foreach (var inline in ToInlines(run, size, upperCase))
                yield return inline;
    }

    private static IEnumerable<Inline> ToInlines(BibleText.Run run, double size, bool upperCase)
    {
        switch (run.Class)
        {
            case BibleText.Run.Classes.Footnote:
            {
                // The popup content keeps its structure, so links/references inside a footnote stay styled.
                var note = new Footnote();
                if (run.Runs?.Any() == true)
                    foreach (var child in ToInlines(run.Runs, size, upperCase))
                        note.Inlines!.Add(child);
                else if (!string.IsNullOrEmpty(run.Text))
                    note.Inlines!.Add(new Avalonia.Controls.Documents.Run(run.Text));
                yield return note;
                yield break;
            }

            case BibleText.Run.Classes.Chapter:
                yield return new FloatRun(run.Chapter.ToString())
                {
                    Side = FloatSide.Left,
                    FontSize = size * 2.4,
                    FontWeight = FontWeight.Bold,
                };
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

    /// <summary>Serves the flattened text to the formatter one styled run at a time.</summary>
    private sealed class RichTextSource(string text, List<Segment> segments) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= text.Length) return null;
            foreach (var seg in segments)
            {
                if (textSourceIndex < seg.Start || textSourceIndex >= seg.End) continue;
                return new TextCharacters(text.AsMemory(textSourceIndex, seg.End - textSourceIndex), ToRunProperties(seg.Style));
            }
            return null;
        }
    }
}
