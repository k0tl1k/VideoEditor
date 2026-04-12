namespace VideoEditor.Application.Abstractions;

public sealed class TimelineExportProgress
{
    public double Percent { get; init; }

    public TimeSpan RenderedDuration { get; init; }
}
