using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoEditor.UI.ViewModels;
using VideoEditor.UI.Windows;

namespace VideoEditor.UI.Controls;

public partial class ProjectPanel : UserControl
{
    private const string ImportedMediaDragFormat = "VideoEditor.ImportedMedia";

    private Point? _dragStartPoint;
    private ImportedMediaItemViewModel? _dragMedia;

    public ProjectPanel()
    {
        InitializeComponent();
    }

    private void ImportMediaMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.ImportMediaCommand.Execute(null);
    }

    private void DeleteMediaMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ImportedMediaItemViewModel media)
            return;

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var usageCount = viewModel.GetImportedMediaUsageCount(media.Asset.Id);
        if (usageCount > 0)
        {
            var detailsText = usageCount == 1
                ? "There is 1 linked clip on the timeline."
                : $"There are {usageCount} linked clips on the timeline.";

            var dialog = new DeleteMediaConfirmationWindow(
                "Delete media",
                media.DisplayName,
                "This media is used on the timeline. Delete it and remove linked clips too?",
                detailsText)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() != true)
                return;
        }

        viewModel.RemoveImportedMedia(media.Asset.Id);
    }

    private void ProjectPanel_DragEnter(object sender, DragEventArgs e)
    {
        SetDropEffect(e);
    }

    private void ProjectPanel_DragOver(object sender, DragEventArgs e)
    {
        SetDropEffect(e);
    }

    private void ProjectPanel_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (!TryGetDroppedFiles(e.Data, out var filePaths))
            return;

        viewModel.ImportMediaFiles(filePaths);
    }

    private void MediaCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragMedia = (sender as FrameworkElement)?.DataContext as ImportedMediaItemViewModel;

        if (_dragMedia is not null && DataContext is MainWindowViewModel viewModel)
            viewModel.SelectedImportedMedia = _dragMedia;
    }

    private void MediaCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint is null || _dragMedia is null)
            return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _dragStartPoint.Value;

        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = new DataObject(ImportedMediaDragFormat, _dragMedia);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);

        _dragStartPoint = null;
        _dragMedia = null;
    }

    private static void SetDropEffect(DragEventArgs e)
    {
        e.Effects = TryGetDroppedFiles(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool TryGetDroppedFiles(IDataObject data, out string[] filePaths)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            filePaths = [];
            return false;
        }

        filePaths = (string[]?)data.GetData(DataFormats.FileDrop) ?? [];
        filePaths = filePaths.Where(File.Exists).ToArray();
        return filePaths.Length > 0;
    }
}
