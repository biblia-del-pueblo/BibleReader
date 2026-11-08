using Avalonia.Controls.Shapes;
using BibleMarkdown;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using IO = System.IO;

namespace BibleReader.Library.Downloads
{
    public class BibleArchive: OnlineBible
    {
        public string File { get; set; }
        public override string Origin => "Local File";

        public override async Task Installl()
        {
            if (string.IsNullOrEmpty(File)) throw new InvalidOperationException("File is not set.");

            var path = ConfigPath;
            var src = IO.Path.Combine(path, "src");
            Directory.CreateDirectory(src);

            var ext = IO.Path.GetExtension(File).ToLower();
            if (ext == ".zip" || ext == ".7z") Zip.UnzipFile(File, src);
            else IO.File.Copy(File, IO.Path.Combine(src, IO.Path.GetFileName(File)));
            
            try {
                await Task.Run(() => {
                    BookList.Books.Load(path);
                    Program.ImportFromBibleEdit(src);
                    Program.ImportFromUSFM(path, src);
                    Program.ImportFromTXT(path, src);
                    Program.ImportFromZefania(path, src);
                    Program.ImportFromXmlOther(path, src);
                    Program.ImportFromSWORD(path, src);
                    Program.ImportJavascripture(path, src);
                    Program.ImportFromBibleMarkdown(path, src);
                    Program.ImportParallelVerses(path);
                    Program.ImportOutline(path);
                });
            }
            finally { }

        }
    }
}
