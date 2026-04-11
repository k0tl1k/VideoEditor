using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoEditor.UI.ViewModels;

namespace VideoEditor.UI.Controls;

public partial class ProgramMonitor : UserControl
{
    private readonly DispatcherTimer _clipEndTimer;
    private readonly DispatcherTimer _timelineFrameTimer;
    private DateTime _timelineFrameStartedAtUtc;
    private TimeSpan _timelineFrameDuration;
    private bool _playAudioWhenOpened;
    private bool _playWhenOpened;
    private int _handledPlaybackRequestVersion;

    public ProgramMonitor()
    {
        InitializeComponent();

        _clipEndTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _clipEndTimer.Tick += ClipEndTimer_Tick;

        _timelineFrameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timelineFrameTimer.Tick += TimelineFrameTimer_Tick;

        DataContextChanged += ProgramMonitor_DataContextChanged;
        Unloaded += ProgramMonitor_Unloaded;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void ProgramMonitor_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldNotify)
            oldNotify.PropertyChanged -= ViewModel_PropertyChanged;

        if (e.NewValue is INotifyPropertyChanged newNotify)
            newNotify.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.PreviewMediaSource))
        {
            _playWhenOpened = false;
            _clipEndTimer.Stop();
            _timelineFrameTimer.Stop();
            PreviewMediaElement.Stop();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.PreviewAudioSource))
        {
            _playAudioWhenOpened = false;
            PreviewAudioElement.Stop();
            return;
        }

        if (e.PropertyName != nameof(MainWindowViewModel.PreviewPlaybackRequestVersion))
            return;

        Dispatcher.BeginInvoke(HandlePlaybackRequest, DispatcherPriority.Background);
    }

    private void HandlePlaybackRequest()
    {
        var viewModel = ViewModel;
        if (viewModel is null || _handledPlaybackRequestVersion == viewModel.PreviewPlaybackRequestVersion)
            return;

        _handledPlaybackRequestVersion = viewModel.PreviewPlaybackRequestVersion;

        switch (viewModel.PreviewPlaybackRequest)
        {
            case "Play":
                PlayFromClipStart();
                break;
            case "Pause":
                _clipEndTimer.Stop();
                _timelineFrameTimer.Stop();
                PreviewMediaElement.Pause();
                _playAudioWhenOpened = false;
                PreviewAudioElement.Pause();
                break;
            case "Stop":
                StopAtClipStart();
                break;
            case "Gap":
                PlayTimelineFrame(viewModel.PreviewGapDuration);
                break;
            case "ImageFrame":
                PlayTimelineFrame(viewModel.PreviewSourceDuration);
                break;
        }
    }

    private void PlayFromClipStart()
    {
        var viewModel = ViewModel;
        if (viewModel?.PreviewMediaSource is null)
            return;

        _playWhenOpened = true;
        _timelineFrameTimer.Stop();
        PreviewMediaElement.Position = viewModel.PreviewSourceStart;
        PreviewMediaElement.Play();
        PlayAudioFromCurrentTimelineSegment();
        _clipEndTimer.Start();
    }

    private void StopAtClipStart()
    {
        var viewModel = ViewModel;

        _playWhenOpened = false;
        _playAudioWhenOpened = false;
        _clipEndTimer.Stop();
        _timelineFrameTimer.Stop();
        PreviewMediaElement.Stop();
        PreviewAudioElement.Stop();

        if (viewModel is not null)
            PreviewMediaElement.Position = viewModel.PreviewSourceStart;
    }

    private void PreviewMediaElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        PreviewMediaElement.Position = viewModel.PreviewSourceStart;

        if (!_playWhenOpened)
            return;

        PreviewMediaElement.Play();
        _clipEndTimer.Start();
    }

    private void PreviewMediaElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        FinishPlayback();
    }

    private void PreviewMediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _playWhenOpened = false;
        _playAudioWhenOpened = false;
        _clipEndTimer.Stop();
        PreviewAudioElement.Stop();
        ViewModel?.FailPreviewPlayback();
    }

    private void ClipEndTimer_Tick(object? sender, EventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null || viewModel.PreviewSourceDuration <= TimeSpan.Zero)
            return;

        var clipEnd = viewModel.PreviewSourceStart + viewModel.PreviewSourceDuration;
        viewModel.UpdateTimelinePlaybackProgress(PreviewMediaElement.Position - viewModel.PreviewSourceStart);

        if (PreviewMediaElement.Position >= clipEnd)
            FinishPlayback();
    }

    private void PlayTimelineFrame(TimeSpan duration)
    {
        _playWhenOpened = false;
        _clipEndTimer.Stop();
        PreviewMediaElement.Stop();
        PlayAudioFromCurrentTimelineSegment();

        _timelineFrameTimer.Stop();
        _timelineFrameDuration = duration > TimeSpan.Zero
            ? duration
            : TimeSpan.Zero;
        _timelineFrameStartedAtUtc = DateTime.UtcNow;
        _timelineFrameTimer.Start();
    }

    private void PlayAudioFromCurrentTimelineSegment()
    {
        var viewModel = ViewModel;
        if (viewModel?.PreviewAudioSource is null)
        {
            _playAudioWhenOpened = false;
            PreviewAudioElement.Stop();
            return;
        }

        _playAudioWhenOpened = true;
        PreviewAudioElement.Position = viewModel.PreviewAudioSourceStart;
        PreviewAudioElement.Play();
    }

    private void PreviewAudioElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        PreviewAudioElement.Position = viewModel.PreviewAudioSourceStart;

        if (_playAudioWhenOpened)
            PreviewAudioElement.Play();
    }

    private void PreviewAudioElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _playAudioWhenOpened = false;
        PreviewAudioElement.Stop();
    }

    private void TimelineFrameTimer_Tick(object? sender, EventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        var elapsed = DateTime.UtcNow - _timelineFrameStartedAtUtc;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed < _timelineFrameDuration)
        {
            viewModel.UpdateTimelinePlaybackProgress(elapsed);
            return;
        }

        viewModel.UpdateTimelinePlaybackProgress(_timelineFrameDuration);
        _timelineFrameTimer.Stop();

        if (viewModel.IsTimelineGapPreview)
            viewModel.CompletePreviewPlayback();
        else
            viewModel.CompletePreviewImageFrame();
    }

    private void FinishPlayback()
    {
        StopAtClipStart();
        ViewModel?.CompletePreviewPlayback();
    }

    private void ProgramMonitor_Unloaded(object sender, RoutedEventArgs e)
    {
        _clipEndTimer.Stop();
        _timelineFrameTimer.Stop();
        PreviewMediaElement.Stop();
        _playAudioWhenOpened = false;
        PreviewAudioElement.Stop();

        if (DataContext is INotifyPropertyChanged notify)
            notify.PropertyChanged -= ViewModel_PropertyChanged;
    }
}
