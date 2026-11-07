using Avalonia.Controls;
using BibleReader.Library;

namespace BibleReader.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Scrolling past the end of a book continues in the next one, back past the start in the previous one.
        bibleView.Books = new Bible().Open("C:\\GitHub\\BibliaLibrePrivate\\Bibles\\English.WorldEnglishBibleUS").Books;
        _ = bibleView.ShowBookAsync(4);
    }
}
