using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Transactions;

namespace BibleReader.Library
{
    public class Settings
    {
        const string SettingsFile = "settings.json";
        public List<Bible> Bibles { get; set; }
        /// <summary>The <see cref="Bible.EnglishName"/> of the Bible that was open last; it is opened again at the start.</summary>
        public string? CurrentBible { get; set; }
        /// <summary>Where the reader was in <see cref="CurrentBible"/>: the number of the book (<c>Book.Index</c>), the chapter and the verse.</summary>
        public int CurrentBookIndex { get; set; }
        public int CurrentChapter { get; set; }
        public int CurrentVerse { get; set; }
        /// <summary>"System" (or null), "Light" or "Dark".</summary>
        public string? Theme { get; set; }
        public bool? ShowWordsOfJesus { get; set; }
        /// <summary>The user confirmed that there is no persecution of Christians in their country; only then are online Bibles listed.</summary>
        public bool InMyCountryThereIsNoPersecution { get; set; }
        /// <summary>Show the verse numbers in green (off when null).</summary>
        public bool? ShowVerseNumbersInGreen { get; set; }
        public double? FontSize { get; set; }
        public string FontFamily { get; set; } = "Bible Roman";
        public string ConfigPath
        {
            get
            {
                if (field == null)
                {
                    field = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrossBible");
                    Directory.CreateDirectory(field);
                }
                return field;
            }
            set
            {
                field = value;
                Directory.CreateDirectory(field);
            }
        }

        [JsonIgnore]
        public static Settings Current { get; private set; } = new Settings();

        public void Save()
        {
            var json = JsonConvert.SerializeObject(this, new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto,
            });
            var file = Path.Combine(ConfigPath, SettingsFile);
            File.WriteAllText(file, json);
        }
        public void Load()
        {
            var file = Path.Combine(ConfigPath, SettingsFile);
            if (!File.Exists(file)) return;
            try
            {
                var json = File.ReadAllText(file);
                var settings = JsonConvert.DeserializeObject<Settings>(json, new JsonSerializerSettings()
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    Converters = new List<JsonConverter> { new VersionConverter(), new StringEnumConverter() },
                    Formatting = Formatting.Indented
                });
                Bibles = settings.Bibles;
                CurrentBible = settings.CurrentBible;
                CurrentBookIndex = settings.CurrentBookIndex;
                CurrentChapter = settings.CurrentChapter;
                CurrentVerse = settings.CurrentVerse;
                Theme = settings.Theme;
                ShowWordsOfJesus = settings.ShowWordsOfJesus;
                InMyCountryThereIsNoPersecution = settings.InMyCountryThereIsNoPersecution;
                ShowVerseNumbersInGreen = settings.ShowVerseNumbersInGreen;
                FontSize = settings.FontSize;
                FontFamily = settings.FontFamily;
                ConfigPath = settings.ConfigPath;
            } catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex}");
            }
        }
    }
}
