namespace VideoEditor.Application.Abstractions;

/// <summary>
///     Stores paths to recently opened projects.
/// </summary>
public interface IRecentProjectService
{
    /// <summary>
    ///     Returns recent projects that still exist on disk.
    /// </summary>
    IReadOnlyList<RecentProjectItem> GetRecentProjects();

    /// <summary>
    ///     Adds or refreshes a recent project entry.
    /// </summary>
    void AddRecentProject(string filePath);
}
