using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	Импортирует медиафайлы в доменную модель.
/// </summary>
public interface IMediaImportService
{
    /// <summary>
    /// 	Преобразует пути файлов в коллекцию медиа-ассетов.
    /// </summary>
    /// <param name="filePaths"> Пути к импортируемым файлам. </param>
    /// <returns> Коллекция импортированных ассетов. </returns>
    IReadOnlyList<MediaAsset> Import(IEnumerable<string> filePaths);
}
