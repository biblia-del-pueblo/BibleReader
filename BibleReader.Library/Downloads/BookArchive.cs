using Avalonia.Controls.Shapes;
using BibleMarkdown;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using IO = System.IO;

namespace BibleReader.Library.Downloads
{
    public class BookArchive: InstallableBook
    {
        public string File { get; set; }
        public override string Origin => "Local File";

        public static string PostprocessMarkdownSmallCaps(string text)
        {
            return Regex.Replace(text, @"(?<!(^|\n)#[^\n]*?)[A-Z][A-Z]+", m =>
            {

                if (Regex.IsMatch(m.Value, "^(?:[IVXCD]+|ISBN)$", RegexOptions.Singleline))
                {
                    // is roman number
                    return m.Value;
                }

                var str = new StringBuilder("[");
                str.Append(m.Value[0]);
                for (int i = 1; i < m.Value.Length; i++) str.Append(Char.ToLower(m.Value[i]));
                str.Append("]{.smallcaps}");
                return str.ToString();
            }, RegexOptions.Singleline);
        }

        public override async Task Installl()
        {
            if (string.IsNullOrEmpty(File)) throw new InvalidOperationException("File is not set.");

            var path = ConfigPath;
            var src = IO.Path.Combine(path, "src");
            Directory.CreateDirectory(src);

            var ext = IO.Path.GetExtension(File).ToLower();
            if (ext == ".zip" || ext == ".7z")
            {
                Directory.Delete(src);
                Zip.UnzipFile(File, src);
            }
            else IO.File.Copy(File, IO.Path.Combine(src, IO.Path.GetFileName(File)), true);
            
            try {
                await Task.Run(() => {
                    var importer = new Importer()
                    {
                        Postprocess = PostprocessMarkdownSmallCaps
                    };
                    importer.All(path);
                });
            }
            finally { }
        }
    }
}
