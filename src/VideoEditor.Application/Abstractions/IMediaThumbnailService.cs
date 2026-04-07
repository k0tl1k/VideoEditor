using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	Возвращает путь к миниатюре медиафайла.
/// </summary>
public interface IMediaThumbnailService
{
    /// <summary>
    /// 	Строит миниатюру и возвращает путь к файлу изображения.
    /// </summary>
    /// <param name="asset"> Медиа-ассет для генерации миниатюры. </param>
    /// <returns> Путь к миниатюре или <see langword="null" />. </returns>
    string? GetThumbnailPath(MediaAsset asset);
}
