using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VideoEditor.Domain.Enums;
using VideoEditor.UI.ViewModels;

namespace VideoEditor.UI.Controls;

/// <summary>
/// 	Панель таймлайна с дорожками, линейкой и drag/drop-логикой.
/// </summary>
public partial class TimelinePanel : UserControl
{
    private const string TimelineClipDragFormat = "VideoEditor.TimelineClip";
    private const string ImportedMediaDragFormat = "VideoEditor.ImportedMedia";

    private Point? _dragStartPoint;
    private double _dragClipGrabOffsetX;
    private TimelineClipItemViewModel? _dragClip;
    private Border? _dragGhost;
    private Border? _dragGhostLane;
    private readonly List<Border> _dragGroupGhosts = new();
    private readonly List<FrameworkElement> _dragEdgeMarkers = new();
    private bool _dragGhostIsAnchored;
    private Grid? _dragSnapGuide;
    private Border? _dragSnapGuideLane;
    private Point? _selectionStartPoint;
    private Border? _selectionLane;
    private Border? _selectionRectangle;
    private bool _isSelectingRange;
    private bool _selectionAddsToExisting;
    private bool _isDraggingRulerPlayhead;
    private bool _isSynchronizingTimelineHorizontalScroll;

    public TimelinePanel()
    {
        InitializeComponent();
        AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(TimelinePanel_PreviewMouseLeftButtonDown), true);
        AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(TimelinePanel_PreviewMouseMove), true);
        AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(TimelinePanel_PreviewMouseLeftButtonUp), true);
    }

    private void TimelinePanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = HandleTimelineKeyDown(e.Key);
    }

    /// <summary>
    /// 	Обрабатывает горячие клавиши таймлайна из окна и самой панели.
    /// </summary>
    /// <param name="key"> Нажатая клавиша. </param>
    /// <returns> True, если клавиша обработана таймлайном. </returns>
    public bool HandleTimelineKeyDown(Key key)
    {
        var hasControl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var hasAlt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (hasControl && key == Key.C)
        {
            return DataContext is MainWindowViewModel viewModel &&
                viewModel.CopySelectedTimelineClips();
        }

        if (hasControl && key == Key.V)
        {
            return DataContext is MainWindowViewModel viewModel &&
                viewModel.PasteCopiedTimelineClips();
        }

        if (hasControl && (key == Key.OemPlus || key == Key.Add))
        {
            ExecuteTimelineZoom(increase: true);
            return true;
        }

        if (hasControl && (key == Key.OemMinus || key == Key.Subtract))
        {
            ExecuteTimelineZoom(increase: false);
            return true;
        }

        if (hasAlt && key == Key.Left)
        {
            ScrollTimelineHorizontal(-ResolveTimelineKeyboardScrollStep());
            return true;
        }

        if (hasAlt && key == Key.Right)
        {
            ScrollTimelineHorizontal(ResolveTimelineKeyboardScrollStep());
            return true;
        }

        if (Keyboard.Modifiers == ModifierKeys.None && key == Key.V)
            return SelectTimelineTool(useRazor: false);

        if (Keyboard.Modifiers == ModifierKeys.None && key == Key.C)
            return SelectTimelineTool(useRazor: true);

        return false;
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject ancestor)
    {
        while (child is not null)
        {
            if (ReferenceEquals(child, ancestor))
                return true;

            child = VisualTreeHelper.GetParent(child);
        }

        return false;
    }

    private void TimelinePanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();

        if (DataContext is MainWindowViewModel timelineViewModel2 && timelineViewModel2.IsRazorTimelineToolActive)
            return;

        if (_selectionStartPoint is not null)
            return;

        var originalSource = e.OriginalSource as DependencyObject;
        if (originalSource is null || !IsDescendantOf(originalSource, TimelineScrollViewer))
            return;

        if ((e.OriginalSource as FrameworkElement)?.DataContext is TimelineClipItemViewModel)
            return;

        _selectionStartPoint = e.GetPosition(TimelineSelectionOverlay);
        _selectionAddsToExisting = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _isSelectingRange = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void TimelinePanel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_selectionStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (DataContext is MainWindowViewModel timelineViewModel &&
            timelineViewModel.IsRazorTimelineToolActive)
            return;

        if (!ReferenceEquals(sender, this))
            return;

        var currentPoint = e.GetPosition(TimelineSelectionOverlay);
        var delta = currentPoint - _selectionStartPoint.Value;
        if (!_isSelectingRange &&
            Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _isSelectingRange = true;
        ShowSelectionRectangle(_selectionStartPoint.Value, currentPoint);
        e.Handled = true;
    }

    private void TimelinePanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionStartPoint is null)
            return;

        if (DataContext is MainWindowViewModel timelineViewModel &&
            timelineViewModel.IsRazorTimelineToolActive)
            return;

        if (!ReferenceEquals(sender, this))
            return;

        var startPoint = _selectionStartPoint.Value;
        var endPoint = e.GetPosition(TimelineSelectionOverlay);
        ReleaseMouseCapture();
        ClearSelectionRectangle();

        if (DataContext is not MainWindowViewModel timelineViewModel2)
        {
            _selectionStartPoint = null;
            return;
        }

        if (!_isSelectingRange)
        {
            timelineViewModel2.ClearTimelineSelection();
            _isSelectingRange = false;
            _selectionStartPoint = null;
            e.Handled = true;
            return;
        }

        var left = Math.Min(startPoint.X, endPoint.X) + TimelineScrollViewer.HorizontalOffset;
        var right = Math.Max(startPoint.X, endPoint.X) + TimelineScrollViewer.HorizontalOffset;
        timelineViewModel2.SelectTimelineClipsInTrackRange(string.Empty, left, right, _selectionAddsToExisting);
        _isSelectingRange = false;
        _selectionStartPoint = null;
        e.Handled = true;
    }

    private void Clip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragClip = (sender as FrameworkElement)?.DataContext as TimelineClipItemViewModel;

        if (_dragClip is not null && DataContext is MainWindowViewModel timelineViewModel)
        {
            if (timelineViewModel.IsRazorTimelineToolActive)
            {
                var clipElement = sender as FrameworkElement;
                if (clipElement is not null)
                {
                    var localPoint = e.GetPosition(clipElement);
                    var splitOffsetSeconds = Math.Max(0, localPoint.X / timelineViewModel.TimelinePixelsPerSecond);
                    var splitPosition = TimeSpan.FromSeconds(_dragClip.Left / timelineViewModel.TimelinePixelsPerSecond + splitOffsetSeconds);
                    timelineViewModel.SplitClipAtTimelinePosition(_dragClip.ClipId, splitPosition);
                    e.Handled = true;
                    return;
                }
            }

            _dragStartPoint = e.GetPosition(this);
            _dragClipGrabOffsetX = sender is IInputElement inputElement ? e.GetPosition(inputElement).X : 0;
            var isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (isCtrlPressed || !timelineViewModel.IsTimelineClipSelected(_dragClip.ClipId) || timelineViewModel.SelectedTimelineClipCount <= 1)
                timelineViewModel.SelectClipForPreview(_dragClip.ClipId, isCtrlPressed);
        }
    }

    private void Clip_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragClip = (sender as FrameworkElement)?.DataContext as TimelineClipItemViewModel;

        if (_dragClip is not null && DataContext is MainWindowViewModel viewModel)
        {
            if (!viewModel.IsTimelineClipSelected(_dragClip.ClipId))
                viewModel.SelectClipForPreview(_dragClip.ClipId);
        }
    }

    private void DeleteClipMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TimelineClipItemViewModel clip)
            return;

        if (DataContext is not MainWindowViewModel timelineViewModel)
            return;

        if (clip.IsSelected && timelineViewModel.SelectedTimelineClipCount > 1)
            timelineViewModel.DeleteSelectedTimelineClipsCommand.Execute(null);
        else
            timelineViewModel.DeleteClip(clip.ClipId, deleteLinkedClips: false);
    }

    private void DeleteLinkedClipMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TimelineClipItemViewModel clip)
            return;

        if (DataContext is not MainWindowViewModel timelineViewModel)
            return;

        timelineViewModel.DeleteClip(clip.ClipId, deleteLinkedClips: true);
    }

    private void SplitClipMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel timelineViewModel)
            return;

        timelineViewModel.SplitSelectedClipCommand.Execute(null);
    }

    private void UnlinkClipMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TimelineClipItemViewModel clip)
            return;

        if (DataContext is not MainWindowViewModel timelineViewModel)
            return;

        timelineViewModel.UnlinkClip(clip.ClipId);
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
        if (DataContext is not MainWindowViewModel timelineViewModel)
            return;

        var position = e.GetPosition(TimelineRulerPanel);
        var rulerLeftOffset = 118d;
        var canvasLeft = Math.Max(0, position.X - rulerLeftOffset) + TimelineScrollViewer.HorizontalOffset;
        timelineViewModel.SetTimelinePlaybackPositionFromCanvasLeft(canvasLeft);
    }

    private void Clip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is MainWindowViewModel timelineViewModel &&
            timelineViewModel.IsRazorTimelineToolActive)
            return;

        if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint is null || _dragClip is null)
            return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _dragStartPoint.Value;

        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = new DataObject(TimelineClipDragFormat, _dragClip);
        var dragViewModel = DataContext as MainWindowViewModel;
        if (dragViewModel is not null)
            dragViewModel.BeginTimelineClipDrag(_dragClip.ClipId);

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        finally
        {
            if (dragViewModel is not null)
                dragViewModel.EndTimelineClipDrag(_dragClip.ClipId);

            ClearDragGhost();
            _dragStartPoint = null;
            _dragClipGrabOffsetX = 0;
            _dragClip = null;
        }
    }

    private void TrackLane_DragEnter(object sender, DragEventArgs e)
    {
        UpdateTrackLaneDragGhost(sender, e);
    }

    private void TrackLane_DragOver(object sender, DragEventArgs e)
    {
        UpdateTrackLaneDragGhost(sender, e);
    }

    private void TrackLane_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border lane)
            ClearDragGhost(lane);
    }

    private void SelectionOverlay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }

    private void SelectionOverlay_PreviewMouseMove(object sender, MouseEventArgs e) { }

    private void SelectionOverlay_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }

    private void TrackLane_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel timelineViewModel)
            return;

        if ((sender as FrameworkElement)?.DataContext is not TimelineTrackItemViewModel track)
            return;

        var lane = (FrameworkElement)sender;
        var position = e.GetPosition(lane);
        var targetLeft = position.X + TimelineScrollViewer.HorizontalOffset;
        ClearDragGhost(sender as Border);

        if (e.Data.GetDataPresent(ImportedMediaDragFormat) &&
            e.Data.GetData(ImportedMediaDragFormat) is ImportedMediaItemViewModel media)
        {
            timelineViewModel.InsertMediaToTimeline(media.Asset, targetLeft);
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(TimelineClipDragFormat))
            return;

        if (e.Data.GetData(TimelineClipDragFormat) is not TimelineClipItemViewModel clip)
            return;

        timelineViewModel.MoveClip(clip.ClipId, track.TrackName, Math.Max(0, targetLeft - _dragClipGrabOffsetX));
    }

    private void UpdateTrackLaneDragGhost(object sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel timelineViewModel)
            return;

        if (sender is not Border lane || lane.DataContext is not TimelineTrackItemViewModel track)
            return;

        if (e.Data.GetDataPresent(ImportedMediaDragFormat) &&
            e.Data.GetData(ImportedMediaDragFormat) is ImportedMediaItemViewModel media)
        {
            var mediaLanePosition = e.GetPosition(lane);
            var mediaDesiredLeft = mediaLanePosition.X + TimelineScrollViewer.HorizontalOffset;
            var mediaGhostWidth = ResolveImportedMediaGhostWidth(timelineViewModel, media);
            ShowDragGhost(lane, mediaDesiredLeft, mediaGhostWidth, media.DisplayName, false);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(TimelineClipDragFormat) ||
            e.Data.GetData(TimelineClipDragFormat) is not TimelineClipItemViewModel clip)
        {
            ClearDragGhost(lane);
            e.Effects = DragDropEffects.None;
            return;
        }

        var lanePosition = e.GetPosition(lane);
        var desiredLeft = Math.Max(0, lanePosition.X + TimelineScrollViewer.HorizontalOffset - _dragClipGrabOffsetX);
        var placement = timelineViewModel.ResolveClipDragPlacement(track.TrackName, clip.ClipId, desiredLeft);

        var selectedClips = track.Clips
            .Where(x => x.IsSelected)
            .OrderBy(x => x.Left)
            .ToList();

        if (clip.IsSelected && selectedClips.Count > 1)
            ShowDragGhostGroup(lane, clip, selectedClips, placement.Left, placement.IsAnchored);
        else
            ShowDragGhost(lane, placement.Left, clip.Width, clip.DisplayName, placement.IsAnchored);

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ShowDragGhostGroup(
        Border lane,
        TimelineClipItemViewModel anchorClip,
        IReadOnlyList<TimelineClipItemViewModel> selectedClips,
        double anchorLeft,
        bool isAnchored)
    {
        if (lane.Child is not Canvas canvas)
            return;

        ClearDragGhost(lane);

        var clipOffsets = selectedClips
            .Select(selectedClip =>
            {
                var offset = selectedClip.Left - anchorClip.Left;
                var left = anchorLeft + offset;
                var width = Math.Max(4, selectedClip.Width);
                return new { SelectedClip = selectedClip, Left = left, Width = width };
            })
            .ToList();

        if (clipOffsets.Count == 0)
            return;

        var leftEdge = clipOffsets.Min(x => x.Left);
        var rightEdge = clipOffsets.Max(x => x.Left + x.Width);
        var totalWidth = Math.Max(4, rightEdge - leftEdge);
        var clipCount = clipOffsets.Count;
        var groupLabel = clipCount == 1
            ? anchorClip.DisplayName
            : $"{anchorClip.DisplayName} + {clipCount - 1}";

        var ghostBorderBrush = isAnchored
            ? new SolidColorBrush(Color.FromArgb(235, 255, 184, 107))
            : new SolidColorBrush(Color.FromArgb(235, 53, 211, 255));
        var ghostBackground = isAnchored
            ? new LinearGradientBrush(
                Color.FromArgb(70, 255, 184, 107),
                Color.FromArgb(28, 255, 184, 107),
                90)
            : new LinearGradientBrush(
                Color.FromArgb(58, 34, 47, 66),
                Color.FromArgb(34, 23, 32, 48),
                90);

        var ghost = new Border
        {
            Width = totalWidth,
            Height = 48,
            Background = ghostBackground,
            BorderBrush = ghostBorderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Opacity = isAnchored ? 0.94 : 0.86,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 0,
                Color = Colors.Black,
                Opacity = 0.22
            }
        };

        var content = new Grid
        {
            Margin = new Thickness(12, 6, 12, 6)
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var textStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };

        textStack.Children.Add(new TextBlock
        {
            Text = isAnchored ? "Snap move" : "Move selection",
            Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        textStack.Children.Add(new TextBlock
        {
            Text = groupLabel,
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        textStack.Children.Add(new TextBlock
        {
            Text = clipCount == 1 ? "1 clip" : $"{clipCount} clips",
            Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0)
        });

        Grid.SetColumn(textStack, 0);
        content.Children.Add(textStack);
        ghost.Child = content;

        Canvas.SetLeft(ghost, leftEdge);
        Canvas.SetTop(ghost, 8);
        canvas.Children.Add(ghost);
        _dragGroupGhosts.Add(ghost);
        AddDragEdgeMarkers(canvas, leftEdge, totalWidth, isAnchored);

        _dragGhostLane = lane;
        _dragGhostIsAnchored = isAnchored;
    }

    private static Grid CreateSnapEdgeMarker(bool isAnchored)
    {
        var markerBrush = isAnchored
            ? new SolidColorBrush(Color.FromArgb(230, 255, 184, 107))
            : new SolidColorBrush(Color.FromArgb(230, 53, 211, 255));

        var marker = new Grid
        {
            Width = 20,
            Height = 66,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0.95
        };

        marker.Children.Add(new Border
        {
            Width = 3,
            Height = 56,
            Background = markerBrush,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        });

        marker.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            Background = markerBrush,
            CornerRadius = new CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        });

        return marker;
    }

    private void AddDragEdgeMarkers(Canvas canvas, double left, double width, bool isAnchored)
    {
        ClearDragEdgeMarkers(canvas);

        var leftMarker = CreateSnapEdgeMarker(isAnchored);
        Canvas.SetLeft(leftMarker, left - 10);
        Canvas.SetTop(leftMarker, 4);
        canvas.Children.Add(leftMarker);
        _dragEdgeMarkers.Add(leftMarker);

        var rightMarker = CreateSnapEdgeMarker(isAnchored);
        Canvas.SetLeft(rightMarker, left + width - 10);
        Canvas.SetTop(rightMarker, 4);
        canvas.Children.Add(rightMarker);
        _dragEdgeMarkers.Add(rightMarker);
    }

    private void UpdateDragEdgeMarkers(Canvas canvas, double left, double width, bool isAnchored)
    {
        if (_dragEdgeMarkers.Count != 2)
        {
            AddDragEdgeMarkers(canvas, left, width, isAnchored);
            return;
        }

        Canvas.SetLeft(_dragEdgeMarkers[0], left - 10);
        Canvas.SetTop(_dragEdgeMarkers[0], 4);
        Canvas.SetLeft(_dragEdgeMarkers[1], left + width - 10);
        Canvas.SetTop(_dragEdgeMarkers[1], 4);
    }

    private void ClearDragEdgeMarkers(Canvas? lane = null)
    {
        if (_dragEdgeMarkers.Count == 0)
            return;

        if (lane is not null && _dragGhostLane?.Child is Canvas currentCanvas && !ReferenceEquals(currentCanvas, lane))
            return;

        if (_dragGhostLane?.Child is Canvas canvas)
        {
            foreach (var marker in _dragEdgeMarkers)
                canvas.Children.Remove(marker);
        }

        _dragEdgeMarkers.Clear();
    }

    private void ShowDragGhost(Border lane, double left, double width, string label, bool isAnchored)
    {
        if (lane.Child is not Canvas canvas)
            return;

        if (_dragGhost is not null && _dragGhostLane == lane)
        {
            Canvas.SetLeft(_dragGhost, left);
            _dragGhost.Width = Math.Max(4, width);
            UpdateDragGhostStyle(_dragGhost, isAnchored);
            UpdateDragEdgeMarkers(canvas, left, _dragGhost.Width, isAnchored);
            return;
        }

        ClearDragGhost();

        var ghostBorderBrush = isAnchored
            ? new SolidColorBrush(Color.FromArgb(230, 255, 184, 107))
            : new SolidColorBrush(Color.FromArgb(220, 53, 211, 255));
        var ghostBackground = isAnchored
            ? new SolidColorBrush(Color.FromArgb(56, 255, 184, 107))
            : new SolidColorBrush(Color.FromArgb(44, 255, 255, 255));

        var ghost = new Border
        {
            Width = Math.Max(4, width),
            Height = 42,
            Background = ghostBackground,
            BorderBrush = ghostBorderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Opacity = 0.82,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top
        };

        ghost.Child = new TextBlock
        {
            Text = label,
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Canvas.SetLeft(ghost, left);
        Canvas.SetTop(ghost, 8);
        canvas.Children.Add(ghost);
        _dragGhost = ghost;
        _dragGhostLane = lane;
        _dragGhostIsAnchored = isAnchored;
        AddDragEdgeMarkers(canvas, left, _dragGhost.Width, isAnchored);
    }

    private static double ResolveImportedMediaGhostWidth(MainWindowViewModel timelineViewModel, ImportedMediaItemViewModel media)
    {
        var fallbackDurationSeconds = media.Asset.Type == MediaType.Image ? 5 : 10;
        var durationSeconds = media.Asset.Duration > TimeSpan.Zero
            ? media.Asset.Duration.TotalSeconds
            : fallbackDurationSeconds;

        return Math.Max(4, durationSeconds * timelineViewModel.TimelinePixelsPerSecond);
    }

    private void UpdateDragGhostStyle(Border ghost, bool isAnchored)
    {
        if (_dragGhostIsAnchored == isAnchored)
            return;

        _dragGhostIsAnchored = isAnchored;
        ghost.BorderBrush = isAnchored
            ? new SolidColorBrush(Color.FromArgb(230, 255, 184, 107))
            : new SolidColorBrush(Color.FromArgb(220, 53, 211, 255));
    }

    private void UpdateDragSnapGuide(Border lane, double left, bool isAnchored)
    {
        if (lane.Child is not Canvas canvas)
            return;

        if (_dragSnapGuide is not null && _dragSnapGuideLane == lane)
        {
            Canvas.SetLeft(_dragSnapGuide, left - 10);
            UpdateSnapGuideStyle(_dragSnapGuide, isAnchored);
            return;
        }

        ClearDragSnapGuide();

        var guide = new Grid
        {
            Width = 20,
            Height = 66,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = isAnchored ? 1.0 : 0.7
        };

        var guideBrush = isAnchored
            ? new SolidColorBrush(Color.FromArgb(240, 255, 184, 107))
            : new SolidColorBrush(Color.FromArgb(220, 53, 211, 255));

        guide.Children.Add(new Border
        {
            Width = 3,
            Height = 56,
            Background = guideBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(2)
        });

        guide.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            Background = guideBrush,
            CornerRadius = new CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 0)
        });

        Canvas.SetLeft(guide, left - 10);
        Canvas.SetTop(guide, 4);
        canvas.Children.Add(guide);
        _dragSnapGuide = guide;
        _dragSnapGuideLane = lane;
    }

    private static void UpdateSnapGuideStyle(Grid guide, bool isAnchored)
    {
        var guideBrush = isAnchored
            ? new SolidColorBrush(Color.FromArgb(240, 255, 184, 107))
            : new SolidColorBrush(Color.FromArgb(220, 53, 211, 255));

        if (guide.Children[0] is Border stem)
            stem.Background = guideBrush;

        if (guide.Children[1] is Border cap)
            cap.Background = guideBrush;
    }

    private void ClearDragGhost(Border? lane = null)
    {
        if (_dragGhost is null && _dragGroupGhosts.Count == 0)
            return;

        if (lane is not null && _dragGhostLane is not null && _dragGhostLane != lane)
            return;

        if (_dragGhostLane?.Child is Canvas canvas)
        {
            if (_dragGhost is not null)
                canvas.Children.Remove(_dragGhost);

            foreach (var groupGhost in _dragGroupGhosts)
                canvas.Children.Remove(groupGhost);

            foreach (var marker in _dragEdgeMarkers)
                canvas.Children.Remove(marker);
        }

        _dragGhost = null;
        _dragGhostLane = null;
        _dragGhostIsAnchored = false;
        _dragGroupGhosts.Clear();
        _dragEdgeMarkers.Clear();
        ClearDragSnapGuide(lane);
    }

    private void ClearDragSnapGuide(Border? lane = null)
    {
        if (_dragSnapGuide is null)
            return;

        if (lane is not null && _dragSnapGuideLane is not null && _dragSnapGuideLane != lane)
            return;

        if (_dragSnapGuideLane?.Child is Canvas canvas)
            canvas.Children.Remove(_dragSnapGuide);

        _dragSnapGuide = null;
        _dragSnapGuideLane = null;
    }

    private void TrackLane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectionStartPoint is not null)
        {
            e.Handled = true;
            return;
        }

        if (DataContext is MainWindowViewModel timelineViewModel && timelineViewModel.IsRazorTimelineToolActive)
        {
            e.Handled = true;
            return;
        }

        if ((e.OriginalSource as FrameworkElement)?.DataContext is TimelineClipItemViewModel)
            return;

        if (sender is not Border lane)
            return;

        _selectionStartPoint = e.GetPosition(lane);
        _selectionLane = lane;
        _selectionAddsToExisting = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _isSelectingRange = false;
        lane.CaptureMouse();
        e.Handled = true;
    }

    private void TrackLane_MouseMove(object sender, MouseEventArgs e)
    {
        // Global selection is handled at the panel level now.
    }

    private void TrackLane_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Global selection is handled at the panel level now.
    }

    private void ShowSelectionRectangle(Point startPoint, Point currentPoint)
    {
        if (TimelineSelectionOverlay is null)
            return;

        if (_selectionRectangle is null)
        {
            _selectionRectangle = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(38, 53, 211, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(230, 53, 211, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                IsHitTestVisible = false
            };
            TimelineSelectionOverlay.Children.Add(_selectionRectangle);
        }

        var left = Math.Min(startPoint.X, currentPoint.X);
        var width = Math.Abs(currentPoint.X - startPoint.X);

        Canvas.SetLeft(_selectionRectangle, left);
        Canvas.SetTop(_selectionRectangle, 0);
        _selectionRectangle.Width = width;
        _selectionRectangle.Height = TimelineSelectionOverlay.ActualHeight > 0
            ? TimelineSelectionOverlay.ActualHeight
            : 58;
    }

    private void ClearSelectionRectangle()
    {
        if (_selectionRectangle is null)
            return;

        TimelineSelectionOverlay.Children.Remove(_selectionRectangle);

        _selectionRectangle = null;
    }

    private void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSynchronizingTimelineHorizontalScroll)
            return;

        _isSynchronizingTimelineHorizontalScroll = true;
        try
        {
            var maximum = Math.Max(0, e.ExtentWidth - e.ViewportWidth);
            var offset = maximum <= 0 ? 0 : Math.Min(e.HorizontalOffset, maximum);
            if (maximum <= 0 && e.HorizontalOffset > 0)
                TimelineScrollViewer.ScrollToHorizontalOffset(0);

            VirtualTimelineRuler.HorizontalOffset = offset;
            TimelineHorizontalScrollBar.Maximum = maximum;
            TimelineHorizontalScrollBar.ViewportSize = e.ViewportWidth;
            TimelineHorizontalScrollBar.LargeChange = Math.Max(1, e.ViewportWidth);
            TimelineHorizontalScrollBar.SmallChange = Math.Max(1, e.ViewportWidth / 10);
            TimelineHorizontalScrollBar.Value = offset;
        }
        finally
        {
            _isSynchronizingTimelineHorizontalScroll = false;
        }
    }

    private void TimelinePanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = HandleTimelineMouseWheel(e.Delta);
    }

    /// <summary>
    /// 	Обрабатывает колесо мыши для вертикального скролла, горизонтального скролла и зума.
    /// </summary>
    /// <param name="delta"> Направление прокрутки колеса. </param>
    /// <returns> True, если колесо обработано таймлайном. </returns>
    public bool HandleTimelineMouseWheel(int delta)
    {
        Focus();

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ExecuteTimelineZoom(increase: delta > 0);
            return true;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            ScrollTimelineHorizontal(-Math.Sign(delta) * ResolveTimelineWheelScrollStep());
            return true;
        }

        TimelineVerticalScrollViewer.ScrollToVerticalOffset(TimelineVerticalScrollViewer.VerticalOffset - delta);
        return true;
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

    private void ScrollTimelineLeftButton_Click(object sender, RoutedEventArgs e)
    {
        Focus();
        ScrollTimelineHorizontal(-ResolveTimelineButtonScrollStep());
    }

    private void ScrollTimelineRightButton_Click(object sender, RoutedEventArgs e)
    {
        Focus();
        ScrollTimelineHorizontal(ResolveTimelineButtonScrollStep());
    }

    private void ZoomOutTimelineButton_Click(object sender, RoutedEventArgs e)
    {
        Focus();
        ExecuteTimelineZoom(increase: false);
    }

    private void ZoomInTimelineButton_Click(object sender, RoutedEventArgs e)
    {
        Focus();
        ExecuteTimelineZoom(increase: true);
    }

    private void ScrollTimelineHorizontal(double delta)
    {
        var maximum = Math.Max(0, TimelineScrollViewer.ExtentWidth - TimelineScrollViewer.ViewportWidth);
        if (maximum <= 0)
            return;

        var nextOffset = Math.Clamp(TimelineScrollViewer.HorizontalOffset + delta, 0, maximum);
        TimelineScrollViewer.ScrollToHorizontalOffset(nextOffset);
    }

    private double ResolveTimelineButtonScrollStep()
    {
        return Math.Max(120, TimelineScrollViewer.ViewportWidth * 0.35);
    }

    private double ResolveTimelineKeyboardScrollStep()
    {
        return Math.Max(80, TimelineScrollViewer.ViewportWidth * 0.22);
    }

    private double ResolveTimelineWheelScrollStep()
    {
        return Math.Max(72, TimelineScrollViewer.ViewportWidth * 0.12);
    }

    private void ExecuteTimelineZoom(bool increase)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var command = increase
            ? viewModel.ZoomInTimelineCommand
            : viewModel.ZoomOutTimelineCommand;

        if (!command.CanExecute(null))
            return;

        var playheadViewportAnchor = ResolvePlayheadViewportAnchor(viewModel);
        command.Execute(null);
        RestorePlayheadViewportAnchor(viewModel, playheadViewportAnchor);
    }

    private bool SelectTimelineTool(bool useRazor)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return false;

        var command = useRazor
            ? viewModel.SelectTimelineRazorToolCommand
            : viewModel.SelectTimelineMoveToolCommand;

        if (!command.CanExecute(null))
            return false;

        command.Execute(null);
        Focus();
        return true;
    }

    private double ResolvePlayheadViewportAnchor(MainWindowViewModel viewModel)
    {
        var viewportWidth = Math.Max(0, TimelineScrollViewer.ViewportWidth);
        if (viewportWidth <= 0)
            return 0;

        var playheadLeft = viewModel.TimelinePlayheadCanvasLeft;
        var viewportLeft = TimelineScrollViewer.HorizontalOffset;
        var viewportRight = viewportLeft + viewportWidth;

        if (playheadLeft >= viewportLeft && playheadLeft <= viewportRight)
            return playheadLeft - viewportLeft;

        return viewportWidth / 2;
    }

    private void RestorePlayheadViewportAnchor(MainWindowViewModel viewModel, double viewportAnchor)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var maximum = Math.Max(0, TimelineScrollViewer.ExtentWidth - TimelineScrollViewer.ViewportWidth);
            var targetOffset = Math.Clamp(viewModel.TimelinePlayheadCanvasLeft - viewportAnchor, 0, maximum);
            TimelineScrollViewer.ScrollToHorizontalOffset(targetOffset);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
