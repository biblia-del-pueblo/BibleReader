using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using BibleReader.Library.UI;

namespace BibleReader.Android;

public partial class App : Application
{
    /// <summary>Folder with the Bible's markdown files; set by <see cref="MainActivity"/> before the app starts.</summary>
    public static string? BibleDirectory { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = CreateMainView();

        base.OnFrameworkInitializationCompleted();
    }

    private static Control CreateMainView()
    {
        if (BibleDirectory is null)
        {
            return new TextBlock
            {
                Text = "No Bible found. Add the Bible's .md files as assets (see BibleReader.Android.csproj) or push them to "
                     + "the app's files folder: Android/data/<package>/files/Bible.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var view = new BibleView();
        _ = view.OpenAsync(BibleDirectory); // reading and parsing the books runs off the UI thread
        return view;
    }
}
