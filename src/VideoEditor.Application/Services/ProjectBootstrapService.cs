using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Services;

/// <summary>
/// 	Формирует базовую структуру проекта для MVP-сценария.
/// </summary>
public sealed class ProjectBootstrapService : IProjectBootstrapService
{
    /// <inheritdoc />
    public VideoProject CreateDefaultProject(string projectName = "New Project")
    {
        var project = new VideoProject
        {
            Name = string.IsNullOrWhiteSpace(projectName) ? "New Project" : projectName
        };

        project.Tracks.Add(new TimelineTrack { Name = "Video Track 1" });
        project.Tracks.Add(new TimelineTrack { Name = "Audio Track 1" });

        return project;
    }
}
