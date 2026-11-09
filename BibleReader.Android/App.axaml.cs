using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using BibleReader.Library;
using BibleReader.Library.UI;

namespace BibleReader.Android;

public partial class App : Application
{
    /// <summary>
    /// Folder with the Bible's markdown files (null result: none found); started by <see cref="BibleApplication"/>
    /// before the app starts, and awaited here so that the main thread is never blocked while it is prepared.
    /// </summary>
    public static string? BibleDirectory { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Avalonia sizes the app by the density of the screen but not by the "Font size" setting of Android, so it is
    /// read here (the full name: inside the BibleReader.Android namespace "Android.App" would resolve to this project).
    /// Changing the setting makes Android create the activity again, which makes the view again and reads it again.
    /// </summary>
    private static void ApplySystemFontScale()
    {
        if (Current is not { } app) return;
        float scale = global::Android.App.Application.Context.Resources?.Configuration?.FontScale ?? 1f;
        UiScale.SetSystemFactor(app, scale);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Settings.Current.Load(); // the language, the size and the Bibles that were chosen before
        ApplySystemFontScale();
        // A factory, not one view: Android creates the activity again (rotation, returning to a killed app), and a
        // view can only be in one place, so every creation needs a new one.
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = CreateMainView;

        // A change of language builds all the texts again: the view is replaced by a new one, which opens the Bible
        // where the reader was.
        Localizer.LanguageChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(OpenMainViewAgain);
        UiScale.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(OpenMainViewAgain);

        base.OnFrameworkInitializationCompleted();
    }

    private void OpenMainViewAgain()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime single) return;
        (single.MainView as BibleView)?.SaveStateNow();
        single.MainView = CreateMainView();
    }

    private static Control CreateMainView()
    {
        ApplySystemFontScale(); // the setting may have been changed while the app was away
        // On screen at once; the books arrive into it.
        var view = new BibleView();
        _ = OpenAsync(view);
        return view;
    }

    private static async Task OpenAsync(BibleView view)
    {
        string? directory = BibleDirectory is null ? null : await BibleDirectory;
        if (directory is null)
        {
            view.Content = new TextBlock
            {
                Text = "No Bible found. Add the Bible's .md files as assets (see BibleReader.Android.csproj) or push them to "
                     + "the app's files folder: Android/data/<package>/files/Bible.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }
        await view.OpenAsync(directory);
    }
}
