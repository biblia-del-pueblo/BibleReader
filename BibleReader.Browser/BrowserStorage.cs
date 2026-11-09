using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using BibleReader.Library;

namespace BibleReader.Browser;

/// <summary>
/// The browser has no disk: <see cref="File"/> works on an in-memory file system that is gone when the page closes.
/// <see cref="Settings.ConfigPath"/> is a folder in it, and everything below it is kept in the browser's IndexedDB
/// (one record per file, keyed by its path relative to the folder): <see cref="RestoreAsync"/> puts the files back when
/// the page opens, and a timer copies what was added, changed or deleted since.
/// </summary>
internal static partial class BrowserStorage
{
    const string ConfigFolder = "/CrossBible";

    [JSImport("globalThis.bibleStorage.listKeys")]
    private static partial Task<string> ListKeys();

    [JSImport("globalThis.bibleStorage.getFile")]
    private static partial Task<string?> GetFile(string key); // base64: a byte array cannot be returned from a promise

    [JSImport("globalThis.bibleStorage.putFile")]
    private static partial Task PutFile(string key, byte[] data);

    [JSImport("globalThis.bibleStorage.deleteFile")]
    private static partial Task DeleteFile(string key);

    /// <summary>What is in IndexedDB: the files by key, with the size and time they had when they were copied.</summary>
    static readonly Dictionary<string, (long Length, DateTime Time)> Stored = new();
    static bool _syncing;

    public static async Task RestoreAsync()
    {
        var settings = Settings.Current;
        settings.ConfigPath = ConfigFolder; // creates the folder
        try
        {
            var keys = JsonSerializer.Deserialize<string[]>(await ListKeys()) ?? [];
            foreach (var key in keys)
            {
                if (await GetFile(key) is not { } text) continue;
                var data = Convert.FromBase64String(text);
                var file = ToPath(key);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, data);
                var info = new FileInfo(file);
                Stored[key] = (info.Length, info.LastWriteTimeUtc);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not read IndexedDB: {ex}"); }

        // Files are written as they change; they are copied over a moment later.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await SyncAsync();
        timer.Start();
    }

    private static string ToPath(string key) => Path.Combine(ConfigFolder, key.Replace('/', Path.DirectorySeparatorChar));

    private static string ToKey(string file) => Path.GetRelativePath(ConfigFolder, file).Replace('\\', '/');

    private static async Task SyncAsync()
    {
        if (_syncing) return; // the last copy is still running
        _syncing = true;
        try
        {
            var present = new HashSet<string>();
            foreach (var file in Directory.EnumerateFiles(ConfigFolder, "*", SearchOption.AllDirectories))
            {
                var key = ToKey(file);
                present.Add(key);
                var info = new FileInfo(file);
                var signature = (info.Length, info.LastWriteTimeUtc);
                if (Stored.TryGetValue(key, out var old) && old == signature) continue;
                await PutFile(key, await File.ReadAllBytesAsync(file));
                Stored[key] = signature;
            }
            foreach (var key in new List<string>(Stored.Keys))
            {
                if (present.Contains(key)) continue;
                await DeleteFile(key);
                Stored.Remove(key);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not write IndexedDB: {ex}"); }
        finally { _syncing = false; }
    }
}
