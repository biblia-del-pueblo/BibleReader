using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BibleReader.Library.Downloads
{
    public class StrongsDictionaries
    {
        public static string? Lookup(string key) => string.Join(Environment.NewLine+Environment.NewLine, 
            (Settings.Current.Bibles ?? new List<BookText>())
                .OfType<IStrongDictionary>()
                .SelectMany(d => key.Split(' ')
                    .Select(k => d.IsEnabled ? d.LookupStrong(NormalizeKey(k)) : null))
                .Where(markdown => markdown != null));

        /// <summary>
        /// The key of the dictionaries has no leading zeros ("H430"), but some modules write the numbers with four digits
        /// ("H0430", as the Reina Valera 1909 with Strong's numbers of CrossWire does).
        /// </summary>
        static string NormalizeKey(string key) => System.Text.RegularExpressions.Regex.Replace(key.Trim(), "^([GgHh])0+(?=[0-9])", m => m.Groups[1].Value.ToUpperInvariant());

        static bool? any;

        /// <summary>
        /// Whether a Strong's dictionary is switched on. Without one the words with Strong's numbers are not marked and
        /// cannot be clicked. The value is kept; <see cref="Refresh"/> checks it again and raises
        /// <see cref="AnyChanged"/> when it changed.
        /// </summary>
        public static bool Any => any ??= Compute();

        /// <summary>Raised when <see cref="Any"/> changed (possibly on another thread than the UI).</summary>
        public static event EventHandler? AnyChanged;

        static bool Compute() => (Settings.Current.Bibles ?? new List<BookText>())
            .OfType<IStrongDictionary>()
            .Any(d => d.IsEnabled);

        /// <summary>
        /// To be called when a dictionary was switched on or off, installed or deleted (a dictionary does it when its
        /// <see cref="IStrongDictionary.IsEnabled"/> changes).
        /// </summary>
        public static void Refresh()
        {
            var now = Compute();
            if (any == now) return;
            any = now;
            AnyChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
