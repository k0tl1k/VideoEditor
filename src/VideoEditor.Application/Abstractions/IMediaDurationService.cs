using VideoEditor.Domain.Enums;

namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	Определяет длительность медиафайла.
/// </summary>
public interface IMediaDurationService
{
    /// <summary>
    /// 	Возвращает длительность файла или ноль, если определить её не удалось.
    /// </summary>
    /// <param name="filePath"> Путь к медиафайлу. </param>
    /// <param name="mediaType"> Тип медиафайла. </param>
    /// <returns> Длительность файла. </returns>
    TimeSpan GetDuration(string filePath, MediaType mediaType);
}
