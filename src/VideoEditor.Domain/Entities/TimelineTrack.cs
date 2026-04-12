namespace VideoEditor.Domain.Entities;

/// <summary>
/// 	Отдельная дорожка таймлайна.
/// </summary>
public sealed class TimelineTrack : EntityBase
{
    /// <summary>
    /// 	Название дорожки, отображаемое в интерфейсе.
    /// </summary>
    public string Name { get; init; } = "Video Track 1";

    /// <summary>
    /// 	Включена ли дорожка для предпросмотра и будущего рендера.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 	Кадры/клипы, размещенные на этой дорожке.
    /// </summary>
    public IList<TimelineClip> Clips { get; } = new List<TimelineClip>();
}
