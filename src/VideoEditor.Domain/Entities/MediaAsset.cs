using VideoEditor.Domain.Enums;

namespace VideoEditor.Domain.Entities;

public sealed class MediaAsset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FilePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public MediaType Type { get; init; } = MediaType.Video;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
}
