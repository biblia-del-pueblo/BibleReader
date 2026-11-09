using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;

using BibleReader.Browser;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // Before the app starts: the files that were saved in the browser are put back, and ConfigPath points at them.
        try
        {
            System.Console.WriteLine("Main: restoring files (build 3)");
            await BrowserStorage.RestoreAsync();
            System.Console.WriteLine("Main: files restored, starting Avalonia");
            await BuildAvaloniaApp()
                .WithInterFont()
                .StartBrowserAppAsync("out");
            System.Console.WriteLine("Main: Avalonia started");
        }
        catch (System.Exception ex)
        {
            System.Console.Error.WriteLine(ex); // the page shows it on the splash screen (see main.js)
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
