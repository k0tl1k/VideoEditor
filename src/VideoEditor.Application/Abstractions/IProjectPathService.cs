namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	Формирует путь хранения данных проекта.
/// </summary>
public interface IProjectPathService
{
    /// <summary>
    /// 	Возвращает директорию проекта по его имени.
    /// </summary>
    string BuildProjectDirectory(string projectName);
}
