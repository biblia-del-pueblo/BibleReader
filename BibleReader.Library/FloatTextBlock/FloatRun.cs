using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace BibleReader.Library.UI;

/// <summary>
/// A <see cref="Run"/> that does not flow inline: <see cref="BibleTextBlock"/> lifts its text out and floats it
/// to the <see cref="Side"/>, with the remaining text wrapping around it (think drop cap or pull quote).
/// Font and foreground set on the run apply to the floated text; unset values are inherited from the FloatTextBlock.
/// </summary>
public class FloatRun : Run
{
    public static readonly StyledProperty<FloatSide> SideProperty =
        AvaloniaProperty.Register<FloatRun, FloatSide>(nameof(Side));

    /// <summary>Fixed width of the floated text; NaN lets it size to its text (up to the available width).</summary>
    public static readonly StyledProperty<double> FloatWidthProperty =
        AvaloniaProperty.Register<FloatRun, double>(nameof(FloatWidth), double.NaN);

    public FloatSide Side { get => GetValue(SideProperty); set => SetValue(SideProperty, value); }
    public double FloatWidth { get => GetValue(FloatWidthProperty); set => SetValue(FloatWidthProperty, value); }

    /// <summary>The control FloatTextBlock actually places; kept in sync with this run's properties.</summary>
    internal TextBlock Visual { get; }

    public FloatRun()
    {
        Visual = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        Sync();
    }

    public FloatRun(string text) : this() => Text = text;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Visual is not null) Sync(); // null while the base constructor is still setting defaults
    }

    private void Sync()
    {
        Visual.Text = Text;
        Visual.Width = FloatWidth;
        BibleTextBlock.SetSide(Visual, Side);

        // Copy only what this run sets locally, so everything else is inherited from the FloatTextBlock.
        if (IsSet(TextElement.FontSizeProperty)) Visual.FontSize = FontSize; else Visual.ClearValue(TextBlock.FontSizeProperty);
        if (IsSet(TextElement.FontFamilyProperty)) Visual.FontFamily = FontFamily; else Visual.ClearValue(TextBlock.FontFamilyProperty);
        if (IsSet(TextElement.FontStyleProperty)) Visual.FontStyle = FontStyle; else Visual.ClearValue(TextBlock.FontStyleProperty);
        if (IsSet(TextElement.FontWeightProperty)) Visual.FontWeight = FontWeight; else Visual.ClearValue(TextBlock.FontWeightProperty);
        if (IsSet(TextElement.ForegroundProperty)) Visual.Foreground = Foreground; else Visual.ClearValue(TextBlock.ForegroundProperty);
    }
}
