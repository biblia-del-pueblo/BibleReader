using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Platform;
using BibleReader.Library.Downloads;
using NHyphenator;
using NHyphenator.Loaders;

namespace BibleReader.Library;

/// <summary>
/// Hyphenation of the text: finds where the words may be split at the end of a line (Liang's algorithm, by NHyphenator)
/// and marks these places with a soft hyphen (U+00AD). The text block breaks lines there and draws the hyphen at the end
/// of a line that was broken at one (see BibleTextBlock). The patterns are the ones of TeX (hyph-utf8) in
/// Assets/Hyphenation; there is one set per language, and a language without one is not hyphenated.
/// </summary>
public static class Hyphenation
{
    /// <summary>The soft hyphen: invisible unless a line is broken at it.</summary>
    public const char SoftHyphen = '­';

    /// <summary>The languages that have patterns (ISO 639-1 codes).</summary>
    public static readonly string[] Languages = { "de", "en", "es", "fr", "pt", "ru", "tr", "el", "it", "nl", "pl", "da", "sv", "nb", "fi",
        "hi", "bn", "ta", "te", "mr", "gu", "kn", "ml", "pa", "or", "th", "hy", "id", "mn", "ne" };

    /// <summary>
    /// The Indian languages, and Thai. Their words have combining vowel signs, which are not letters for NHyphenator, so they are
    /// hyphenated by <see cref="LiangPatterns"/>. They are not written with a hyphen at the end of a line: the line is
    /// broken at a zero width space (<see cref="BreakOpportunity"/>) and nothing is drawn.
    /// </summary>
    public static readonly HashSet<string> IndicLanguages = new() { "hi", "bn", "ta", "te", "mr", "gu", "kn", "ml", "pa", "or", "th", "ne" };

    /// <summary>An invisible place where a line may be broken, without a hyphen (used for the Indian languages).</summary>
    public const char BreakOpportunity = '\u200B';

    /// <summary>Whether the character only marks a place where a word may be split (it is not part of the text that is read or copied).</summary>
    public static bool IsMark(char c) => c is SoftHyphen or BreakOpportunity;

    // Languages whose patterns must be sorted before use (see NHyphenator).
    private static readonly HashSet<string> SortedLanguages = new() { "de", "pt" };

    private static readonly ConcurrentDictionary<string, Hyphenator?> Hyphenators = new();
    private static readonly ConcurrentDictionary<(string Language, string Word), string> Words = new();

