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
    public class Book
    {
        public Bible Bible { get; set; }
        public Book(Bible bible) { Bible = bible; } 
        public string Name { get; set; }
        public string Abbrevation { get; set; }
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
            File = file.FullName,
        }));
        var booknamesFile = IO.Path.Combine(path, "booknames.xml");
        if (!IO.File.Exists(booknamesFile)) booknamesFile = IO.Path.Combine(path, "src", "booknames.xml");
        if (IO.File.Exists(booknamesFile))
        {
            var xml = System.Xml.Linq.XDocument.Load(booknamesFile);
            var id = xml.Root.Elements("ID")
                .Select(id => {
                    var xbooks = id.Elements("BOOK");
                    return xbooks
                        .Join(Books, book => book.Value, book => book.Name, (id, book) => new { Id = id, Book = book })
                        .ToList();
                })
                .MaxBy(list => list.Count);
            foreach (var book in id)
            {
                book.Book.Abbrevation = book.Id.Attribute("bshort")?.Value;
                book.Book.Description = book.Id.Attribute("desc")?.Value;
            }
        }
        return this;
    }

}