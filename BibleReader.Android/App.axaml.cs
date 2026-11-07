using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using BibleReader.Library.UI;

namespace BibleReader.Android;

public partial class App : Application
{
    /// <summary>
    /// Folder with the Bible's markdown files (null result: none found); started by <see cref="BibleApplication"/>
    /// before the app starts, and awaited here so that the main thread is never blocked while it is prepared.
    /// </summary>
    public static Task<string?>? BibleDirectory { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // A factory, not one view: Android creates the activity again (rotation, returning to a killed app), and a
        // view can only be in one place, so every creation needs a new one.
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = CreateMainView;

        base.OnFrameworkInitializationCompleted();
    }

    private static Control CreateMainView()
    {
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
