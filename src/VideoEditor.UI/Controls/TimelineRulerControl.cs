using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VideoEditor.UI.Controls;

public sealed class TimelineRulerControl : FrameworkElement
{
    public static readonly DependencyProperty PixelsPerSecondProperty = DependencyProperty.Register(
        nameof(PixelsPerSecond),
        typeof(double),
        typeof(TimelineRulerControl),
        new FrameworkPropertyMetadata(32d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(
        nameof(DurationSeconds),
        typeof(double),
        typeof(TimelineRulerControl),
        new FrameworkPropertyMetadata(180d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.Register(
        nameof(HorizontalOffset),
        typeof(double),
        typeof(TimelineRulerControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayheadCanvasLeftProperty = DependencyProperty.Register(
        nameof(PlayheadCanvasLeft),
        typeof(double),
        typeof(TimelineRulerControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Typeface _labelTypeface = new("Segoe UI");

    public double PixelsPerSecond
    {
        get => (double)GetValue(PixelsPerSecondProperty);
        set => SetValue(PixelsPerSecondProperty, value);
    }

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    public double PlayheadCanvasLeft
    {
        get => (double)GetValue(PlayheadCanvasLeftProperty);
        set => SetValue(PlayheadCanvasLeftProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var pixelsPerSecond = Math.Max(0.001, PixelsPerSecond);
        var durationSeconds = Math.Max(1, DurationSeconds);
        var visibleStart = Math.Max(0, HorizontalOffset / pixelsPerSecond);
        var visibleEnd = Math.Min(durationSeconds, (HorizontalOffset + ActualWidth) / pixelsPerSecond);
        var tickStep = ResolveTickStep(pixelsPerSecond);
        var labelStep = ResolveLabelStep(pixelsPerSecond, tickStep);
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(42, 58, 85)), 1);
        var playheadPen = new Pen(new SolidColorBrush(Color.FromRgb(53, 211, 255)), 2);

        var firstTick = Math.Floor(visibleStart / tickStep) * tickStep;
        for (var second = firstTick; second <= visibleEnd + tickStep; second += tickStep)
        {
            if (second < 0)
                continue;

            var x = second * pixelsPerSecond - HorizontalOffset;
            var isLabel = IsStepAligned(second, labelStep);
            var isMajor = IsStepAligned(second, Math.Max(labelStep, tickStep));
            var height = isMajor ? 18 : 10;
            var opacity = isMajor ? 1.0 : 0.55;
            tickPen.Brush.Opacity = opacity;
            drawingContext.DrawLine(tickPen, new Point(x, 0), new Point(x, height));

            if (!isLabel)
                continue;

            var label = FormatTimecodeLabel((int)Math.Round(second));
            var formattedText = new FormattedText(
                label,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                _labelTypeface,
                10,
                new SolidColorBrush(Color.FromRgb(139, 153, 176)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(formattedText, new Point(x + 4, 20));
        }

        var playheadX = PlayheadCanvasLeft - HorizontalOffset;
        if (playheadX >= 0 && playheadX <= ActualWidth)
            drawingContext.DrawLine(playheadPen, new Point(playheadX, 0), new Point(playheadX, ActualHeight));
    }

    private static bool IsStepAligned(double value, int step)
    {
        if (step <= 0)
            return false;

        return Math.Abs(value % step) < 0.001 || Math.Abs(value % step - step) < 0.001;
    }

    private static int ResolveTickStep(double pixelsPerSecond)
    {
        var step = Math.Max(1, (int)Math.Ceiling(12 / pixelsPerSecond));
        return NormalizeStep(step);
    }

    private static int ResolveLabelStep(double pixelsPerSecond, int tickStep)
    {
        var step = Math.Max(tickStep, (int)Math.Ceiling(86 / pixelsPerSecond));
        return NormalizeStep(step);
    }

    private static int NormalizeStep(int step)
    {
        if (step <= 1)
            return 1;

        var normalizedSteps = new[]
        {
            2, 5, 10, 15, 30,
            60, 120, 300, 600, 900,
            1800, 3600, 7200
        };

        return normalizedSteps.FirstOrDefault(x => x >= step) is var normalized && normalized > 0
            ? normalized
            : step;
    }

    private static string FormatTimecodeLabel(int totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours:00}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }
}
