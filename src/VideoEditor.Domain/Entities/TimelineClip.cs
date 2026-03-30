namespace VideoEditor.Domain.Entities;

public sealed class TimelineClip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MediaAssetId { get; init; }

    // Segment inside the source file.
    public TimeSpan SourceStart { get; init; } = TimeSpan.Zero;
    public TimeSpan SourceDuration { get; init; } = TimeSpan.Zero;

    // Position of this clip on the timeline.
    public TimeSpan TimelineStart { get; init; } = TimeSpan.Zero;

    public TimeSpan TimelineEnd => TimelineStart + SourceDuration;
}
