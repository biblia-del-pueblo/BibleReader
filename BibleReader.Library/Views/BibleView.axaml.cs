using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Runtime.CompilerServices;

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
        // Remember the open Bible, and open the remembered one when the view is first shown.
        Block.PropertyChanged += (_, e) =>
        {
            if (e.Property == BibleTextBlock.PositionProperty) { SavePositionSoon(); return; }
            if (e.Property != BibleTextBlock.BibleProperty) return;
            var name = (e.NewValue as Bible)?.EnglishName;
            if (Settings.Current.CurrentBible == name) return;
            Settings.Current.CurrentBible = name;
            try { Settings.Current.Save(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
        };
        AttachedToVisualTree += (_, _) => RestoreBible();

        // Pinching with two fingers changes the font size; the single finger still scrolls (see BibleTextBlock).
        Block.GestureRecognizers.Add(new Avalonia.Input.PinchGestureRecognizer());
        Block.AddHandler(Avalonia.Input.InputElement.PinchEvent, OnPinch);
        Block.AddHandler(Avalonia.Input.InputElement.PinchEndedEvent, OnPinchEnded);
    }

    public const double MinFontSize = 12, MaxFontSize = 40;
    private double _pinchStartSize;

    /// <summary>
    /// <see cref="Avalonia.Input.PinchEventArgs.Scale"/> is the spread of the fingers relative to where the pinch started,
    /// so the size is the one at the start times it. The text itself is only built again a moment after the fingers stop
    /// moving (<see cref="OnAppearanceChanged"/>).
    /// </summary>
    private void OnPinch(object? sender, Avalonia.Input.PinchEventArgs e)
    {
        if (_pinchStartSize <= 0) _pinchStartSize = Settings.Current.FontSize ?? SettingsView.DefaultFontSize;
        double size = Math.Round(Math.Clamp(_pinchStartSize * e.Scale, MinFontSize, MaxFontSize));
        e.Handled = true;
        if (Settings.Current.FontSize == size) return;
        Settings.Current.FontSize = size;
        OnAppearanceChanged(this, EventArgs.Empty);
    }

    private void OnPinchEnded(object? sender, Avalonia.Input.PinchEndedEventArgs e)
    {
        _pinchStartSize = 0;
        try { Settings.Current.Save(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
    }

    private bool _restored;
    private Avalonia.Threading.DispatcherTimer? _saveTimer;

    /// <summary>Saves the position a second after it stopped changing: it changes with every scroll.</summary>
    private void SavePositionSoon()
    {
        _saveTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick -= OnSaveTimer;
        _saveTimer.Tick += OnSaveTimer;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Saves the position at once (it is saved a second after the last change otherwise), for a view that is about to be
    /// replaced by a new one, which opens the Bible at the saved position.
    /// </summary>
    public void SaveStateNow()
    {
        _saveTimer?.Stop();
        SavePosition(force: true);
    }

    private void OnSaveTimer(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        SavePosition(force: false);
    }

    private void SavePosition(bool force)
    {
        if (Block.IsBusy) { if (!force) SavePositionSoon(); return; } // still opening: the position is not the reader's yet
        if (Block.Bible is null || Block.Position is not { Book: { } book } position) return;
        var settings = Settings.Current;
        if (settings.CurrentBible != Block.Bible.EnglishName) return; // the Bible is being switched
        if (settings.CurrentBookIndex == book.Index && settings.CurrentChapter == position.Chapter && settings.CurrentVerse == position.Verse) return;
        settings.CurrentBookIndex = book.Index;
        settings.CurrentChapter = position.Chapter;
        settings.CurrentVerse = position.Verse;
        try { settings.Save(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
    }

    private void RestoreBible()
    {
        if (_restored) return;
        _restored = true;
        SettingsView.ApplyTheme(Settings.Current.Theme);
        ApplyAppearance(); // before the first Bible is opened below, so its text is built with these
        if (Block.Bible is not null || Settings.Current.CurrentBible is not { Length: > 0 } name) return;
        if (Settings.Current.Bibles?.FirstOrDefault(b => b.EnglishName == name) is not { } bible) return;
        var settings = Settings.Current;
        Block.OpenNextAt(settings.CurrentBookIndex, settings.CurrentChapter, settings.CurrentVerse);
        Toolbar.Bible = bible; // the text block follows, and opens at the saved position
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

    public IReadOnlyList<Bible.Book>? Books
    {
        get => Block.Books;
        set => Block.Books = value;
    }
    /// <summary>The Bible shown; the text block follows the toolbar's <see cref="BibleToolbar.Bible"/>.</summary>
    public Bible? Bible
    {
        get => Block.Bible;
        set => Block.Bible = value;
    }

    public async Task ShowBookAsync(int n)=> await Block.ShowBookAsync(n);

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

    private Avalonia.Threading.DispatcherTimer? _appearanceTimer;

    /// <summary>Takes the font size, font and words of Jesus from the settings (the text is not rebuilt here).</summary>
    private void ApplyAppearance()
    {
        var settings = Settings.Current;
        Block.FontSize = settings.FontSize ?? SettingsView.DefaultFontSize;
        Block.FontFamily = SettingsView.FamilyOf(settings.FontFamily);
        Block.ShowWordsOfJesus = settings.ShowWordsOfJesus ?? true;
        Block.ShowVerseNumbersInGreen = settings.ShowVerseNumbersInGreen ?? false;
        Block.Hyphenate = settings.Hyphenate ?? true;
    }

    /// <summary>The settings changed: the text is built again a moment after the last change (a slider fires often).</summary>
    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        _appearanceTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _appearanceTimer.Tick -= OnAppearanceTimer;
        _appearanceTimer.Tick += OnAppearanceTimer;
        _appearanceTimer.Stop();
        _appearanceTimer.Start();
    }

    private async void OnAppearanceTimer(object? sender, EventArgs e)
    {
        _appearanceTimer?.Stop();
        ApplyAppearance();
        try { await Block.RebuildAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not rebuild the text: {ex}"); }
    }

    /// <summary>The close button of the online Bibles or the settings: back to the text.</summary>
    private void OnCloseOverlay(object? sender, EventArgs e)
    {
        Toolbar.ShowOnlineBibles = false;
        Toolbar.ShowSettings = false;
    }

    /// <summary>A deleted Bible can no longer be read: when it is the one shown, the toolbar and the text block drop it.</summary>
    private void OnBibleDeleted(object? sender, InstallableBook bible)
    {
        if (Toolbar.Bible?.EnglishName == bible.EnglishName) Toolbar.Bible = null;
        if (Block.Bible?.EnglishName == bible.EnglishName) Block.Bible = null; // also when it was opened with OpenAsync
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
    public async Task OpenAsync(string directory, int bookIndex = -1) => await OpenAsync(await Task.Run(() => Bible.OpenFrom(directory)), bookIndex);

    public async Task OpenAsync(Bible bible, int bookIndex = -1)
    {
        var books = bible.Books;
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
