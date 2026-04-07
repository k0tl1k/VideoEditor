using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using VideoEditor.Application;
using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;
using VideoEditor.UI.Commands;

namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	ViewModel главного окна редактора.
/// </summary>
public sealed class MainWindowViewModel
{
    private readonly IMediaImportService _mediaImportService;
    private readonly IMediaThumbnailService _mediaThumbnailService;

    /// <summary>
    /// 	Текущий проект в сессии редактора.
    /// </summary>
    public VideoProject CurrentProject { get; }

    /// <summary>
    /// 	Путь хранения данных текущего проекта.
    /// </summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// 	Импортированные ассеты для отображения в панели проекта.
    /// </summary>
    public ObservableCollection<ImportedMediaItemViewModel> ImportedMedia { get; } = new();

    /// <summary>
    /// 	Команда импорта медиафайлов.
    /// </summary>
    public ICommand ImportMediaCommand { get; }

    /// <summary>
    /// 	Инициализирует ViewModel данными стартового проекта.
    /// </summary>
    /// <param name="projectBootstrapService"> Сервис создания стартового проекта. </param>
    /// <param name="projectPathService"> Сервис построения путей проекта. </param>
    /// <param name="mediaImportService"> Сервис импорта медиафайлов. </param>
    /// <param name="mediaThumbnailService"> Сервис генерации миниатюр. </param>
    public MainWindowViewModel(
        IProjectBootstrapService projectBootstrapService,
        IProjectPathService projectPathService,
        IMediaImportService mediaImportService,
        IMediaThumbnailService mediaThumbnailService)
    {
        _mediaImportService = mediaImportService;
        _mediaThumbnailService = mediaThumbnailService;

        CurrentProject = projectBootstrapService.CreateDefaultProject("Diploma Project");
        ProjectDirectory = projectPathService.BuildProjectDirectory(CurrentProject.Name);

        ImportMediaCommand = new RelayCommand(ImportMedia);
    }

    private void ImportMedia()
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = MediaFileFormats.OpenFileDialogFilter
        };

        if (dialog.ShowDialog() != true)
            return;

        var imported = _mediaImportService.Import(dialog.FileNames);

        foreach (var asset in imported)
        {
            var existingAsset = CurrentProject.MediaAssets.FirstOrDefault(x =>
                x.FilePath.Equals(asset.FilePath, StringComparison.OrdinalIgnoreCase));

            if (existingAsset is not null)
            {
                var existingItem = ImportedMedia.FirstOrDefault(x =>
                    x.Asset.FilePath.Equals(asset.FilePath, StringComparison.OrdinalIgnoreCase));

                if (existingItem is not null && !existingItem.HasThumbnail)
                {
                    var refreshedThumbnail = _mediaThumbnailService.GetThumbnailPath(existingAsset);
                    if (!string.IsNullOrWhiteSpace(refreshedThumbnail))
                    {
                        var index = ImportedMedia.IndexOf(existingItem);
                        ImportedMedia[index] = new ImportedMediaItemViewModel(existingAsset, refreshedThumbnail);
                    }
                }

                continue;
            }

            CurrentProject.MediaAssets.Add(asset);
            var thumbnailPath = _mediaThumbnailService.GetThumbnailPath(asset);
            ImportedMedia.Add(new ImportedMediaItemViewModel(asset, thumbnailPath));
        }
    }
}
