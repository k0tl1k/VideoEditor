using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;

namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	ViewModel главного окна редактора.
/// </summary>
public sealed class MainWindowViewModel
{
    /// <summary>
    /// 	Текущий проект в сессии редактора.
    /// </summary>
    public VideoProject CurrentProject { get; }

    /// <summary>
    /// 	Путь хранения данных текущего проекта.
    /// </summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// 	Инициализирует ViewModel данными стартового проекта.
    /// </summary>
    public MainWindowViewModel(
        IProjectBootstrapService projectBootstrapService,
        IProjectPathService projectPathService)
    {
        CurrentProject = projectBootstrapService.CreateDefaultProject("Diploma Project");
        ProjectDirectory = projectPathService.BuildProjectDirectory(CurrentProject.Name);
    }
}
