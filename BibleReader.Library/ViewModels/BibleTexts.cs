using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;
using IO = System.IO;

namespace BibleReader.Library;

public class Bible
{
    internal static readonly Dictionary<int, Book.Classes> ClassByIndex = new Dictionary<int, Book.Classes>()
    {
        {0, Book.Classes.Prefix },
        {1, Book.Classes.Pentateuch },
        {2, Book.Classes.Pentateuch },
        {3, Book.Classes.Pentateuch },
        {4, Book.Classes.Pentateuch },
        {5, Book.Classes.Pentateuch },
        {6, Book.Classes.Historical },
        {7, Book.Classes.Historical },
        {8, Book.Classes.Historical },
        {9, Book.Classes.Historical },
        {10, Book.Classes.Historical },
        {11, Book.Classes.Historical },
        {12, Book.Classes.Historical },
        {13, Book.Classes.Historical },
        {14, Book.Classes.Historical },
        {15, Book.Classes.Historical },
        {16, Book.Classes.Historical },
        {17, Book.Classes.Historical },
        {18, Book.Classes.PoetryAndWisdom },
        {19, Book.Classes.PoetryAndWisdom },
        {20, Book.Classes.PoetryAndWisdom },
        {21, Book.Classes.PoetryAndWisdom },
        {22, Book.Classes.PoetryAndWisdom },
        {23, Book.Classes.MajorProphets },
        {24, Book.Classes.MajorProphets },
        {25, Book.Classes.MajorProphets },
        {26, Book.Classes.MajorProphets },
        {27, Book.Classes.MajorProphets },
        {28, Book.Classes.MinorProphets },
        {29, Book.Classes.MinorProphets },
        {30, Book.Classes.MinorProphets },
        {31, Book.Classes.MinorProphets },
        {32, Book.Classes.MinorProphets },
        {33, Book.Classes.MinorProphets },
        {34, Book.Classes.MinorProphets },
        {35, Book.Classes.MinorProphets },
        {36, Book.Classes.MinorProphets },
        {37, Book.Classes.MinorProphets },
        {38, Book.Classes.MinorProphets },
        {39, Book.Classes.MinorProphets },
        {40, Book.Classes.Gospels },
        {41, Book.Classes.Gospels },
        {42, Book.Classes.Gospels },
        {43, Book.Classes.Gospels },
        {44, Book.Classes.Acts },
        {45, Book.Classes.Epistles },
        {46, Book.Classes.Epistles },
        {47, Book.Classes.Epistles },
        {48, Book.Classes.Epistles },
        {49, Book.Classes.Epistles },
        {50, Book.Classes.Epistles },
        {51, Book.Classes.Epistles },
        {52, Book.Classes.Epistles },
        {53, Book.Classes.Epistles },
        {54, Book.Classes.Epistles },
        {55, Book.Classes.Epistles },
        {56, Book.Classes.Epistles },
        {57, Book.Classes.Epistles },
        {58, Book.Classes.Epistles },
        {59, Book.Classes.Epistles },
        {60, Book.Classes.Epistles },
        {61, Book.Classes.Epistles },
        {62, Book.Classes.Epistles },
        {63, Book.Classes.Epistles },
        {64, Book.Classes.Epistles },
        {65, Book.Classes.Epistles },
        {66, Book.Classes.Revelation }
    };
    internal static readonly Dictionary<string, Book.Classes> ClassById = new Dictionary<string, Book.Classes>()
    {
        {"Gen", Book.Classes.Pentateuch },
        {"Exod", Book.Classes.Pentateuch },
        {"Lev", Book.Classes.Pentateuch },
        {"Num", Book.Classes.Pentateuch },
        {"Deut", Book.Classes.Pentateuch },
        {"Josh", Book.Classes.Historical },
        {"Judg", Book.Classes.Historical },
        {"Ruth", Book.Classes.Historical },
        {"1Sam", Book.Classes.Historical },
        {"2Sam", Book.Classes.Historical },
        {"1Kgs", Book.Classes.Historical },
        {"2Kgs", Book.Classes.Historical },
        {"1Chr", Book.Classes.Historical },
        {"2Chr", Book.Classes.Historical },
        {"Ezra", Book.Classes.Historical },
        {"Neh", Book.Classes.Historical },
        {"Esth", Book.Classes.Historical },
        {"Job", Book.Classes.PoetryAndWisdom },
        {"Ps", Book.Classes.PoetryAndWisdom },
        {"Prov", Book.Classes.PoetryAndWisdom },
        {"Eccl", Book.Classes.PoetryAndWisdom },
        {"Song", Book.Classes.PoetryAndWisdom },
        {"Isa", Book.Classes.MajorProphets },
        {"Jer", Book.Classes.MajorProphets },
        {"Lam", Book.Classes.MajorProphets },
        {"Ez", Book.Classes.MajorProphets },
        {"Dan", Book.Classes.MajorProphets },
        {"Hos", Book.Classes.MinorProphets },
        {"Joel", Book.Classes.MinorProphets },
        {"Amos", Book.Classes.MinorProphets },
        {"Obad", Book.Classes.MinorProphets },
        {"Jonah", Book.Classes.MinorProphets },
        {"Mic", Book.Classes.MinorProphets },
        {"Nah", Book.Classes.MinorProphets },
        {"Hab", Book.Classes.MinorProphets },
        {"Zeph", Book.Classes.MinorProphets },
        {"Hag", Book.Classes.MinorProphets },
        {"Zech", Book.Classes.MinorProphets },
        {"Mal", Book.Classes.MinorProphets },
        {"Matt", Book.Classes.Gospels },
        {"Mark", Book.Classes.Gospels },
        {"Luke", Book.Classes.Gospels },
        {"John", Book.Classes.Gospels },
        {"Acts", Book.Classes.Acts },
        {"Rom", Book.Classes.Epistles },
        {"1Cor", Book.Classes.Epistles },
        {"2Cor", Book.Classes.Epistles },
        {"Gal", Book.Classes.Epistles },
        {"Eph", Book.Classes.Epistles },
        {"Phil", Book.Classes.Epistles },
        {"Col", Book.Classes.Epistles },
        {"1Thess", Book.Classes.Epistles },
        {"2Thess", Book.Classes.Epistles },
        {"1Tim", Book.Classes.Epistles },
        {"2Tim", Book.Classes.Epistles },
        {"Titus", Book.Classes.Epistles },
        {"Phlm", Book.Classes.Epistles },
        {"Heb", Book.Classes.Epistles },
        {"Jas", Book.Classes.Epistles },
        {"1Pet", Book.Classes.Epistles },
        {"2Pet", Book.Classes.Epistles },
        {"1John", Book.Classes.Epistles },
        {"2John", Book.Classes.Epistles },
        {"3John", Book.Classes.Epistles },
        {"Jude", Book.Classes.Epistles },
        {"Rev", Book.Classes.Revelation },
        {"Jdt", Book.Classes.Apocrypha },
        {"Wis", Book.Classes.Apocrypha },
        {"Tob", Book.Classes.Apocrypha },
        {"Sir", Book.Classes.Apocrypha },
        {"Bar", Book.Classes.Apocrypha },
        {"1Macc", Book.Classes.Apocrypha },
        {"2Macc", Book.Classes.Apocrypha },
        {"AddDan", Book.Classes.Apocrypha },
        {"AddEsth", Book.Classes.Apocrypha },
        {"PrMan", Book.Classes.Apocrypha },
        {"3Macc", Book.Classes.Apocrypha },
        {"4Macc", Book.Classes.Apocrypha },
        {"EpJer", Book.Classes.Apocrypha },
        {"1Esd", Book.Classes.Apocrypha },
        {"2Esd", Book.Classes.Apocrypha },
        {"OdesSol", Book.Classes.Apocrypha },
        {"PssSol", Book.Classes.Apocrypha },
        {"Lao", Book.Classes.Apocrypha },
        {"1En", Book.Classes.Apocrypha },
        {"Jub", Book.Classes.Apocrypha },
        {"Sus", Book.Classes.Apocrypha },
        {"Bel", Book.Classes.Apocrypha }
    };

