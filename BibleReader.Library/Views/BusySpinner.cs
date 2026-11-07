using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace BibleReader.Library.UI;

/// <summary>
/// A turning arc that shows something is being prepared. It draws itself and moves on with every animation frame
/// while it is visible, and does nothing (no frames are requested) while it is hidden or detached.
/// </summary>
public sealed class BusySpinner : Control
{
    /// <summary>The colour of the arc.</summary>
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<BusySpinner, IBrush?>(nameof(Brush), Brushes.Gray);

    /// <summary>The width of the arc's line.</summary>
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<BusySpinner, double>(nameof(Thickness), 4);

    /// <summary>Seconds for one full turn.</summary>
    public static readonly StyledProperty<double> PeriodProperty =
        AvaloniaProperty.Register<BusySpinner, double>(nameof(Period), 0.9);

    public IBrush? Brush { get => GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public double Period { get => GetValue(PeriodProperty); set => SetValue(PeriodProperty, value); }

    private double _angle;        // degrees
    private bool _running;        // a frame is requested
    private TimeSpan? _startedAt; // the time of the first frame, so the turn starts at 0

    static BusySpinner()
    {
        AffectsRender<BusySpinner>(BrushProperty, ThicknessProperty);
        AffectsMeasure<BusySpinner>(WidthProperty, HeightProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(32, 32); // when no Width/Height is set

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _startedAt = null; // frames stop by themselves: see OnFrame
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Own visibility only: set IsVisible on the spinner itself (a hidden parent stops the frames, but cannot restart them).
        if (change.Property == IsVisibleProperty) Update();
    }

    /// <summary>Starts asking for frames when the spinner is shown; they stop on their own when it is not.</summary>
    private void Update()
    {
        // Own flag only: a parent bound to the same value may not have become visible yet. The frame callback runs
        // later, once everything has settled, and checks the effective visibility.
        if (_running || !IsVisible || TopLevel.GetTopLevel(this) is not { } top) return;
        _running = true;
        _startedAt = null;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
        {
            _running = false; // hidden or gone: no more frames until it is shown again
            return;
        }
        _startedAt ??= time;
        double period = Math.Max(0.1, Period);
        _angle = (time - _startedAt.Value).TotalSeconds / period % 1 * 360;
        InvalidateVisual();
        top.RequestAnimationFrame(OnFrame);
    }

    public override void Render(DrawingContext context)
    {
        double thickness = Math.Max(1, Thickness);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        double radius = size / 2 - thickness / 2;
        if (radius <= 0 || Brush is not { } brush) return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        // Three quarters of a circle, starting at the right (3 o'clock) and going clockwise.
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(center.X + radius, center.Y), false);
            g.ArcTo(new Point(center.X, center.Y - radius), new Size(radius, radius), 0, true, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        var turn = Matrix.CreateTranslation(-center.X, -center.Y)
                   * Matrix.CreateRotation(_angle * Math.PI / 180)
                   * Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(turn))
            context.DrawGeometry(null, new Pen(brush, thickness, lineCap: PenLineCap.Round), geometry);
    }
}
