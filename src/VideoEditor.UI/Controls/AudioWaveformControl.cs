using System.Windows;
using System.Windows.Media;

namespace VideoEditor.UI.Controls;

/// <summary>
///     Lightweight waveform renderer for audio clips on the timeline.
/// </summary>
public sealed class AudioWaveformControl : FrameworkElement
{
    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks),
        typeof(IReadOnlyList<double>),
        typeof(AudioWaveformControl),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Peaks
    {
        get => (IReadOnlyList<double>)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var peaks = Peaks;
        if (peaks.Count == 0 || ActualWidth <= 1 || ActualHeight <= 1)
            return;

        var centerY = ActualHeight / 2;
        var maxHeight = Math.Max(1, ActualHeight - 8);
        var barCount = Math.Max(1, (int)Math.Floor(ActualWidth / 3));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(190, 176, 226, 255)), 1.4);
        pen.Freeze();

        for (var i = 0; i < barCount; i++)
        {
            var peakIndex = Math.Min(peaks.Count - 1, (int)Math.Floor(i * peaks.Count / (double)barCount));
            var peak = Math.Clamp(peaks[peakIndex], 0, 1);
            var halfHeight = Math.Max(1, peak * maxHeight / 2);
            var x = i * ActualWidth / barCount + 1;
            drawingContext.DrawLine(pen, new Point(x, centerY - halfHeight), new Point(x, centerY + halfHeight));
        }
    }
}
