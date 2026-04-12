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
    private bool _isDraggingRulerPlayhead;

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

    private void Clip_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragClip = (sender as FrameworkElement)?.DataContext as TimelineClipItemViewModel;

        if (_dragClip is not null && DataContext is MainWindowViewModel viewModel)
            viewModel.SelectClipForPreview(_dragClip.ClipId);
    }

    private void DeleteClipMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TimelineClipItemViewModel clip)
            return;

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.DeleteClip(clip.ClipId);
    }

    private void TimelineRuler_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingRulerPlayhead = true;
        TimelineRuler.CaptureMouse();
        SetTimelinePositionFromRuler(e);
        e.Handled = true;
    }

    private void TimelineRuler_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingRulerPlayhead || e.LeftButton != MouseButtonState.Pressed)
            return;

        SetTimelinePositionFromRuler(e);
        e.Handled = true;
    }

    private void TimelineRuler_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingRulerPlayhead)
            return;

        SetTimelinePositionFromRuler(e);
        _isDraggingRulerPlayhead = false;
        TimelineRuler.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void SetTimelinePositionFromRuler(MouseEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var position = e.GetPosition(TimelineRuler);
        viewModel.SetTimelinePlaybackPositionFromCanvasLeft(position.X + TimelineScrollViewer.HorizontalOffset);
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

    private void TrackLane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TimelineClipItemViewModel)
            return;

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.ClearTimelineSelection();
    }

    private void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        RulerMarksTransform.X = -e.HorizontalOffset;
    }

    private void TimelineScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TimelineVerticalScrollViewer.ScrollToVerticalOffset(TimelineVerticalScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
