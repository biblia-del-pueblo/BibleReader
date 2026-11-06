using System.IO;
using System.Linq;
using Avalonia.Controls;
using BibleReader.Library;

namespace BibleReader.Test;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    
        var file = File.ReadAllText("01-Genesis.md");
        text.Parse(new BibleText().Open("C:\\GitHub\\BibliaLibrePrivate\\Bibles\\English.WorldEnglishBibleUS").Books.Skip(4).FirstOrDefault().Runs);
    }
}
