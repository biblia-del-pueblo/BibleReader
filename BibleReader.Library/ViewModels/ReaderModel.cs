using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Fonts;
using Avalonia.Layout;
using Avalonia.Media;

namespace BibleReader.Library.ViewModels
{
    public class ReaderModel: INotifyPropertyChanged
    {
        public Typeface Font { get; set; }
        public string Markdown { get; set; }
        public int Page { get; set; }
        public int PagesCount { get; set; }
        public double Position { get; set; }
        public double Length { get; }
        public int Columns { get; set; }
        public int ColumnSpacing { get; set; }
        public bool IsSingleColumn => Columns == 1;
        public Size Size { get; set; }

        public IEnumerable<Inline> Inlines(int column)
        {

        }

        public double GetPostition(int chapter, int verse)
        {

        }
        public int GetPage(int chapter, int verse) { ]}

        public ReaderModel() { }


    }
}
