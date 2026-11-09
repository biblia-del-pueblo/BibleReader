using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BibleReader.Library.Downloads;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
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

    /// <summary>
    /// True while the online Bibles are shown instead of the text. Set by the "Online Bibles…" menu entry; the host
    /// (see <see cref="BibleView"/>) binds to it to swap its content. Choosing a chapter resets it.
    /// </summary>
    public static readonly StyledProperty<bool> ShowOnlineBiblesProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(ShowOnlineBibles));
    /// <summary>The Bible chosen in the Bibles popup. Bind a <see cref="BibleTextBlock.Bible"/> to it to read it.</summary>
    public static readonly StyledProperty<Bible?> BibleProperty =
        AvaloniaProperty.Register<BibleToolbar, Bible?>(nameof(Bible));

    /// <summary>True while the settings page is shown instead of the text. Set by the "Settings…" menu entry.</summary>
    public static readonly StyledProperty<bool> ShowSettingsProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(ShowSettings));

    /// <summary>True while neither the online Bibles nor the settings are shown: the host shows the text then.</summary>
    public static readonly StyledProperty<bool> ShowTextProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(ShowText), true);

    public bool ShowOnlineBibles { get => GetValue(ShowOnlineBiblesProperty); set => SetValue(ShowOnlineBiblesProperty, value); }
    public bool ShowSettings { get => GetValue(ShowSettingsProperty); set => SetValue(ShowSettingsProperty, value); }
    public bool ShowText { get => GetValue(ShowTextProperty); private set => SetValue(ShowTextProperty, value); }
    private const double WideWidth = 500;

    /// <summary>True while the view is wider than <see cref="WideWidth"/>.</summary>
    public static readonly StyledProperty<bool> IsWideProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(IsWide));

    public bool IsWide { get => GetValue(IsWideProperty); private set => SetValue(IsWideProperty, value); }

    public BibleTextBlock? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public Bible.VersePosition? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    public Bible? Bible
    {
        get => GetValue(BibleProperty);
        set => SetValue(BibleProperty, value);
    }

    private DateTime _closedAt = DateTime.MinValue;
    private DateTime _menuClosedAt = DateTime.MinValue;
    private DateTime _biblesClosedAt = DateTime.MinValue;
    private DateTime _strongsClosedAt = DateTime.MinValue;

    private Bible.Book? _chaptersFor; // the book whose chapters the popup shows (null: the book page)

    static BibleToolbar()
    {
        TargetProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
            bar.OnTargetChanged(e.OldValue as BibleTextBlock, e.NewValue as BibleTextBlock));
        PositionProperty.Changed.AddClassHandler<BibleToolbar>((bar, _) => bar.UpdateText());
        IsWideProperty.Changed.AddClassHandler<BibleToolbar>((bar, _) => bar.UpdateText());
        // The online Bibles and the settings replace the text, one at a time.
        ShowOnlineBiblesProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
        {
            if (e.NewValue is true) bar.ShowSettings = false;
            bar.ShowText = !bar.ShowOnlineBibles && !bar.ShowSettings;
        });
        ShowSettingsProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
        {
            if (e.NewValue is true) bar.ShowOnlineBibles = false;
            bar.ShowText = !bar.ShowOnlineBibles && !bar.ShowSettings;
        });
    }

    /// <summary>
    /// Set when the app opens its main window again (after a change of language): the next toolbar shows the settings page
    /// that the reader was on, not the text.
    /// </summary>
    public static bool ReturnToSettings { get; set; }

    public BibleToolbar()
    {
        InitializeComponent();
        SizeChanged += (_, e) => IsWide = e.NewSize.Width > WideWidth;
        if (ReturnToSettings) { ReturnToSettings = false; ShowSettings = true; }
        FlowDirection = Localizer.FlowDirection; // mirrored for Arabic and Hebrew
        MenuPopup.PlacementTarget = MenuButton;
        BiblesPopup.PlacementTarget = BiblesButton;
        StrongsPopup.PlacementTarget = StrongsButton;
        SearchPopup.PlacementTarget = SearchButton;
        // Tunnelled: the presses are seen before the button and the popup that it light-dismisses can handle them.
        StrongsButton.AddHandler(PointerPressedEvent, OnStrongsPressed, RoutingStrategies.Tunnel);
        StrongsButton.AddHandler(PointerReleasedEvent, OnStrongsReleased, RoutingStrategies.Tunnel);
        StrongsButton.AddHandler(PointerCaptureLostEvent, OnStrongsReleased);
        // A long press or double click on the back or forward button, or a long press on the position (the only way in a narrow bar), lists the history.
        foreach (var button in new[] { BackButton, ForwardButton, PositionButton })
        {
            button.AddHandler(PointerPressedEvent, OnHistoryPressed, RoutingStrategies.Tunnel);
            button.AddHandler(PointerReleasedEvent, OnHistoryReleased, RoutingStrategies.Tunnel);
            button.AddHandler(PointerCaptureLostEvent, OnHistoryReleased);
        }
        // A horizontal swipe anywhere on the bar goes back or forward in the history. Tunnelled, to see the gesture
        // whichever button it starts on.
        AddHandler(PointerPressedEvent, OnSwipePressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnSwipeMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnSwipeReleased, RoutingStrategies.Tunnel);
        BookPopup.PlacementTarget = PositionButton; // a reference to a named sibling, set here rather than as a binding
        UpdateHistoryButtons(); // the history of the last session
        UpdateStrongsSelected();
        UpdateText();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BibleReader.Library.Downloads.StrongsDictionaries.AnyChanged += OnStrongsAnyChanged;
        UpdateStrongsSelected(); // it may have changed while the bar was away
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        BibleReader.Library.Downloads.StrongsDictionaries.AnyChanged -= OnStrongsAnyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    // Raised when a dictionary is switched on or off, installed or deleted, possibly on another thread.
    private void OnStrongsAnyChanged(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateStrongsSelected);

    /// <summary>The Strongs button is selected while any of the Strong's dictionaries of <see cref="Settings.Bibles"/> is switched on.</summary>
    private void UpdateStrongsSelected() =>
        StrongsButton.Classes.Set("selected", StrongsDictionariesInstalled().Any(d => d.IsEnabled));

    private void OnTargetChanged(BibleTextBlock? old, BibleTextBlock? target)
    {
        if (old is not null)
        {
            old.PropertyChanged -= OnTargetPropertyChanged;
            old.RemoveHandler(PointerPressedEvent, OnTextSwipePressed);
            old.RemoveHandler(PointerMovedEvent, OnSwipeMoved);
            old.RemoveHandler(PointerReleasedEvent, OnSwipeReleased);
        }
        if (target is not null)
        {
            target.PropertyChanged += OnTargetPropertyChanged;
            // A swipe on the text goes through the history too (tunnelled: the text block handles its own presses).
            target.AddHandler(PointerPressedEvent, OnTextSwipePressed, RoutingStrategies.Tunnel);
            target.AddHandler(PointerMovedEvent, OnSwipeMoved, RoutingStrategies.Tunnel);
            target.AddHandler(PointerReleasedEvent, OnSwipeReleased, RoutingStrategies.Tunnel);
        }
        Position = target?.Position;
    }

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BibleTextBlock.PositionProperty) Position = Target?.Position;
    }

    private void OnPositionClick(object? sender, RoutedEventArgs e)
    {
        if (_historyLongPressed) { _historyLongPressed = false; return; } // the long press listed the history: the release is no click
        if (BookPopup.IsOpen) { BookPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; don't reopen it with the same click.
        if ((DateTime.UtcNow - _closedAt).TotalMilliseconds < 300) return;
        if (Target?.Books is not { } books) return;

        ShowBooks(books);
        BookPopup.IsOpen = true;
    }
    
    /// <summary>The menu: copy the current reference or verse text, and switch between reading and the online Bibles.</summary>
    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        if (MenuPopup.IsOpen) { MenuPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; don't reopen it with the same click.
        if ((DateTime.UtcNow - _menuClosedAt).TotalMilliseconds < 300) return;

        MenuItems.Children.Clear();
        /*if (Position is { Book: not null } position)
        {
            AddMenuEntry("copy.svg", "Copy reference", () => CopyAsync(ReferenceOf(position)));
            // Only a verse has a text; the start of a book or chapter has none.
            if (VerseTextOf(position) is { Length: > 0 } text)
                AddMenuEntry("copy.svg", "Copy verse text", () => CopyAsync(text));
        }*/
        AddMenuEntry(ShowOnlineBibles ? null : "download.svg", ShowOnlineBibles ? Localizer.Get("MenuBackToReading") : Localizer.Get("MenuInstallBibles"), () =>
        {
            ShowOnlineBibles = !ShowOnlineBibles;
            return Task.CompletedTask;
        });
        AddMenuEntry(ShowSettings ? null : "gear.svg", ShowSettings ? Localizer.Get("MenuBackToReading") : Localizer.Get("MenuSettings"), () =>
        {
            ShowSettings = !ShowSettings;
            return Task.CompletedTask;
        });
        MenuPopup.IsOpen = true;
    }

    // ---- History ---------------------------------------------------------------------------------------

    // The history lives in the settings (saved when the app ends, see Settings.SaveOnExit), oldest entry first.
    private static List<Settings.HistoryEntry> _history => Settings.Current.History ??= new();
    private static int _historyIndex
    {
        get => Math.Clamp(Settings.Current.HistoryIndex, -1, _history.Count - 1);
        set => Settings.Current.HistoryIndex = value;
    }
    private bool _historyLongPressed;
    private Control? _historyHoldTarget;
    private Avalonia.Threading.DispatcherTimer? _historyHold;

    private static Settings.HistoryEntry EntryOf(Bible.VersePosition p) => new() { BookIndex = p.Book.Index, Chapter = p.Chapter, Verse = p.Verse };

    /// <summary>The book of the entry in the Bible that is read (the history is of positions, not of Bibles).</summary>
    private Bible.Book? BookOf(Settings.HistoryEntry entry) => Target?.Books?.FirstOrDefault(b => b.Index == entry.BookIndex);

    /// <summary>
    /// Notes where the reader is now in the current entry, so that coming back lands there. Scrolling does not make
    /// history: only going somewhere does. A place in another chapter than the entry's becomes an entry of its own.
    /// </summary>
    private void SnapshotCurrent()
    {
        if (Position is not { Book: not null } position) return;
        int current = _historyIndex;
        if (current >= 0 && _history[current].BookIndex == position.Book.Index && _history[current].Chapter == position.Chapter)
        {
            _history[current].Verse = position.Verse;
            return;
        }
        _history.Insert(current + 1, EntryOf(position));
        _historyIndex = current + 1;
    }

    /// <summary>
    /// Adds the place that the reader is about to go to (a chapter, a search result) as the newest entry; the entries ahead
    /// of the current one are dropped. Call it before the text block is moved.
    /// </summary>
    private void PushHistory(Bible.VersePosition target)
    {
        if (target.Book is null) return;
        SnapshotCurrent();
        int current = _historyIndex;
        if (current >= 0 && _history[current].BookIndex == target.Book.Index && _history[current].Chapter == target.Chapter)
            _history[current].Verse = target.Verse;
        else
        {
            _history.RemoveRange(current + 1, _history.Count - current - 1);
            _history.Add(EntryOf(target));
            _historyIndex = _history.Count - 1;
        }
        UpdateHistoryButtons();
        SaveHistory();
    }

    /// <summary>Saves the settings (off the UI thread), with the history, when the reader goes somewhere (not for every verse that scrolls by).</summary>
    private static async void SaveHistory()
    {
        try { await Settings.Current.SaveAsync(); } // the file is written off the UI thread
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
    }

    private void UpdateHistoryButtons()
    {
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    }

    private void GoToHistory(int index)
    {
        if (index < 0 || index >= _history.Count || Target is null) return;
        var entry = _history[index];
        if (BookOf(entry) is not { } book) return;
        SnapshotCurrent(); // where the reader is now; may add an entry, which moves the one that is wanted
        _historyIndex = _history.IndexOf(entry);
        ShowOnlineBibles = false;
        ShowSettings = false;
        Target.Position = new Bible.VersePosition { Book = book, Chapter = entry.Chapter, Verse = entry.Verse };
        UpdateHistoryButtons();
        SaveHistory();
    }

    private object? _backPending;      // the step back that waits for a possible second press
    private bool _historyOpenOnRelease; // the second press of a double click on the back button

    /// <summary>
    /// A click goes back one step, unless a second press follows: a double click (like a long press) lists the history
    /// instead, so the step waits for as long as a second press may take (as the Strongs button does).
    /// </summary>
    private void OnBackHistoryClick(object? sender, RoutedEventArgs e) => _ = StepHistoryAsync(BackButton, -1);

    private void OnForwardHistoryClick(object? sender, RoutedEventArgs e) => _ = StepHistoryAsync(ForwardButton, 1);

    private async Task StepHistoryAsync(Button button, int step)
    {
        if (_historyLongPressed) { _historyLongPressed = false; return; } // the long press opened the list: the release is no click
        // The list opens when the second press ends: a popup that opens during a press is light-dismissed by it.
        if (_historyOpenOnRelease) { _historyOpenOnRelease = false; OpenHistoryPopup(button); return; }
        var pending = _backPending = new object();
        await Task.Delay(StrongsDoubleClickDelay);
        if (!ReferenceEquals(pending, _backPending)) return;
        GoToHistory(_historyIndex + step);
    }

    private void OnHistoryPressed(object? sender, PointerPressedEventArgs e)
    {
        _historyLongPressed = false;
        _historyHoldTarget = sender as Control;
        _historyOpenOnRelease = false;
        if ((ReferenceEquals(sender, BackButton) || ReferenceEquals(sender, ForwardButton)) && e.ClickCount == 2)
        {
            _backPending = null; // the step back that waited does not happen
            _historyOpenOnRelease = true;
            _historyHold?.Stop();
            return;
        }
        _historyHold ??= new Avalonia.Threading.DispatcherTimer { Interval = LongPressDelay };
        _historyHold.Tick -= OnHistoryHeld;
        _historyHold.Tick += OnHistoryHeld;
        _historyHold.Stop();
        _historyHold.Start();
    }

    private void OnHistoryHeld(object? sender, EventArgs e)
    {
        _historyHold?.Stop();
        if (_historyHoldTarget is { IsEnabled: false }) return; // a disabled back button has nothing to go back to
        _historyLongPressed = true; // the release that follows is not a click
        OpenHistoryPopup(_historyHoldTarget ?? PositionButton);
    }

    private void OnHistoryReleased(object? sender, RoutedEventArgs e) => _historyHold?.Stop();

    private const double SwipeDistance = 40;
    private Point _swipeStart;
    private bool _swipeTracked;

    /// <summary>Starts to track a swipe, unless the press is in a popup of the bar (the book grid, the lists).</summary>
    private void OnSwipePressed(object? sender, PointerPressedEventArgs e)
    {
        _historyLongPressed = false; // from here on, a release is a click again
        _swipeTracked = e.Source is Visual source && ReferenceEquals(TopLevel.GetTopLevel(source), TopLevel.GetTopLevel(this));
        _swipeStart = e.GetPosition(this);
    }

    /// <summary>
    /// A swipe on the text: only a finger or a pen, since a mouse drag selects text, and only while the text has a single
    /// column, since with more a horizontal drag turns the pages.
    /// </summary>
    private void OnTextSwipePressed(object? sender, PointerPressedEventArgs e)
    {
        OnSwipePressed(sender, e);
        if (e.Pointer.Type == PointerType.Mouse || Target?.IsSingleColumn != true) _swipeTracked = false;
    }

    /// <summary>A swipe is mostly horizontal and long enough; <paramref name="dx"/> is how far the pointer went from the press.</summary>
    private bool IsSwipe(PointerEventArgs e, out double dx)
    {
        var moved = e.GetPosition(this) - _swipeStart;
        dx = moved.X;
        return _swipeTracked && Math.Abs(moved.X) >= SwipeDistance && Math.Abs(moved.X) > 2 * Math.Abs(moved.Y);
    }

    /// <summary>Once the pointer has swiped, the press is no long press any more.</summary>
    private void OnSwipeMoved(object? sender, PointerEventArgs e)
    {
        if (_historyHold is { IsEnabled: true } && IsSwipe(e, out _)) _historyHold.Stop();
    }

    private void OnSwipeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsSwipe(e, out double dx) || _historyLongPressed) { _swipeTracked = false; return; }
        _swipeTracked = false;
        _historyLongPressed = true; // the release is no click on the back or position button
        // To the right goes back, to the left forward; the other way round in a mirrored bar.
        bool right = dx > 0;
        if (FlowDirection == FlowDirection.RightToLeft) right = !right;
        GoToHistory(_historyIndex + (right ? -1 : 1));
    }

    /// <summary>
    /// Lists the history, the newest first; choosing an entry goes to it. From the forward button it lists only what
    /// is ahead of the position, the nearest first.
    /// </summary>
    private void OpenHistoryPopup(Control anchor)
    {
        HistoryItems.Children.Clear();
        bool ahead = ReferenceEquals(anchor, ForwardButton);
        for (int n = 0; n < _history.Count; n++)
        {
            int i = ahead ? _historyIndex + 1 + n : _history.Count - 1 - n;
            if (i < 0 || i >= _history.Count) break;

            int index = i;
            // The book, and chapter:verse beside it (the start of a chapter is verse 1).
            var entry = _history[i];
            var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock { Text = ShortenWithEllipsis(BookOf(entry)?.Name ?? string.Empty, 30) });
            content.Children.Add(new TextBlock { Text = $"{Math.Max(1, entry.Chapter)}:{Math.Max(1, entry.Verse)}", Opacity = 0.7 });
            var button = new Button { Content = content, };
            button.Classes.Add("entry");
            if (i == _historyIndex) button.Classes.Add("current");
            button.Click += (_, _) => { HistoryPopup.IsOpen = false; GoToHistory(index); };
            HistoryItems.Children.Add(button);
        }
        if (HistoryItems.Children.Count == 0)
            HistoryItems.Children.Add(new TextBlock { Text = Localizer.Get("NoHistory"), Opacity = 0.7, Margin = new Thickness(12, 8) });
        HistoryPopup.PlacementTarget = anchor;
        HistoryPopup.IsOpen = true;
    }

    /// <summary>Lists the installed Bibles (<see cref="Settings.Bibles"/>); choosing one sets <see cref="Bible"/>.</summary>
    private void OnBiblesClick(object? sender, RoutedEventArgs e)
    {
        if (BiblesPopup.IsOpen) { BiblesPopup.IsOpen = false; return; }
        if ((DateTime.UtcNow - _biblesClosedAt).TotalMilliseconds < 300) return;

        BiblesItems.Children.Clear();
        var bibles = Settings.Current.Bibles ?? new List<Bible>();
        string? current = Position?.Book?.Bible?.Name;
        // Strong's dictionaries are not read like a Bible: they are switched on and off in the Strongs list.
        foreach (var bible in bibles.Where(b => !string.IsNullOrEmpty(b.EnglishName) && b is not IStrongDictionary))
        {
            var chosen = bible;
            var button = new Button { Content = ShortenWithEllipsis(chosen.Name ?? chosen.EnglishName, 40) };
            button.Classes.Add("entry");
            if (chosen.EnglishName == current) button.Classes.Add("current");
            button.Click += (_, _) =>
            {
                BiblesPopup.IsOpen = false;
                ShowOnlineBibles = false;
                ShowSettings = false;
                if (Bible != chosen) Bible = chosen;
            };
            BiblesItems.Children.Add(button);
        }
        if (BiblesItems.Children.Count == 0)
            BiblesItems.Children.Add(new TextBlock { Text = Localizer.Get("NoBiblesInstalled"), Opacity = 0.7, Margin = new Thickness(12, 8) });
        BiblesPopup.IsOpen = true;
    }

    private void OnBiblesPopupClosed(object? sender, EventArgs e) => _biblesClosedAt = DateTime.UtcNow;

    /// <summary>
    /// A click on the Strongs button switches all the installed Strong's dictionaries on, or, when they all are on,
    /// off. A double click or a long press opens the list, in which every dictionary is switched on or off by itself.
    /// </summary>
    private async void OnStrongsClick(object? sender, RoutedEventArgs e)
    {
        if (_strongsLongPressed) { _strongsLongPressed = false; return; } // the long press opened the list: the release is no click
        if (_strongsOpenOnRelease) { _strongsOpenOnRelease = false; OpenStrongsPopup(); return; } // the end of a double click
        if (StrongsPopup.IsOpen) { StrongsPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; this click is not a toggle.
        if ((DateTime.UtcNow - _strongsClosedAt).TotalMilliseconds < 300) return;

        // A click toggles only when no second press follows: for a double click the list opens instead.
        var pending = _strongsPending = new object();
        await Task.Delay(StrongsDoubleClickDelay);
        if (!ReferenceEquals(pending, _strongsPending)) return;
        SetAllStrongs(!StrongsDictionariesInstalled().All(d => d.IsEnabled));
    }

    /// <summary>
    /// Lists the installed Strong's dictionaries (the <see cref="IStrongDictionary"/> entries of
    /// <see cref="Settings.Bibles"/>); checking one sets its <see cref="IStrongDictionary.IsEnabled"/>.
    /// </summary>
    private void OpenStrongsPopup()
    {
        StrongsItems.Children.Clear();
        _strongsBoxes.Clear();
        var dictionaries = StrongsDictionariesInstalled();
        if (dictionaries.Count > 0)
        {
            // "All" switches all the dictionaries on, or, when they all are, off; it is unchecked when none is on and
            // shows a dash when some are.
            _strongsAllBox = new CheckBox { Content = Localizer.Get("All"), IsThreeState = true, Margin = new Thickness(4, 2), FontWeight = FontWeight.SemiBold };
            _strongsAllBox.Click += (_, _) => SetAllStrongs(!dictionaries.All(d => d.IsEnabled));
            StrongsItems.Children.Add(_strongsAllBox);
            StrongsItems.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 2), Opacity = 0.3, Background = Brushes.Gray });
        }
        foreach (var dictionary in dictionaries)
        {
            var chosen = dictionary;
            var name = (chosen as Bible) is { } book && !string.IsNullOrEmpty(book.Name ?? book.EnglishName)
                ? book.Name ?? book.EnglishName : chosen.GetType().Name;
            var box = new CheckBox { Content = ShortenWithEllipsis(name, 40), IsChecked = chosen.IsEnabled, Margin = new Thickness(4, 2) };
            box.IsCheckedChanged += (_, _) =>
            {
                if (_syncingStrongs) return;
                chosen.IsEnabled = box.IsChecked == true;
                StrongsDictionariesChanged();
            };
            _strongsBoxes.Add((chosen, box));
            StrongsItems.Children.Add(box);
        }
        if (dictionaries.Count == 0)
            StrongsItems.Children.Add(new TextBlock { Text = Localizer.Get("NoStrongsInstalled"), Opacity = 0.7, Margin = new Thickness(4, 8) });
        SyncStrongsBoxes();
        StrongsPopup.IsOpen = true;
    }

    private readonly List<(IStrongDictionary Dictionary, CheckBox Box)> _strongsBoxes = new();
    private CheckBox? _strongsAllBox;
    private bool _syncingStrongs;

    private static List<IStrongDictionary> StrongsDictionariesInstalled() =>
        (Settings.Current.Bibles ?? new List<Bible>()).OfType<IStrongDictionary>().ToList();

    /// <summary>A dictionary was switched on or off: tell <see cref="StrongsDictionaries.Any"/>, save, and show the state of the list.</summary>
    private void StrongsDictionariesChanged()
    {
        StrongsDictionaries.Refresh(); // whatever the dictionary does itself, Any is told that one was switched on or off
        try { Settings.Current.Save(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
        SyncStrongsBoxes();
    }

    /// <summary>Switches all the installed dictionaries on or off.</summary>
    private void SetAllStrongs(bool enabled)
    {
        var dictionaries = StrongsDictionariesInstalled();
        if (dictionaries.Count == 0) return;
        foreach (var dictionary in dictionaries) dictionary.IsEnabled = enabled;
        StrongsDictionariesChanged();
    }

    /// <summary>Shows the state of the dictionaries in the check boxes, without the boxes changing the dictionaries back.</summary>
    private void SyncStrongsBoxes()
    {
        _syncingStrongs = true;
        try
        {
            foreach (var (dictionary, box) in _strongsBoxes) box.IsChecked = dictionary.IsEnabled;
            if (_strongsAllBox is not null)
                _strongsAllBox.IsChecked = _strongsBoxes.All(b => b.Dictionary.IsEnabled) ? true
                    : _strongsBoxes.Any(b => b.Dictionary.IsEnabled) ? null : false;
        }
        finally { _syncingStrongs = false; }
    }

    private const ulong DoubleClickMilliseconds = 400;
    private static readonly TimeSpan StrongsDoubleClickDelay = TimeSpan.FromMilliseconds(420); // a little more than the time for a second press
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);
    private object? _strongsPending;      // the toggle that waits for a possible second press
    private ulong _strongsPressedAt;
    private bool _strongsLongPressed, _strongsOpenOnRelease;
    private Avalonia.Threading.DispatcherTimer? _strongsHold;

    /// <summary>
    /// Two presses of the button in quick succession, or one that is held, open the list. The presses are timed here,
    /// not taken from the Tapped events, which do not tell a double tap reliably next to a popup.
    /// </summary>
    private void OnStrongsPressed(object? sender, PointerPressedEventArgs e)
    {
        _strongsLongPressed = false;
        _strongsOpenOnRelease = false;
        if (_strongsPressedAt != 0 && e.Timestamp - _strongsPressedAt < DoubleClickMilliseconds)
        {
            _strongsPressedAt = 0;
            _strongsPending = null; // the toggle that waited does not happen
            // The list opens when this press ends: a popup that opens during a press is light-dismissed by it.
            _strongsOpenOnRelease = true;
            return;
        }
        _strongsPressedAt = e.Timestamp;

        // Held long enough, it opens the list while the finger is still down.
        _strongsHold ??= new Avalonia.Threading.DispatcherTimer { Interval = LongPressDelay };
        _strongsHold.Tick -= OnStrongsHeld;
        _strongsHold.Tick += OnStrongsHeld;
        _strongsHold.Stop();
        _strongsHold.Start();
    }

    private void OnStrongsHeld(object? sender, EventArgs e)
    {
        _strongsHold?.Stop();
        _strongsLongPressed = true; // the release that follows is not a click
        _strongsPending = null;
        _strongsPressedAt = 0;
        OpenStrongsPopup();
    }

    /// <summary>The press ended (or was taken away): it was not held long enough.</summary>
    private void OnStrongsReleased(object? sender, RoutedEventArgs e) => _strongsHold?.Stop();

    private void OnStrongsPopupClosed(object? sender, EventArgs e) => _strongsClosedAt = DateTime.UtcNow;

    // ---- Search ----------------------------------------------------------------------------------------

    private DateTime _searchClosedAt = DateTime.MinValue;
    private object? _searchRunning; // the search whose results are wanted: an older one that ends later is dropped

    private void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        if (SearchPopup.IsOpen) { SearchPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; don't reopen it with the same click.
        if ((DateTime.UtcNow - _searchClosedAt).TotalMilliseconds < 300) return;
        SearchPopup.IsOpen = true;
    }

    private void OnSearchPopupOpened(object? sender, EventArgs e) => SearchBox.Focus();

    private void OnSearchPopupClosed(object? sender, EventArgs e) => _searchClosedAt = DateTime.UtcNow;

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = SearchAsync();
    }

    private void OnSearchGoClick(object? sender, RoutedEventArgs e) => _ = SearchAsync();

    /// <summary>
    /// Searches the Bible that is shown for the words in the box (<see cref="InstallableBook.Search"/>) and lists the
    /// verses, the best match first, with the words in bold. Choosing a verse goes to it.
    /// </summary>
    private async Task SearchAsync()
    {
        var phrase = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(phrase)) return;
        SearchStatus.IsVisible = true;

        var shown = Position?.Book?.Bible ?? Bible;
        if (shown is null) { SearchStatus.Text = Localizer.Get("OpenBibleFirst"); return; }

        // The Bible that is read is a plain Bible; the installed one of the same folder does the search.
        var searchable = (Settings.Current.Bibles ?? new List<Bible>()).OfType<InstallableBook>()
            .FirstOrDefault(b => b.EnglishName == shown.EnglishName || b.EnglishName == shown.Name)
            ?? new InstallableBook { EnglishName = shown.EnglishName ?? shown.Name, Name = shown.Name };

        var running = _searchRunning = new object();
        SearchResults.Children.Clear();
        SearchStatus.Text = Localizer.Get("Searching");
        List<InstallableBook.SearchResult> results;
        try { results = await searchable.Search(phrase); }
        catch (Exception ex)
        {
            if (ReferenceEquals(running, _searchRunning)) SearchStatus.Text = Localizer.Format("SearchFailed", ex.Message);
            return;
        }
        if (!ReferenceEquals(running, _searchRunning)) return;

        SearchStatus.Text = results.Count == 0 ? Localizer.Get("NoVersesFound") : Localizer.Format("VersesFound", results.Count);
        foreach (var result in results)
        {
            var found = result;
            var verse = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = UiScale.Size(14) };
            foreach (var inline in BibleTextBlock.MarkdownInlines(BibleMarkdown.WordIndex.WithoutFootnotes(found.Markdown), UiScale.Size(14)))
                verse.Inlines!.Add(inline);
            var content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = $"{found.Position.Book.Name} {found.Position.Chapter}:{found.Position.Verse}", FontWeight = FontWeight.SemiBold, FontSize = UiScale.Size(12), Opacity = 0.7 },
                    verse,
                },
            };
            var button = new Button { Content = content };
            button.Classes.Add("entry");
            button.Click += (_, _) => GoToSearchResult(found);
            SearchResults.Children.Add(button);
        }
    }

    private void GoToSearchResult(InstallableBook.SearchResult result)
    {
        SearchPopup.IsOpen = false;
        ShowOnlineBibles = false;
        ShowSettings = false;
        if (Target is null) return;
        // the book of the text block (the search made its own list of the books of the folder)
        var book = Target.Books?.FirstOrDefault(b => b.Index == result.Position.Book.Index) ?? result.Position.Book;
        var place = new Bible.VersePosition { Book = book, Chapter = result.Position.Chapter, Verse = result.Position.Verse };
        PushHistory(place);
        Target.Position = place;
    }

    private void AddMenuEntry(string? image, string header, Func<Task> action)
    {
        Button button;
        if (image == null) button = new Button { Content = header };
        else
        {
            var panel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal };
            var svg = new ThemedSvg { Path = $"/Assets/{image}", Size = 18 }; // coloured for the theme, sized for the interface
            panel.Children.Add(svg);
            panel.Children.Add(new TextBlock() { Text = header, Margin = new Thickness(8, 0, 0, 0) });
            button = new Button { Content = panel };
        }
        button.Classes.Add("entry");
        button.Click += async (_, _) =>
        {
            MenuPopup.IsOpen = false;
            await action();
        };
        MenuItems.Children.Add(button);
    }

    private void OnMenuPopupClosed(object? sender, EventArgs e) => _menuClosedAt = DateTime.UtcNow;

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
        ShowOnlineBibles = false; // a chapter is read in the text, not in the online list
        ShowSettings = false;
        if (Target is null) return;
        // Verse 0 is the start of the chapter; the text block loads the book if it is not shown yet.
        var place = new Bible.VersePosition { Book = book, Chapter = chapter, Verse = 0 };
        PushHistory(place);
        Target.Position = place;
    }

    private static readonly FontFamily GreekFont = new("avares://BibleReader.Library/Assets#Cardo");
    private static readonly FontFamily HebrewFont = new("avares://BibleReader.Library/Assets#Frank Ruehl CLM");

    /// <summary>Alpha, beta and aleph, bet: the language of the Strong's numbers of the testament that is read.</summary>
    private void UpdateStrongsLetters(Bible.Book? book)
    {
        // The Old Testament has Hebrew Strong's numbers (H), the New Testament Greek ones (G). The books of the
        // Apocrypha are Greek; front and back matter say nothing, so the letters stay as they are.
        bool? hebrew = book?.Class switch
        {
            Bible.Book.Classes.Pentateuch or Bible.Book.Classes.Historical or Bible.Book.Classes.PoetryAndWisdom
                or Bible.Book.Classes.MajorProphets or Bible.Book.Classes.MinorProphets => true,
            Bible.Book.Classes.Gospels or Bible.Book.Classes.Acts or Bible.Book.Classes.Epistles
                or Bible.Book.Classes.Revelation or Bible.Book.Classes.Apocrypha => false,
            _ => null,
        };
        if (hebrew is null && !string.IsNullOrEmpty(StrongsLetters.Text)) return;
        StrongsLetters.FontFamily = hebrew == true ? HebrewFont : GreekFont;
        StrongsLetters.FontSize = UiScale.Size(hebrew == true ? 30 : 20);
        StrongsLetters.Text = hebrew == true ? "אב" : "αβ";
    }

    private Bible? _watchedBible;

    /// <summary>Follows <see cref="Bible.HasStrongs"/> of the Bible that is shown, so the Strongs button appears as soon as it is known.</summary>
    private void WatchBible(Bible? bible)
    {
        if (ReferenceEquals(bible, _watchedBible)) return;
        if (_watchedBible is not null) _watchedBible.PropertyChanged -= OnBiblePropertyChanged;
        _watchedBible = bible;
        if (bible is not null) bible.PropertyChanged += OnBiblePropertyChanged;
    }

    private void OnBiblePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A book is read on any thread, the button belongs to the UI thread.
        if (e.PropertyName == nameof(Bible.HasStrongs)) Avalonia.Threading.Dispatcher.UIThread.Post(UpdateText);
    }

    private void UpdateText()
    {
        var position = Position;
        WatchBible(position?.Book?.Bible);
        BackButton.IsVisible = ForwardButton.IsVisible = IsWide;
        // The Strongs button is only there for a Bible that has Strong's numbers (known once a book of it has been read).
        StrongsButton.IsVisible = position?.Book?.Bible?.HasStrongs == true;
        UpdateStrongsLetters(position?.Book);
        BookName.Text = ShortenWithEllipsis(position?.Book?.Name ?? string.Empty, IsWide ? 30 : 20);
        BibleName.Text = ShortenWithEllipsis(position?.Book?.Bible?.Name ?? string.Empty, IsWide ? 35 : 25);
        ReferenceText.Text = position is null ? "—"
            : position.Verse > 0 ? $"{position.Chapter}:{position.Verse}"
            : position.Chapter > 0 ? position.Chapter.ToString() : string.Empty;
    }

    private string ShortenWithEllipsis(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }
}
