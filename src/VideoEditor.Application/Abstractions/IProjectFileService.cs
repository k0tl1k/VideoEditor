using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Abstractions;

/// <summary>
///     Saves and loads editor projects from disk.
/// </summary>
public interface IProjectFileService
{
    /// <summary>
    ///     Saves a project to the specified file path.
    /// </summary>
    void Save(VideoProject project, string filePath);

    /// <summary>
    ///     Loads a project from the specified file path.
    /// </summary>
    VideoProject Load(string filePath);
}
