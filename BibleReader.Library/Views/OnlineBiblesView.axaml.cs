using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BibleReader.Library.Downloads;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BibleReader.Library.UI;

/// <summary>One row of <see cref="OnlineBiblesView"/>: an eBible plus its install state.</summary>
public partial class OnlineBibleItem : ObservableObject
{
    public OnlineBibleItem(OnlineBible bible, OnlineBiblesView view)
    {
        Bible = bible;
        IsInstalled = CheckInstalled(bible);
        View = view;
    }

    OnlineBiblesView View { get; set; }
    public OnlineBible Bible { get; }

    public string Description =>
        string.Join(" · ", new[] { Bible.LanguageEnglishName, Bible.Language, Bible.CountryName, Bible.Origin }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall)), NotifyPropertyChangedFor(nameof(CanDelete)), NotifyPropertyChangedFor(nameof(IsConfirmingDelete)),
        NotifyPropertyChangedFor(nameof(IsInstalledAndWide))]
    private bool _isInstalled;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall)), NotifyPropertyChangedFor(nameof(CanDelete)), NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
    private bool _isBusy;

    /// <summary>Delete was clicked once: the row asks for a confirmation before it removes the Bible's files.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanDelete)), NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
    private bool _confirmDelete;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool CanInstall => !IsInstalled && !IsBusy;
    public bool CanDelete => IsInstalled && !IsBusy && !ConfirmDelete;
    public bool IsConfirmingDelete => IsInstalled && !IsBusy && ConfirmDelete;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsInstalledAndWide => IsInstalled && View.IsWide;

    /// <summary>Called by the view when <see cref="OnlineBiblesView.IsWide"/> changed: the bindings read it again.</summary>
    public void NotifyWideChanged() => OnPropertyChanged(nameof(IsInstalledAndWide));

    /// <summary>Installed means the Bible's folder holds converted books (the folder alone is created on access).</summary>
    private static bool CheckInstalled(OnlineBible bible)
    {
        if (string.IsNullOrEmpty(bible.EnglishName)) return false;
        var dir = bible.ConfigPath;
        return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.md").Any();
    }
}

/// <summary>
/// Lists the Bibles offered by ebible.org, filtered by language (and a name search), and installs the chosen
/// one with <see cref="eBible.Installl"/>. The layout is in OnlineBiblesView.axaml.
/// </summary>
public partial class OnlineBiblesView : UserControl
{
    private const string AllLanguages = "All languages";

    private List<OnlineBibleItem> _items = new();
    private List<OnlineBibleItem> _installed = new(); // the rows of the installed Bibles expander
    private bool _loading;

    /// <summary>Raised after a Bible was installed, so the host can add it to its list.</summary>
    public event EventHandler<OnlineBible>? BibleInstalled;

    /// <summary>Raised after a Bible was deleted, so the host can remove it from its list.</summary>
    public event EventHandler<OnlineBible>? BibleDeleted;

    /// <summary>The width above which the rows show the "Install" caption next to the button's icon.</summary>
    private const double WideWidth = 500;

    /// <summary>True while the view is wider than <see cref="WideWidth"/>.</summary>
    public static readonly StyledProperty<bool> IsWideProperty =
        AvaloniaProperty.Register<OnlineBiblesView, bool>(nameof(IsWide));

    public bool IsWide { get => GetValue(IsWideProperty); private set => SetValue(IsWideProperty, value); }

    static OnlineBiblesView()
    {
        // The rows' "Installed" caption depends on it through IsInstalledAndWide, which has to be announced per row.
        IsWideProperty.Changed.AddClassHandler<OnlineBiblesView>((view, _) =>
        {
            foreach (var item in view._items.Concat(view._installed)) item.NotifyWideChanged();
        });
    }

