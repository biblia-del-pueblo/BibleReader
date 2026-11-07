using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace BibleReader.Library.UI;

/// <summary>
/// A bar that shows where a <see cref="BibleTextBlock"/> currently is ("Genesis 3:5"). Place it above the text
/// block and point <see cref="Target"/> at it; the bar follows the text block's <see cref="BibleTextBlock.Position"/>.
/// Clicking the position opens a grid of the text block's <see cref="BibleTextBlock.Books"/>; choosing a book shows
/// a grid of its chapters, and choosing a chapter scrolls the text block to it.
/// The layout and styling are in BibleToolbar.axaml.
/// </summary>
/// <example>
/// <code>
/// &lt;StackPanel&gt;
///   &lt;local:BibleToolbar Target="{Binding #text}"/&gt;
///   &lt;local:BibleTextBlock x:Name="text"/&gt;
/// &lt;/StackPanel&gt;
/// </code>
/// </example>
public partial class BibleToolbar : UserControl
{
    /// <summary>The text block whose position is shown and which the book list navigates.</summary>
    public static readonly StyledProperty<BibleTextBlock?> TargetProperty =
        AvaloniaProperty.Register<BibleToolbar, BibleTextBlock?>(nameof(Target));

    /// <summary>The position shown. Follows <see cref="Target"/>; can also be set directly.</summary>
    public static readonly StyledProperty<Bible.VersePosition?> PositionProperty =
        AvaloniaProperty.Register<BibleToolbar, Bible.VersePosition?>(nameof(Position));

    public BibleTextBlock? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public Bible.VersePosition? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    private DateTime _closedAt = DateTime.MinValue;
    private Bible.Book? _chaptersFor; // the book whose chapters the popup shows (null: the book page)

