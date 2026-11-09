using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BibleReader.Library;
using BibleReader.Library.UI;

namespace BibleReader.Browser;

/// <summary>The app in the browser: its main view is the <see cref="BibleView"/>.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            System.Console.WriteLine("App: initialized");
            BrowserStorage.StartSync();
            Settings.Current.Load(); // the language, the size and the Bibles that were chosen before
            if (ApplicationLifetime is ISingleViewApplicationLifetime single)
                single.MainView = new BibleView();
            System.Console.WriteLine("App: BibleView created");
        }
        catch (System.Exception ex)
        {
            System.Console.Error.WriteLine($"App: {ex}");
            throw;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