    public class Book
    {
        public enum Classes
        {
            Prefix,
            Pentateuch,
            Historical,
            PoetryAndWisdom,
            MajorProphets,
            MinorProphets,
            Gospels,
            Acts,
            Epistles,
            Revelation,
            Apocrypha,
            Appendix
        }
        public Bible Bible { get; set; }
        public Book(Bible bible) { Bible = bible; } 
        public string Name { get; set; }
        public string Abbrevation { get; set; }
        public int Index { get; set; }
        public Classes Class { get; set; }
        public string Id { get; set; }
        public string Description { get; set; }
        public string File { get; set; }
        public string Markdown { get => field ??= IO.File.ReadAllText(File, System.Text.Encoding.UTF8); set => field = value; }
        public List<Run> Runs {
            get => field ??= Bible.ParseBook(this).ToList();
            set => field = value;
        }
    }
    public class VersePosition
    {
        public Book Book { get; set; }
        public int Chapter { get; set; }
        public int Verse { get; set; }
    }

    public class Run
    {
        public enum Classes
        {
            Text,
            Italic,
            Bold,
            SmallCaps,
            WordWithStrong,
            WordsOfJesus,
            Greek,
            Hebrew,
            Arabic,
            Link,
            Title,
            Book,
            Chapter,
            Verse,
            Footnote,
            Paragraph
        }
        public Classes Class { get; set; }
        public string Text { get; set; }
        public string Url { get; set; }
        public IEnumerable<Run> Runs { get; set; }
        public string StrongNumber { get; set; }
        public VersePosition Position { get; set; }
        public VersePosition Reference { get; set; }
        public Color MarkerColor { get; set; }
        public int Chapter { get; set; }
        public Run Parent { get; private set; }
        public bool First { get; set; }
        IEnumerable<Run> Append(IEnumerable<Run> children)
        {
            yield return this;
            foreach (var run in children)
            {
                if (run.Parent == null) run.Parent = this;
                yield return run;
            }
        }
        public IEnumerable<Run> All => Runs
            .SelectMany(run => run.Append(run.All));
    }
    
