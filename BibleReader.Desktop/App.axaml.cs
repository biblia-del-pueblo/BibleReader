using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using BibleReader.Library;
using BibleReader.Library.UI;

namespace BibleReader.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Register the SVG plugin so XAML knows how to parse SVGs
        System.GC.KeepAlive(typeof(SvgImageExtension));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        // A change of language builds all the texts again: a new main window is opened (in the place of the old one,
        // which is closed), and it opens the Bible where the reader was.
        Localizer.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(OpenMainWindowAgain);

        base.OnFrameworkInitializationCompleted();
    }

    private void OpenMainWindowAgain()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is not { } old) return;
        old.FindControl<BibleView>("bibleView")?.SaveStateNow(); // the new window opens at the saved position

        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = old.Position,
            Width = old.ClientSize.Width,
            Height = old.ClientSize.Height,
        };
        if (old.WindowState != WindowState.Minimized) window.WindowState = old.WindowState;

        // The new window is the main window before the old one closes: the app ends with its last window.
        desktop.MainWindow = window;
        window.Show();
        old.Close();
    }
}
