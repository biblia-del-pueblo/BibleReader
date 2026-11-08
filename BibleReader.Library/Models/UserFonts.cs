using Avalonia.Media;
using Avalonia.Media.Fonts;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace BibleReader.Library;

/// <summary>
/// Fonts the user installed. The font files live in <c>Settings.ConfigPath/Fonts</c>; they are loaded into an Avalonia
/// font collection of their own, from which <see cref="FamilyOf"/> builds a <see cref="FontFamily"/>.
/// </summary>
public static class UserFonts
{
    private static readonly string[] Extensions = { ".ttf", ".otf", ".ttc" };
    private static readonly Uri CollectionKey = new("fonts:BibleReaderUser");

    private sealed class Collection : FontCollectionBase
    {
        public override Uri Key => CollectionKey;
    }

    private static Collection? _collection;
    private static readonly HashSet<string> _families = new(StringComparer.OrdinalIgnoreCase);

    public static string Folder => Path.Combine(Settings.Current.ConfigPath, "Fonts");

    /// <summary>The file types the font picker offers.</summary>
    public static IReadOnlyList<string> PickerPatterns { get; } = new[] { "*.ttf", "*.otf", "*.ttc", "*.zip" };

    /// <summary>The families of the installed fonts, sorted.</summary>
    public static IReadOnlyList<string> Families
    {
        get { EnsureLoaded(); return _families.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList(); }
    }

    public static bool Contains(string? family)
    {
        EnsureLoaded();
        return family is not null && _families.Contains(family);
    }

    /// <summary>The font family for an installed font's name.</summary>
    public static FontFamily FamilyOf(string family) => new(CollectionKey.OriginalString + "#" + family);

    /// <summary>Registers the collection and loads the files that are already in the folder (once).</summary>
    private static void EnsureLoaded()
    {
        if (_collection is not null) return;
        _collection = new Collection();
        FontManager.Current.AddFontCollection(_collection);
        var folder = Folder;
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder).Where(IsFont))
        {
            try { Add(File.ReadAllBytes(file)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not load the font {file}: {ex.Message}"); }
        }
    }

    private static bool IsFont(string name) =>
        Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    private static string? Add(byte[] data)
    {
        if (_collection is { } c && c.TryAddGlyphTypeface(new MemoryStream(data), out var glyphTypeface))
        {
            _families.Add(glyphTypeface.FamilyName);
            return glyphTypeface.FamilyName;
        }
        return null;
    }

    /// <summary>
    /// Installs a font file, or every font in a zip, into <see cref="Folder"/>.
    /// </summary>
    /// <returns>The family names of the fonts that were installed (empty when the file held no usable font).</returns>
    public static List<string> Install(string fileName, Stream content)
    {
        EnsureLoaded();
        Directory.CreateDirectory(Folder);
        var installed = new List<string>();

        void Install(string name, byte[] data)
        {
            name = Path.GetFileName(name); // never a path from inside an archive
            if (string.IsNullOrEmpty(name) || !IsFont(name)) return;
            if (Add(data) is not { } family) return; // not a font Avalonia can read: do not keep it
            File.WriteAllBytes(Path.Combine(Folder, name), data);
            if (!installed.Contains(family)) installed.Add(family);
        }

        if (Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries.Where(e => IsFont(e.Name)))
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                Install(entry.Name, memory.ToArray());
            }
        }
        else
        {
            using var memory = new MemoryStream();
            content.CopyTo(memory);
            Install(fileName, memory.ToArray());
        }
        return installed;
    }
}