    public static IEnumerable<Run> ParseText(string text, Run.Classes textClass, VersePosition position)
    {
        foreach (Match m in Regex.Matches(text, @"\*\*(?<bold>.*?)\*\*|\*(?<italic>.*?)\*|\^\[(?<footnote>.*?)\]|" +
            @"\[(?<smallcaps>.*?)\]\{.smallcaps\}|\[(?<wordsofjesus>.*?)\]\{.wj\}|" +
            @"\[(?<link>(.*?))\]\((?<linkurl>[a-zA-Z]+://.*?)\)|" + 
            @"(?<normal>.*?(?=\*\*.*?\*\*|\*.*?\*|\^\[.*?\]|\[.*?\]\{.smallcaps\}|\[.*?\]\{.wj\}|\[.*?\]\([a-zA-Z]+://.*?\)|$))", RegexOptions.Singleline))
        {
            if (m.Groups["bold"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Bold,
                    Text = m.Groups["bold"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["bold"].Value, Run.Classes.Bold, position).ToList()
                };
            else if (m.Groups["italic"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Italic,
                    Text = m.Groups["italic"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["italic"].Value, Run.Classes.Italic, position).ToList()
                };
            else if (m.Groups["footnote"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Footnote,
                    Text = m.Groups["footnote"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["footnote"].Value, Run.Classes.Text, position).ToList()
                };
            else if (m.Groups["smallcaps"].Success)
                yield return new Run
                {
                    Class = Run.Classes.SmallCaps,
                    Text = m.Groups["smallcaps"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["smallcaps"].Value, Run.Classes.SmallCaps, position).ToList()
                };
            else if (m.Groups["link"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Link,
                    Text = m.Groups["link"].Value,
                    Url = m.Groups["linkurl"].Value,
                    Position = position,
                    Runs = Enumerable.Empty<Run>()
                };
            else if (m.Groups["wordsofjesus"].Success)
                yield return new Run
                {
                    Class = Run.Classes.WordsOfJesus,
                    Text = m.Groups["wordsofjesus"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["wordsofjesus"].Value, Run.Classes.WordsOfJesus, position).ToList()
                };
            else if (m.Groups["normal"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Text,
                    Text = m.Groups["normal"].Value,
                    Position = position,
                    Runs = Enumerable.Empty<Run>()
                };
        }
    }
    static IEnumerable<Run> ParseVerse(Match verse, VersePosition position)
    {
        if (verse.Groups["title"].Success)
        {
            var titleText = verse.Groups["title"].Value;
            yield return new Run
            {
                Class = Run.Classes.Title,
                Text = titleText,
                Position = position,
                Runs = ParseText(titleText, Run.Classes.Title, position).ToList()
            };
        }
        else
        {
            var verseText = verse.Groups["versebody"].Value;
            int verseNumber = int.Parse(verse.Groups["verseno"].Value);
            position = new VersePosition { Book = position.Book, Chapter = position.Chapter, Verse = verseNumber };
            var verseRun = new Run
            {
                Class = Run.Classes.Verse,
                Text = verseText,
                Position = position,
                Runs = ParseText(verseText, Run.Classes.Text, position).ToList()
            };
            yield return verseRun;
        }

    }
    static IEnumerable<Run> ParseChapter(Match chapter, VersePosition position)
    {
        var chapterText = chapter.Groups["chbody"].Value;
        var chapterNumber = int.Parse(chapter.Groups["chno"].Value);
        position = new VersePosition { Book = position.Book, Chapter = chapterNumber, Verse = 0 };
        yield return new Run
        {
            Class = Run.Classes.Chapter,
            Chapter = chapterNumber,
            Position = position,
            Text = chapterText,
            Runs = Regex.Matches(chapterText, @"((?<=^|\n)##\s+(?<title>.*?)\r?\n)|@(?<verseno>[0-9]+)(?=\s)(?<versebody>.*?)(?=@[0-9]+\s+|\r?\n#|$)", RegexOptions.Singleline)
                .SelectMany(verse => ParseVerse(verse, position))
                .ToList()
        };
    }

    public static IEnumerable<Run> ParseBook(Book book)
    {
        var markdown = book.Markdown;
        var first = true;
        // strip comments
        markdown = Regex.Replace(markdown, @"/\*.*?\*/", "", RegexOptions.Singleline); // comments
        markdown = Regex.Replace(markdown, @"(?<!:)//.*?\r?\n", "", RegexOptions.Singleline); // single line comments
        // Replace footnotes
        bool replaced;
        do
        {
            replaced = false;
            markdown = Regex.Replace(markdown, @"\^(?<mark>[a-zA-Z]+)\^(?!\[)(?<text>.*?)(?:\^\k<mark>(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]))[ \t]*\r?\n?", m =>
            {
                replaced = true;
                return $"{m.Groups["footnote"].Value}{m.Groups["text"].Value}";
            }, RegexOptions.Singleline);// ^^ footnotes
        } while (replaced);

        var chapters = Regex.Matches(markdown, @"(^|\n)#\s+(?<chno>[0-9]+)\s*\r?\n(?<chbody>.*?)(?=\n#\s+[0-9]+\s*\r?\n|$)", RegexOptions.Singleline)
            .OfType<Match>();
        foreach (var chapter in chapters.SelectMany(chapter => ParseChapter(chapter, new VersePosition { Book = book })))
        {
            var title = chapter.Runs.FirstOrDefault(r => r.Class == Run.Classes.Title);
            if (title != null)
            {
                title.First = first;
                first = false;
            }
            yield return chapter;
        }
    }

    public List<Book> Books { get; set; } = new List<Book>();
    public string Name { get; set; }
    public Bible Open(string path)
    {
        Name = IO.Path.GetFileName(path);
        var dir = new IO.DirectoryInfo(path);
        if (!dir.Exists) throw new IO.DirectoryNotFoundException($"Directory not found: {path}");
        var files = dir.EnumerateFiles("*.md", IO.SearchOption.TopDirectoryOnly)
            .Where(file => Regex.IsMatch(file.Name, @"^[0-9.]+-"))
            .OrderBy(file => file.Name);
        Books.AddRange(files.Select(file => new Book(this)
        {
            Name = Regex.Replace(file.Name, @"^[0-9.]+-(.*)\.md$", "$1"),
            Index = int.Parse(Regex.Match(file.Name, "^[0-9]+").Value),
            File = file.FullName,
        }));
        var booknamesFile = IO.Path.Combine(path, "booknames.xml");
        if (!IO.File.Exists(booknamesFile)) booknamesFile = IO.Path.Combine(path, "src", "booknames.xml");
        if (IO.File.Exists(booknamesFile))
        {
            var xml = System.Xml.Linq.XDocument.Load(booknamesFile);
            var enid = xml.Root.Elements("ID")
                .FirstOrDefault(e => e.Attribute("descr")?.Value.Equals("english", StringComparison.OrdinalIgnoreCase) == true)
                ?.Elements("BOOK");
            var id = xml.Root.Elements("ID")
                .Select(id => {
                    var xbooks = id.Elements("BOOK");
                    return xbooks
                        .Join(Books, book => book.Value, book => book.Name, (id, book) => new { Id = id, Book = book })
                        .Join(enid, book => book.Id.Attribute("bnumber")?.Value, id => id.Attribute("bnumber")?.Value, (b, enid) => new
                        {
                            b.Id, b.Book, EnId = enid
                        })
                        .ToList();
                })
                .MaxBy(list => list.Count);
            foreach (var book in id)
            {
                book.Book.Abbrevation = book.Id.Attribute("bshort")?.Value;
                book.Book.Description = book.Id.Attribute("desc")?.Value;
                var bid = book.Id.Attribute("id")?.Value ?? book.EnId.Attribute("bshort")?.Value;
                book.Book.Id = bid;
                if (string.IsNullOrEmpty(bid))
                {
                    if (Bible.ClassByIndex.TryGetValue(book.Book.Index, out var @class)) book.Book.Class = @class;
                    else book.Book.Class = Book.Classes.Apocrypha;
                } else
                {
                    if (Bible.ClassById.TryGetValue(bid, out var @class)) book.Book.Class = @class;
                    else book.Book.Class = Book.Classes.Apocrypha;
                }
            }
        }
        return this;
    }

}