    static BibleToolbar()
    {
        TargetProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
            bar.OnTargetChanged(e.OldValue as BibleTextBlock, e.NewValue as BibleTextBlock));
        PositionProperty.Changed.AddClassHandler<BibleToolbar>((bar, _) => bar.UpdateText());
    }

    public BibleToolbar()
    {
        InitializeComponent();
        BookPopup.PlacementTarget = PositionButton; // a reference to a named sibling, set here rather than as a binding
        UpdateText();
    }

    private void OnTargetChanged(BibleTextBlock? old, BibleTextBlock? target)
    {
        if (old is not null) old.PropertyChanged -= OnTargetPropertyChanged;
        if (target is not null) target.PropertyChanged += OnTargetPropertyChanged;
        Position = target?.Position;
    }

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BibleTextBlock.PositionProperty) Position = Target?.Position;
    }

    private void OnPositionClick(object? sender, RoutedEventArgs e)
    {
        if (BookPopup.IsOpen) { BookPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; don't reopen it with the same click.
        if ((DateTime.UtcNow - _closedAt).TotalMilliseconds < 300) return;
        if (Target?.Books is not { } books) return;

        ShowBooks(books);
        BookPopup.IsOpen = true;
    }
    
    /// <summary>The menu of the current position: copy its reference ("Genesis 1:9") or its verse text.</summary>
    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        if (Position is not { Book: not null } position) return;

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Copy reference", () => CopyAsync(ReferenceOf(position))));
        // Only a verse has a text; the start of a book or chapter has none.
        if (VerseTextOf(position) is { Length: > 0 } text)
            menu.Items.Add(CreateMenuItem("Copy verse text", () => CopyAsync(text)));
        menu.Open(MenuButton);
    }

    private static MenuItem CreateMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    /// <summary>"Genesis 1:9", "Genesis 1" or "Genesis", as far as the position goes.</summary>
    private static string ReferenceOf(Bible.VersePosition position)
    {
        string name = position.Book?.Name ?? string.Empty;
        return position.Verse > 0 ? $"{name} {position.Chapter}:{position.Verse}"
            : position.Chapter > 0 ? $"{name} {position.Chapter}"
            : name;
    }

    /// <summary>The words of the verse, without footnotes or markup; empty when the position is not a verse.</summary>
    private static string VerseTextOf(Bible.VersePosition position)
    {
        if (position.Book is not { } book || position.Chapter <= 0 || position.Verse <= 0) return string.Empty;
        var verse = book.Runs
            .Where(run => run.Class == Bible.Run.Classes.Chapter && run.Chapter == position.Chapter)
            .SelectMany(chapter => chapter.Runs ?? [])
            .FirstOrDefault(run => run.Class == Bible.Run.Classes.Verse && run.Position?.Verse == position.Verse);
        return verse is null ? string.Empty : Regex.Replace(PlainText(verse), @"\s+", " ").Trim();

        static string PlainText(Bible.Run run) =>
            run.Class == Bible.Run.Classes.Footnote ? string.Empty
            : run.Runs?.Any() == true ? string.Concat(run.Runs.Select(PlainText))
            : run.Text ?? string.Empty;
    }

    private void OnPopupClosed(object? sender, EventArgs e) => _closedAt = DateTime.UtcNow;

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (Target?.Books is { } books) ShowBooks(books);
    }

    /// <summary>First page of the popup: one button per book.</summary>
    private void ShowBooks(IReadOnlyList<Bible.Book> books)
    {
        _chaptersFor = null;
        ChapterHeader.IsVisible = false;
        BookGrid.Children.Clear();
        // Front matter (table of contents, preface, glossary…) has no chapters to scroll to.
        foreach (var book in books.Where(book => !IsFrontMatter(book)))
            BookGrid.Children.Add(CreateButton(Abbreviation(book), book.Name, ReferenceEquals(book, Position?.Book), () => ShowChapters(book), book.Class.ToString()));
    }

    /// <summary>Second page of the popup: one button per chapter of <paramref name="book"/>.</summary>
    private async void ShowChapters(Bible.Book book)
    {
        _chaptersFor = book;
        TitleText.Text = book.Name;
        ChapterHeader.IsVisible = true;
        BookGrid.Children.Clear();
        BookGrid.Children.Add(new TextBlock { Text = "…", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(8) });

        // The chapters are counted from the markdown headings: far cheaper than parsing the whole book.
        List<int> chapters;
        try { chapters = await Task.Run(() => ChapterNumbers(book)); }
        catch { chapters = new List<int>(); }
        if (!ReferenceEquals(_chaptersFor, book) || !BookPopup.IsOpen) return; // went back or closed in the meantime

        if (chapters.Count == 0) { SelectChapter(book, 1); return; }
        BookGrid.Children.Clear();
        // Chapter 0 is the book's heading, above chapter 1.
        int current = ReferenceEquals(book, Position?.Book) ? Math.Max(1, Position!.Chapter) : -1;
        foreach (int chapter in chapters)
        {
            int number = chapter;
            BookGrid.Children.Add(CreateButton(number.ToString(), null, number == current, () => SelectChapter(book, number)));
        }
    }

    private static List<int> ChapterNumbers(Bible.Book book) =>
        Regex.Matches(book.Markdown, @"(?:^|\n)#\s+(?<n>[0-9]+)\s*\r?\n")
            .Select(m => int.Parse(m.Groups["n"].Value))
            .Distinct()
            .ToList();

    private static string Abbreviation(Bible.Book book)
    {
        if (!string.IsNullOrWhiteSpace(book.Abbrevation)) return book.Abbrevation;
        string name = book.Name ?? string.Empty;
        return name.Length > 3 ? name[..3] : name;
    }

    /// <summary>One cell of a grid; the look comes from the <c>cell</c> and <c>current</c> styles in the AXAML.</summary>
    private static Button CreateButton(string text, string? tip, bool isCurrent, Action onClick, string? category = null)
    {
        var button = new Button { Content = text };
        button.Classes.Add("cell");
        if (category is not null) button.Classes.Add(category); // the colour per Bible.Book.Classes value, see the AXAML
        if (isCurrent) button.Classes.Add("current");
        if (tip is not null) ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static bool IsFrontMatter(Bible.Book book) =>
        System.IO.Path.GetFileName(book.File ?? string.Empty).StartsWith("00.", StringComparison.Ordinal);

    private void SelectChapter(Bible.Book book, int chapter)
    {
        BookPopup.IsOpen = false;
        if (Target is null) return;
        // Verse 0 is the start of the chapter; the text block loads the book if it is not shown yet.
        Target.Position = new Bible.VersePosition { Book = book, Chapter = chapter, Verse = 0 };
    }

    private void UpdateText()
    {
        var position = Position;
        BookText.Text = position?.Book?.Name ?? string.Empty;
        ReferenceText.Text = position is null ? "—"
            : position.Verse > 0 ? $"{position.Chapter}:{position.Verse}"
            : position.Chapter > 0 ? position.Chapter.ToString() : string.Empty;
    }
}
