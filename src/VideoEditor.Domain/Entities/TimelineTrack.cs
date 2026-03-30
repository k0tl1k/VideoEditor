namespace VideoEditor.Domain.Entities;

public sealed class TimelineTrack
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Video Track 1";
    public IList<TimelineClip> Clips { get; } = new List<TimelineClip>();
}
