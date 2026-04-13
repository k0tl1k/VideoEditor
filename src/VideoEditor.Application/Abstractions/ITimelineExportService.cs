using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	Экспортирует проект таймлайна в видеофайл.
/// </summary>
public interface ITimelineExportService
{
    /// <summary>
    /// 	Запускает экспорт проекта.
    /// </summary>
    /// <param name="project"> Проект для экспорта. </param>
    /// <param name="outputFilePath"> Путь к выходному файлу. </param>
    /// <param name="options"> Параметры экспорта. </param>
    /// <param name="progress"> Получатель прогресса экспорта. </param>
    /// <param name="cancellationToken"> Токен отмены операции. </param>
    Task ExportAsync(
        VideoProject project,
        string outputFilePath,
        TimelineExportOptions options,
        IProgress<TimelineExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
