namespace VideoEditor.Domain.Entities;

/// <summary>
/// 	Экземпляр клипа, размещенный на дорожке таймлайна.
/// </summary>
public sealed class TimelineClip : EntityBase
{
    /// <summary>
    /// 	Ссылка на исходный медиафайл.
    /// </summary>
    public Guid MediaAssetId { get; init; }

    /// <summary>
    /// 	Точка начала в исходном файле.
    /// </summary>
    public TimeSpan SourceStart { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// 	Длительность выбранного фрагмента исходника.
    /// </summary>
    public TimeSpan SourceDuration { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// 	Позиция начала клипа на таймлайне.
    /// </summary>
    public TimeSpan TimelineStart { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// 	Вычисляемая позиция конца клипа на таймлайне.
    /// </summary>
    public TimeSpan TimelineEnd => TimelineStart + SourceDuration;
}
