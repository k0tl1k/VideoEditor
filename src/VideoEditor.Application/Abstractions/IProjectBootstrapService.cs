using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	Создает стартовое состояние проекта для редактора.
/// </summary>
public interface IProjectBootstrapService
{
    /// <summary>
    /// 	Создает новый проект с базовыми дорожками.
    /// </summary>
    /// <param name="projectName">Название проекта.</param>
    VideoProject CreateDefaultProject(string projectName = "New Project");
}