    public OnlineBiblesView()
    {
        InitializeComponent();
        DataContext = this;
        SizeChanged += (_, e) => IsWide = e.NewSize.Width > WideWidth;
        AttachedToVisualTree += async (_, _) =>
        {
            UpdateInstalled();
            if (_items.Count == 0) await LoadAsync();
        };
        // The page is kept while it is hidden, so what is installed may have changed since it was last shown.
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsVisible) UpdateInstalled(); };
    }

    /// <summary>Raised when the close button is clicked, so the host can show the text again.</summary>
    public event EventHandler? CloseRequested;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Fills the "Installed Bibles" expander from <see cref="Settings.Bibles"/>.</summary>
    private void UpdateInstalled()
    {
        // The same rows as the online list: an installed Bible can be deleted from here too.
        var items = (Settings.Current.Bibles ?? new List<Bible>())
            .OfType<OnlineBible>()
            .Where(b => !string.IsNullOrEmpty(b.EnglishName))
            .OrderBy(b => string.IsNullOrWhiteSpace(b.Name) ? b.EnglishName : b.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => new OnlineBibleItem(b, this))
            .ToList();
        _installed = items;
        InstalledList.ItemsSource = items;
        InstalledHeader.Text = $"Installed Bibles ({items.Count})";
    }

    // ---- Install from a file ------------------------------------------------------------------------

    private bool _installingFromFile;
    private TaskCompletionSource<string?>? _nameRequest;

    /// <summary>
    /// Picks a zip, 7z or xml file, asks for a name and installs it as a <see cref="BibleArchive"/>: the books are
    /// imported into the Bible's folder and the Bible is added to the settings like an online one.
    /// </summary>
    private async void OnInstallFromFileClick(object? sender, RoutedEventArgs e)
    {
        if (_installingFromFile || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        _installingFromFile = true;
        InstallFromFileButton.IsEnabled = false;
        string? temporary = null;
        BibleArchive? bible = null;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Install a Bible from a file",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Bible archives") { Patterns = new[] { "*.zip", "*.7z", "*.xml" } } },
            });
            if (files.Count == 0) return;
            var file = files[0];

            var name = await AskNameAsync(Path.GetFileNameWithoutExtension(file.Name));
            if (name is null) return;

            // Some platforms (Android) give a content address instead of a path: copy that to a temporary file.
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(file.Name));
                await using var source = await file.OpenReadAsync();
                await using var target = System.IO.File.Create(temporary);
                await source.CopyToAsync(target);
                path = temporary;
            }

            LoadSpinner.IsVisible = true;
            StatusText.Text = $"Installing {name}…";
            bible = new BibleArchive { File = path, Name = name, EnglishName = name, Language = "", LanguageEnglishName = "" };
            await bible.Installl();
            if (!Directory.EnumerateFiles(bible.ConfigPath, "*.md").Any())
                throw new InvalidDataException("No books could be imported from the file.");

            var settings = Settings.Current;
            settings.Bibles ??= new List<Bible>();
            settings.Bibles.Add(bible);
            settings.Save();
            UpdateInstalled();
            InstalledExpander.IsExpanded = true;
            StatusText.Text = $"{name} installed";
            BibleInstalled?.Invoke(this, bible);
            bible = null; // installed: keep its folder
        }
        catch (Exception ex)
        {
            StatusText.Text = "Install from file failed: " + ex.Message;
        }
        finally
        {
            // A failed install leaves a half filled folder behind: the name was free, so the folder is new.
            if (bible is not null)
                try { if (Directory.Exists(bible.ConfigPath)) Directory.Delete(bible.ConfigPath, true); } catch { }
            if (temporary is not null) try { System.IO.File.Delete(temporary); } catch { }
            LoadSpinner.IsVisible = _loading;
            InstallFromFileButton.IsEnabled = true;
            _installingFromFile = false;
        }
    }

    /// <summary>Shows the name panel; completes with the name, or null when it is cancelled.</summary>
    private Task<string?> AskNameAsync(string suggestion)
    {
        NameBox.Text = suggestion;
        NameError.IsVisible = false;
        NamePanel.IsVisible = true;
        NameBox.Focus();
        NameBox.SelectAll();
        _nameRequest = new TaskCompletionSource<string?>();
        return _nameRequest.Task;
    }

    private void FinishName(string? name)
    {
        NamePanel.IsVisible = false;
        var request = _nameRequest;
        _nameRequest = null;
        request?.TrySetResult(name);
    }

    private void OnNameOkClick(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        string? error =
            string.IsNullOrEmpty(name) ? "Please enter a name."
            : Settings.Current.Bibles?.Any(b => string.Equals(b.EnglishName, name, StringComparison.OrdinalIgnoreCase)) == true
                ? "A Bible with this name is installed already."
            : null;
        NameError.Text = error;
        NameError.IsVisible = error is not null;
        if (error is null) FinishName(name);
    }

    private void OnNameCancelClick(object? sender, RoutedEventArgs e) => FinishName(null);

    private void OnNameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter) OnNameOkClick(sender, e);
        else if (e.Key == Avalonia.Input.Key.Escape) FinishName(null);
    }

    private async void OnReloadClick(object? sender, RoutedEventArgs e) => await LoadAsync();

    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        LoadSpinner.IsVisible = true;
        StatusText.Text = "Loading Bibles from ebible.org…";
        try
        {
            var bibles = await Task.Run(async () => (await eBible.GetBiblesAsync()).ToList());
            _items = bibles.Select(b => new OnlineBibleItem(b, this)).ToList();
            FillLanguages();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not load the list: " + ex.Message;
        }
        finally
        {
            _loading = false;
            LoadSpinner.IsVisible = false;
        }
    }

    private void FillLanguages()
    {
        var languages = _items
            .Select(i => LanguageOf(i.Bible))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        int i = 0;
        // Remove duplicates
        while (i < languages.Count-1)
        {
            while (i < languages.Count - 1 && languages[i + 1].Contains(languages[i], StringComparison.OrdinalIgnoreCase)) languages.RemoveAt(i + 1);
            i++;
        }
        var selected = LanguageBox.SelectedItem as string;
        LanguageBox.ItemsSource = new[] { AllLanguages }.Concat(languages).ToList();
        LanguageBox.SelectedItem = selected is not null && ((IEnumerable<string>)LanguageBox.ItemsSource).Contains(selected)
            ? selected : AllLanguages;
    }

    private static string LanguageOf(OnlineBible bible) =>
        string.IsNullOrWhiteSpace(bible.LanguageEnglishName) ? bible.Language : bible.LanguageEnglishName;

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (LanguageBox is null || SearchBox is null || StatusText is null) return; // during InitializeComponent
        var language = LanguageBox.SelectedItem as string;
        var text = SearchBox.Text?.Trim();
        var filtered = _items.Where(i =>
            (language is null or AllLanguages || LanguageOf(i.Bible).Contains(language)) &&
            (string.IsNullOrEmpty(text) ||
             (i.Bible.Name?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
             (i.Bible.EnglishName?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)))
            .ToList();
        List.ItemsSource = filtered;
        if (_items.Count > 0) StatusText.Text = $"{filtered.Count} of {_items.Count} Bibles";
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OnlineBibleItem { CanDelete: true } item) item.ConfirmDelete = true;
    }

    private void OnCancelDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OnlineBibleItem item) item.ConfirmDelete = false;
    }

    /// <summary>Removes the Bible's folder (the converted books and the downloaded source) and its entry in the settings.</summary>
    private async void OnConfirmDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not OnlineBibleItem { IsConfirmingDelete: true } item) return;
        item.Error = null;
        item.IsBusy = true;
        try
        {
            var folder = item.Bible.ConfigPath;
            await Task.Run(() => { if (Directory.Exists(folder)) Directory.Delete(folder, true); });
            var settings = Settings.Current;
            settings.Bibles?.RemoveAll(b => b.EnglishName == item.Bible.EnglishName);
            settings.Save();
            item.IsInstalled = false;
            // The Bible may be shown in both lists (the rows are separate objects): the online one is no longer installed.
            foreach (var other in _items.Where(o => o.Bible.EnglishName == item.Bible.EnglishName)) other.IsInstalled = false;
            UpdateInstalled();
            BibleDeleted?.Invoke(this, item.Bible);
        }
        catch (Exception ex)
        {
            item.Error = "Delete failed: " + ex.Message;
        }
        finally
        {
            item.ConfirmDelete = false;
            item.IsBusy = false;
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not OnlineBibleItem item || !item.CanInstall) return;
        item.Error = null;
        item.IsBusy = true;
        try
        {
            await item.Bible.Installl();
            item.IsInstalled = true;
            var settings = Settings.Current;
            settings.Bibles ??= new List<Bible>();
            if (!settings.Bibles.OfType<eBible>().Any(b => b.EnglishName == item.Bible.EnglishName))
                settings.Bibles.Add(item.Bible);
            settings.Save();
            UpdateInstalled();
            BibleInstalled?.Invoke(this, item.Bible);
        }
        catch (Exception ex)
        {
            item.Error = "Install failed: " + ex.Message;
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}
