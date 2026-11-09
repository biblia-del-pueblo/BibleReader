using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Android.Provider;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace BibleReader.Android;

/// <summary>
/// The Android application: starts Avalonia with <see cref="App"/>. It runs before any activity, so this is where
/// the app builder is customised and where the Bible's files are located for <see cref="App"/>.
/// </summary>
// The full name: inside the BibleReader.Android namespace "Android.App" would resolve to this project's namespace.
[global::Android.App.Application]
public class BibleApplication : AvaloniaAndroidApplication<App>
{
    public BibleApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // The app starts right after this and needs the Bible's files. Copying them from the assets (once per
        // install) takes a while, so it runs on a worker thread and the app waits for it without blocking.
        App.BibleDirectory = PrepareBibleDirectory();
        if (App.BibleDirectory != null) Library.Settings.Current.ConfigPath = App.BibleDirectory;

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    /// <summary>
    /// Finds the folder with the Bible's markdown files, which the <c>Bible</c> class reads as ordinary files:
    /// first a folder pushed to the device, otherwise the Bible bundled as assets, copied to app storage once per install.
    /// </summary>
    private string? PrepareBibleDirectory()
    {
        // 1. A folder put on the device wins, e.g. adb push <folder> /sdcard/Android/data/<package>/files/Bible
        var external = GetExternalFilesDir(null)?.AbsolutePath;
        if (external is not null && Directory.Exists(external)) return external;

        var target = Path.Combine(FilesDir.AbsolutePath, "CrossBible");
        Directory.CreateDirectory(target);
        return target;
    }
}