    private static Hyphenator? HyphenatorOf(string language) => Hyphenators.GetOrAdd(language, code =>
    {
        if (!Languages.Contains(code)) return null;
        try { return new Hyphenator(new AssetPatternsLoader(code), SoftHyphen.ToString(), minWordLength: 6, minLetterCount: 3, hyphenateLastWord: true, sortPatterns: SortedLanguages.Contains(code)); }
        catch (Exception ex) when (ex is IOException or ArgumentException or KeyNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"No hyphenation patterns for {code}: {ex.Message}");
            return null;
        }
    });

    private static readonly ConcurrentDictionary<string, LiangPatterns?> IndicPatterns = new();

    private static LiangPatterns? IndicPatternsOf(string language) => IndicPatterns.GetOrAdd(language, code =>
    {
        try { return LiangPatterns.Parse(AssetPatternsLoader.Read(AssetPatternsLoader.UriOf(code, "pat")) ?? "", BreakOpportunity); }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"No hyphenation patterns for {code}: {ex.Message}");
            return null;
        }
    });

    /// <summary>Letters, and for the Indian languages also the vowel signs and joiners that belong to them.</summary>
    private static bool IsWordChar(char c, bool indic) =>
        char.IsLetter(c) || (indic && (c is '\u200C' or '\u200D'
            || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark));

    /// <summary>
    /// The text with a mark at every place where a word may be split: a soft hyphen (<see cref="SoftHyphen"/>), or for
    /// the Indian languages a <see cref="BreakOpportunity"/>. Short words stay as they are. The same word is only worked out once.
    /// </summary>
    public static string Apply(string text, string language)
    {
        if (text.Length == 0) return text;
        bool indic = IndicLanguages.Contains(language);
        Func<string, string>? hyphenate = null;
        if (indic) { if (IndicPatternsOf(language) is { } patterns) hyphenate = patterns.Hyphenate; }
        else if (HyphenatorOf(language) is { } hyphenator) hyphenate = hyphenator.HyphenateText;
        if (hyphenate is null) return text;

        StringBuilder? result = null;
        int wordStart = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool inWord = i < text.Length && IsWordChar(text[i], indic);
            if (inWord) { if (wordStart < 0) wordStart = i; continue; }
            if (wordStart >= 0)
            {
                string word = text[wordStart..i];
                string hyphenated = word.Length < 6 ? word : Words.GetOrAdd((language, word), key => hyphenate(key.Word));
                if (hyphenated != word)
                {
                    result ??= new StringBuilder(text.Length + 16).Append(text, 0, wordStart);
                    result.Append(hyphenated);
                }
                else result?.Append(word);
                wordStart = -1;
            }
            if (i < text.Length) result?.Append(text[i]);
        }
        return result?.ToString() ?? text;
    }

    /// <summary>The language code (<c>"de"</c>) of the installed Bible of this name, or null if it is not known or has no patterns.</summary>
    public static string? LanguageOf(Bible? bible)
    {
        if (bible is null) return null;
        var installed = (Settings.Current.Bibles ?? new List<Bible>()).OfType<InstallableBook>()
            .FirstOrDefault(b => b.EnglishName == bible.EnglishName || b.EnglishName == bible.Name);
        if (installed is null) return null;
        return CodeOf(installed.Language) ?? CodeOf(installed.LanguageEnglishName);
    }

    // The two-letter code of "de", "pt-BR", "German" or "Deutsch"; null when it has no patterns.
    private static string? CodeOf(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        language = language.Trim();
        string? code;
        try { code = CultureInfo.GetCultureInfo(language).TwoLetterISOLanguageName; }
        catch (CultureNotFoundException)
        {
            code = NameToCode.Value.TryGetValue(language, out var found) ? found : null;
        }
        if (code is "no" or "nn") code = "nb"; // Norwegian, both written forms, share the patterns of Bokmål
        return code is not null && Languages.Contains(code) ? code : null;
    }

    private static readonly Lazy<Dictionary<string, string>> NameToCode = new(() =>
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.TwoLetterISOLanguageName.Length != 2) continue;
            names.TryAdd(culture.EnglishName, culture.TwoLetterISOLanguageName);
            names.TryAdd(culture.NativeName, culture.TwoLetterISOLanguageName);
        }
        return names;
    });

    /// <summary>Reads the patterns (and the exceptions, if there are) of a language from the assets of this assembly.</summary>
    private sealed class AssetPatternsLoader(string code) : IHyphenatePatternsLoader
    {
        // Nepali is written in Devanagari like Hindi and uses its patterns.
        internal static Uri UriOf(string code, string kind) => new($"avares://BibleReader.Library/Assets/Hyphenation/hyph-{(code == "ne" ? "hi" : code)}.{kind}.txt");

        internal static string? Read(Uri uri)
        {
            if (!AssetLoader.Exists(uri)) return null;
            using var reader = new StreamReader(AssetLoader.Open(uri), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        public string LoadPatterns() => Read(UriOf(code, "pat")) ?? "";
        public string? LoadExceptions() => Read(UriOf(code, "hyp"));
    }

    /// <summary>
    /// Liang's algorithm for patterns of TeX (<c>1क</c>, <c>ा1</c>, <c>2्2</c>: a digit between letters is the priority of
    /// a break there; the highest one wins, odd means a break). NHyphenator cannot be used for the Indian languages,
    /// because it ends a word at every vowel sign.
    /// </summary>
    public sealed class LiangPatterns
    {
        private readonly Dictionary<string, byte[]> patterns = new();
        private int longest;
        private char mark;

        public static LiangPatterns Parse(string text, char mark)
        {
            var result = new LiangPatterns { mark = mark };
            foreach (var line in text.Split(new[] { '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var letters = new StringBuilder();
                var values = new List<byte>();
                byte pending = 0;
                foreach (char c in line)
                {
                    if (c is >= '0' and <= '9') { pending = (byte)(c - '0'); continue; }
                    letters.Append(c);
                    values.Add(pending);
                    pending = 0;
                }
                values.Add(pending);
                if (letters.Length == 0) continue;
                result.patterns[letters.ToString()] = values.ToArray();
                result.longest = Math.Max(result.longest, letters.Length);
            }
            if (result.patterns.Count == 0) throw new ArgumentException("Patterns must contain at least one pattern");
            return result;
        }

        /// <summary>The word with the mark at every place where it may be split, at least two characters from both ends.</summary>
        public string Hyphenate(string word)
        {
            const int minimum = 2;
            if (word.Length < 2 * minimum) return word;
            string dotted = "." + word.ToLowerInvariant() + ".";
            var levels = new byte[dotted.Length + 1];
            for (int start = 0; start < dotted.Length; start++)
                for (int length = 1; length <= longest && start + length <= dotted.Length; length++)
                    if (patterns.TryGetValue(dotted.Substring(start, length), out var values))
                        for (int j = 0; j < values.Length; j++)
                            if (values[j] > levels[start + j]) levels[start + j] = values[j];

            var result = new StringBuilder(word.Length + 4);
            for (int k = 0; k < word.Length; k++)
            {
                // the level before the character word[k] is levels[k + 1] (the dotted word starts with a dot)
                if (k >= minimum && word.Length - k >= minimum && levels[k + 1] % 2 == 1) result.Append(mark);
                result.Append(word[k]);
            }
            return result.ToString();
        }
    }
}
