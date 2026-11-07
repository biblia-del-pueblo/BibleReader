using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BibleReader.Library.UI;

/// <summary>One row of <see cref="OnlineBiblesView"/>: an eBible plus its install state.</summary>
public partial class OnlineBibleItem : ObservableObject
{
    public OnlineBibleItem(OnlineBible bible)
    {
        Bible = bible;
        IsInstalled = CheckInstalled(bible);
    }

    public OnlineBible Bible { get; }

    public string Description =>
        string.Join(" · ", new[] { Bible.LanguageEnglishName, Bible.Language, Bible.CountryName }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall)), NotifyPropertyChangedFor(nameof(CanDelete)), NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
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
/// one with <see cref="eBible.DownloadBible"/>. The layout is in OnlineBiblesView.axaml.
/// </summary>
public partial class OnlineBiblesView : UserControl
{
    private const string AllLanguages = "All languages";

    private List<OnlineBibleItem> _items = new();
    private bool _loading;

    /// <summary>Raised after a Bible was installed, so the host can add it to its list.</summary>
    public event EventHandler<OnlineBible>? BibleInstalled;

    /// <summary>Raised after a Bible was deleted, so the host can remove it from its list.</summary>
    public event EventHandler<OnlineBible>? BibleDeleted;

    public OnlineBiblesView()
    {
        InitializeComponent();
        DataContext = this;
        AttachedToVisualTree += async (_, _) => { if (_items.Count == 0) await LoadAsync(); };
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
            _items = bibles.Select(b => new OnlineBibleItem(b)).ToList();
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
            await item.Bible.DownloadBible();
            item.IsInstalled = true;
            var settings = Settings.Current;
            settings.Bibles ??= new List<Bible>();
            if (!settings.Bibles.OfType<eBible>().Any(b => b.EnglishName == item.Bible.EnglishName))
                settings.Bibles.Add(item.Bible);
            settings.Save();
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
