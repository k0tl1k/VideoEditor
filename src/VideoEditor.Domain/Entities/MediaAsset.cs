using VideoEditor.Domain.Enums;

namespace VideoEditor.Domain.Entities;

/// <summary>
/// 	Импортированный исходный файл.
/// </summary>
public sealed class MediaAsset : EntityBase
{
    /// <summary>
    /// 	Абсолютный или относительный путь к исходному файлу на диске.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// 	Отображаемое имя файла в интерфейсе.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 	Тип медиа (видео/аудио/изображение).
    /// </summary>
    public MediaType Type { get; init; } = MediaType.Video;

    /// <summary>
    /// 	Полная длительность исходного файла.
    /// </summary>
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
}
