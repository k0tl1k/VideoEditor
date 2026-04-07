using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;
namespace VideoEditor.Application.Services;

/// <summary>
/// 	Определяет тип медиа и создаёт доменные ассеты при импорте.
/// </summary>
public sealed class MediaImportService : IMediaImportService
{
    /// <inheritdoc />
    public IReadOnlyList<MediaAsset> Import(IEnumerable<string> filePaths)
    {
        var result = new List<MediaAsset>();

        foreach (var path in filePaths.Where(File.Exists))
        {
            var extension = Path.GetExtension(path);
            var mediaType = MediaFileFormats.ResolveMediaType(extension);

            result.Add(new MediaAsset
            {
                FilePath = path,
                DisplayName = Path.GetFileName(path),
                Type = mediaType,
                Duration = TimeSpan.Zero
            });
        }

        return result;
    }
}
