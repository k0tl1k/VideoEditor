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
    /// 	Идентификатор группы связанных клипов (например, видео и аудио одного файла).
    /// </summary>
    public Guid? LinkedGroupId { get; set; }

    /// <summary>
    /// 	Точка начала в исходном файле.
    /// </summary>
    public TimeSpan SourceStart { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 	Длительность выбранного фрагмента исходника.
    /// </summary>
    public TimeSpan SourceDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 	Базовая точка начала фрагмента, к которой возвращается trim reset.
    /// </summary>
    public TimeSpan TrimBaselineSourceStart { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 	Базовая длительность фрагмента, к которой возвращается trim reset.
    /// </summary>
    public TimeSpan TrimBaselineSourceDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 	Позиция начала клипа на таймлайне.
    /// </summary>
    public TimeSpan TimelineStart { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 	Позиция клипа по X внутри кадра 1920x1080.
    /// </summary>
    public double FrameX { get; set; }

    /// <summary>
    /// 	Позиция клипа по Y внутри кадра 1920x1080.
    /// </summary>
    public double FrameY { get; set; }

    /// <summary>
    /// 	Масштаб клипа внутри кадра.
    /// </summary>
    public double FrameScale { get; set; } = 1.0;

    /// <summary>
    /// 	Громкость аудиоклипа: 1.0 = 100%.
    /// </summary>
    public double AudioVolume { get; set; } = 1.0;

    /// <summary>
    /// 	Признак, что встроенный звук видеоклипа должен быть заглушен.
    /// </summary>
    public bool MuteEmbeddedAudio { get; set; }

    /// <summary>
    /// 	Вычисляемая позиция конца клипа на таймлайне.
    /// </summary>
    public TimeSpan TimelineEnd => TimelineStart + SourceDuration;

    /// <summary>
    /// 	Сохраняет текущее trim-состояние как базовое.
    /// </summary>
    public void CaptureTrimBaseline()
    {
        TrimBaselineSourceStart = SourceStart;
        TrimBaselineSourceDuration = SourceDuration;
    }
}
