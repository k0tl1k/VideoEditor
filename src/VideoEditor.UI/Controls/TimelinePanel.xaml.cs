using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoEditor.UI.ViewModels;

namespace VideoEditor.UI.Controls;

public partial class TimelinePanel : UserControl
{
    private const string TimelineClipDragFormat = "VideoEditor.TimelineClip";

    private Point? _dragStartPoint;
    private TimelineClipItemViewModel? _dragClip;

    public TimelinePanel()
    {
        InitializeComponent();
    }

    private void Clip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragClip = (sender as FrameworkElement)?.DataContext as TimelineClipItemViewModel;

        if (_dragClip is not null && DataContext is MainWindowViewModel viewModel)
            viewModel.SelectClipForPreview(_dragClip.ClipId);
    }

    private void Clip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint is null || _dragClip is null)
            return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _dragStartPoint.Value;

        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = new DataObject(TimelineClipDragFormat, _dragClip);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);

        _dragStartPoint = null;
        _dragClip = null;
    }

    private void TrackLane_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TimelineClipDragFormat))
            return;

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (e.Data.GetData(TimelineClipDragFormat) is not TimelineClipItemViewModel clip)
            return;

        if ((sender as FrameworkElement)?.DataContext is not TimelineTrackItemViewModel track)
            return;

        var lane = (FrameworkElement)sender;
        var position = e.GetPosition(lane);
        var targetLeft = position.X + TimelineScrollViewer.HorizontalOffset;
        viewModel.MoveClip(clip.ClipId, track.TrackName, targetLeft);
    }

    private void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        RulerMarksTransform.X = -e.HorizontalOffset;
    }
}
