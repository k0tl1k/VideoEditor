using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;
namespace VideoEditor.Application.Services;

/// <summary>
/// 	Определяет тип медиа и создаёт доменные ассеты при импорте.
/// </summary>
public sealed class MediaImportService : IMediaImportService
{
    private readonly IMediaDurationService _mediaDurationService;

    /// <summary>
    /// 	Инициализирует сервис импорта медиа.
    /// </summary>
    /// <param name="mediaDurationService"> Сервис чтения длительности медиафайлов. </param>
    public MediaImportService(IMediaDurationService mediaDurationService)
    {
        _mediaDurationService = mediaDurationService;
    }

    /// <inheritdoc />
    public IReadOnlyList<MediaAsset> Import(IEnumerable<string> filePaths)
    {
        var result = new List<MediaAsset>();

        foreach (var path in filePaths.Where(File.Exists))
        {
            var extension = Path.GetExtension(path);
            if (!MediaFileFormats.IsSupportedExtension(extension))
                continue;

            var mediaType = MediaFileFormats.ResolveMediaType(extension);

            result.Add(new MediaAsset
            {
                FilePath = path,
                DisplayName = Path.GetFileName(path),
                Type = mediaType,
                Duration = _mediaDurationService.GetDuration(path, mediaType)
            });
        }

        return result;
    }
}
