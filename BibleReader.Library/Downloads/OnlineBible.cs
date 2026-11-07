using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BibleReader.Library;

public class OnlineBible: Bible
{
    public string CountryCode { get; set; }
    public string CountryName { get; set; }
    protected virtual string DetailsUrl { get; set; }
    protected virtual string CrossWireModuleUrl { get; set; }
    public string Language { get; set; }
    public string LanguageEnglishName { get; set; }
    public virtual async Task DownloadBible() { }

}
