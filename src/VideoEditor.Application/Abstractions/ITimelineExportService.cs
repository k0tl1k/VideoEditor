using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Abstractions;

public interface ITimelineExportService
{
    Task ExportAsync(
        VideoProject project,
        string outputFilePath,
        TimelineExportOptions options,
        IProgress<TimelineExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
