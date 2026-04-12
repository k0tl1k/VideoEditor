using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoEditor.UI.ViewModels;

namespace VideoEditor.UI.Controls;

public partial class TimelinePanel : UserControl
{
    private const string TimelineClipDragFormat = "VideoEditor.TimelineClip";
    private const string ImportedMediaDragFormat = "VideoEditor.ImportedMedia";

    private Point? _dragStartPoint;
    private TimelineClipItemViewModel? _dragClip;
    private bool _isDraggingRulerPlayhead;
    private bool _isSynchronizingTimelineHorizontalScroll;

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
        // No longer used; the top ruler panel handles direct clicks and drags.
    }

    private void TimelineRuler_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // No longer used; the top ruler panel handles direct clicks and drags.
    }

    private void TimelineRuler_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // No longer used; the top ruler panel handles direct clicks and drags.
    }

    private void TimelineRulerPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingRulerPlayhead = true;
        TimelineRulerPanel.CaptureMouse();
        SetTimelinePositionFromRulerPanel(e);
        e.Handled = true;
    }

    private void TimelineRulerPanel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingRulerPlayhead || e.LeftButton != MouseButtonState.Pressed)
            return;

        SetTimelinePositionFromRulerPanel(e);
        e.Handled = true;
    }

    private void TimelineRulerPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingRulerPlayhead)
            return;

        SetTimelinePositionFromRulerPanel(e);
        _isDraggingRulerPlayhead = false;
        TimelineRulerPanel.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void SetTimelinePositionFromRulerPanel(MouseEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var position = e.GetPosition(TimelineRulerPanel);
        var rulerLeftOffset = 118d;
        var canvasLeft = Math.Max(0, position.X - rulerLeftOffset) + TimelineScrollViewer.HorizontalOffset;
        viewModel.SetTimelinePlaybackPositionFromCanvasLeft(canvasLeft);
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
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if ((sender as FrameworkElement)?.DataContext is not TimelineTrackItemViewModel track)
            return;

        var lane = (FrameworkElement)sender;
        var position = e.GetPosition(lane);
        var targetLeft = position.X + TimelineScrollViewer.HorizontalOffset;

        if (e.Data.GetDataPresent(ImportedMediaDragFormat) &&
            e.Data.GetData(ImportedMediaDragFormat) is ImportedMediaItemViewModel media)
        {
            viewModel.InsertMediaToTimeline(media.Asset, targetLeft);
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(TimelineClipDragFormat))
            return;

        if (e.Data.GetData(TimelineClipDragFormat) is not TimelineClipItemViewModel clip)
            return;

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

        if (_isSynchronizingTimelineHorizontalScroll)
            return;

        _isSynchronizingTimelineHorizontalScroll = true;
        try
        {
            TimelineHorizontalScrollBar.Maximum = Math.Max(0, e.ExtentWidth - e.ViewportWidth);
            TimelineHorizontalScrollBar.ViewportSize = e.ViewportWidth;
            TimelineHorizontalScrollBar.LargeChange = Math.Max(1, e.ViewportWidth);
            TimelineHorizontalScrollBar.SmallChange = Math.Max(1, e.ViewportWidth / 10);
            TimelineHorizontalScrollBar.Value = e.HorizontalOffset;
        }
        finally
        {
            _isSynchronizingTimelineHorizontalScroll = false;
        }
    }

    private void TimelineScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TimelineVerticalScrollViewer.ScrollToVerticalOffset(TimelineVerticalScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void TimelineHorizontalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSynchronizingTimelineHorizontalScroll)
            return;

        _isSynchronizingTimelineHorizontalScroll = true;
        try
        {
            TimelineScrollViewer.ScrollToHorizontalOffset(e.NewValue);
        }
        finally
        {
            _isSynchronizingTimelineHorizontalScroll = false;
        }
    }
}
