using System;
using System.Collections.Generic;
using System.Text;

namespace BibleReader.Library
{
    public class CrossWireBook: InstallableBook
    {
        const string Url = "https://crosswire.org/sword/modules";

        public enum Classes
        {
            All,
            Bible,
            Comments,
            Dictionaries,
            Book,
            Map,
            AddOn
        }

        public Classes Class { get; set; }
        public override string Origin => "CrossWire";

    }
}
