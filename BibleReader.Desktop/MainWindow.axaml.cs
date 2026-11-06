using Avalonia.Controls;
using BibleReader.Library;

namespace BibleReader.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Scrolling past the end of a book continues in the next one, back past the start in the previous one.
        text.Books = new Bible().Open("C:\\GitHub\\BibliaLibrePrivate\\Bibles\\English.WorldEnglishBibleUS").Books;
        _ = text.ShowBookAsync(4);
    }
}
