namespace VideoEditor.Domain.Entities;

public sealed class VideoProject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "New Project";
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public IList<MediaAsset> MediaAssets { get; } = new List<MediaAsset>();
    public IList<TimelineTrack> Tracks { get; } = new List<TimelineTrack>();
}
