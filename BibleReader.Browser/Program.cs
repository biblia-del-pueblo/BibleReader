using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;

using BibleReader.Browser;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // Before the app starts: the files that were saved in the browser are put back, and ConfigPath points at them.
        await BrowserStorage.RestoreAsync();
        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
