using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BibleReader.Library;

public class Zip
{
	public static CancellationToken Cancel => Download.Cancel.Token;

	/// <summary>
	/// .NET Framework does not probe runtimes/{rid}/native, so load archive.dll by full path;
	/// a module loaded this way satisfies the later DllImport("archive") of LibArchive.Net.
	/// </summary>
	public static void UnzipZipFile(string zipFile, string destFolder, Func<string, bool> filter = null, Stream stream = null,
		Action<long, long> progress = null)
	{
        var cancel = Cancel;
        
		try
        {
			if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

			if (filter == null) filter = name => true;

			//Log.WriteStart("Unzipping file");
			//Log.WriteInfo(string.Format("Unzipping file \"{0}\" to the folder \"{1}\"", zipFile, destFolder));

			using (var file = stream ?? new FileStream(zipFile, System.IO.FileMode.Open, FileAccess.Read))
			using (var zip = new ZipArchive(file))
			{
				long zipSize = file.Length;
				long unzipped = 0;

				int files = 0;

                foreach (var entry in zip.Entries)
                {
                    cancel.ThrowIfCancellationRequested();

                    if (filter(entry.FullName))
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(Path.Combine(destFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        }
                        else
                        {
                            var filename = Path.Combine(destFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            var dir = Path.GetDirectoryName(filename);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            entry.ExtractToFile(filename, true);
                            files++;
                        }
                    }
                    else if (!string.IsNullOrEmpty(entry.Name)) files++;

                    unzipped += entry.CompressedLength;

                    if (zipSize != 0) progress?.Invoke(unzipped, zipSize);
                }

				progress?.Invoke(zipSize, zipSize);
			}
		}
		catch (Exception ex)
		{
			if (ex is ThreadAbortException)
				return;
            
			if (cancel.IsCancellationRequested) return;
            
			throw;
		}
	}

}
