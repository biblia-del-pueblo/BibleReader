using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using BibleMarkdown;

namespace BibleReader.Library.Downloads
{
    /// <summary>
    /// The Bibles that the Biblia del Pueblo project publishes at https://biblia-del-pueblo.github.io/books: the output of
    /// <c>bibmark -publish</c> (see <c>BibleMarkdown.Publisher</c>), an index.json that lists a zip for every Bible, with the
    /// info.json that is in the zip. The zip holds the markdown books that the reader reads, so installing is
    /// unzipping it; nothing is converted.
    /// </summary>
    public class BibliaLibreBible: InstallableBook
    {
        public const string Url = "https://biblia-del-pueblo.github.io/books";
        const string IndexUrl = Url + "/index.json";

        public override string Origin => "BibliaLibre";
        protected virtual string DetailsUrl { get; set; }

        /// <summary>
        /// The Bibles of the index. Every entry is <c>{ "File": "Name.zip", "Info": { ... } }</c>; the info is an
        /// <see cref="InstallableBook"/> (name, language, country ...). A Bible whose info lacks a name takes the one of its zip.
        /// </summary>
        public static async Task<IEnumerable<InstallableBook>> GetBiblesAsync()
        {
            var index = JArray.Parse(await Download.DownloadStringAsync(IndexUrl));
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new StringEnumConverter() } });

            var bibles = new List<BibliaLibreBible>();
            foreach (var entry in index.OfType<JObject>())
            {
                var file = (string?)entry["File"];
                if (string.IsNullOrWhiteSpace(file)) continue;

                // A bad info is no reason to leave the Bible out: it is listed with what the zip's name tells.
                BibliaLibreBible bible;
                try { bible = (entry["Info"] as JObject)?.ToObject<BibliaLibreBible>(serializer) ?? new BibliaLibreBible(); }
                catch (JsonException) { bible = new BibliaLibreBible(); }

                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(bible.EnglishName)) bible.EnglishName = name;
                if (string.IsNullOrWhiteSpace(bible.Name)) bible.Name = bible.EnglishName;
                if (bible.Class == Classes.All) bible.Class = Classes.Bible; // "All" is the filter, not a kind of module
                bible.DetailsUrl = Url + "/" + Uri.EscapeDataString(file); // the address of the zip
                bibles.Add(bible);
            }
            return bibles;
        }

        /// <summary>Downloads the zip into the folder of the Bible; its files are the books, ready to be read.</summary>
        public override async Task Install()
        {
            if (string.IsNullOrEmpty(DetailsUrl)) throw new InvalidOperationException("The address of the Bible is not set.");
            var srcpath = Path.Combine(ConfigPath, "src");
            await Task.Run(async () =>
            {
                await Download.DownloadAndUnzip(DetailsUrl, srcpath);
                var transform = new Transform(ConfigPath, srcpath);
                transform.Import.All();
                if (!Directory.EnumerateFiles(ConfigPath, "*.md").Any() &&
                    !Directory.EnumerateFiles(ConfigPath, "*.db").Any())
                    throw new InvalidDataException("The Bible was downloaded, but it has no books.");
                transform.Index.Create();
                Directory.Delete(srcpath, true);
            });
        }
    }
}
