namespace VideoEditor.Domain.Entities;

/// <summary>
/// 	Корневой объект сессии видеомонтажа.
/// </summary>
public sealed class VideoProject : EntityBase
{
    /// <summary>
    /// 	Название проекта, отображаемое пользователю.
    /// </summary>
    public string Name { get; set; } = "New Project";

    /// <summary>
    /// 	Время создания проекта в формате UTC.
    /// </summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 	Все импортированные медиафайлы проекта.
    /// </summary>
    public IList<MediaAsset> MediaAssets { get; } = new List<MediaAsset>();

    /// <summary>
    /// 	Дорожки таймлайна, формирующие финальную композицию.
    /// </summary>
    public IList<TimelineTrack> Tracks { get; } = new List<TimelineTrack>();
}
