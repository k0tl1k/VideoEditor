using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoEditor.UI.ViewModels;

namespace VideoEditor.UI.Controls;

public partial class ProgramMonitor : UserControl
{
    private readonly DispatcherTimer _clipEndTimer;
    private readonly DispatcherTimer _seekFrameRenderTimer;
    private readonly DispatcherTimer _seekRequestDebounceTimer;
    private readonly DispatcherTimer _seekVisualLayerFrameRenderTimer;
    private readonly DispatcherTimer _timelineFrameTimer;
    private readonly List<MediaElement> _visualLayerMediaElements = new();
    private DateTime _timelineFrameStartedAtUtc;
    private TimeSpan _pendingSeekSourceStart;
    private TimeSpan _timelineFrameDuration;
    private bool _isPreviewPlaying;
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

        _seekRequestDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _seekRequestDebounceTimer.Tick += SeekRequestDebounceTimer_Tick;

        _seekFrameRenderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _seekFrameRenderTimer.Tick += SeekFrameRenderTimer_Tick;

        _seekVisualLayerFrameRenderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _seekVisualLayerFrameRenderTimer.Tick += SeekVisualLayerFrameRenderTimer_Tick;

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

        if (e.PropertyName == nameof(MainWindowViewModel.IsPreviewVideoAudioMuted))
        {
            ApplyVideoMuteState();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.PreviewAudioVolume))
        {
            ApplyAudioVolume();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.PreviewVideoVolume))
        {
            ApplyVideoVolume();
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
                _isPreviewPlaying = false;
                _clipEndTimer.Stop();
                _timelineFrameTimer.Stop();
                PreviewMediaElement.Pause();
                PauseVisualLayerVideos();
                _playAudioWhenOpened = false;
                PreviewAudioElement.Pause();
                break;
            case "Stop":
                StopAtClipStart();
                break;
            case "Seek":
                SeekToPreviewFrame();
                break;
            case "Gap":
                PlayTimelineFrame(viewModel.PreviewGapDuration);
                break;
            case "ImageFrame":
                PlayTimelineFrame(viewModel.PreviewSourceDuration);
                break;
            case "SyncAudio":
                ApplyVideoMuteState();
                if (_isPreviewPlaying)
                    PlayAudioFromCurrentTimelineSegment();
                break;
            case "SyncPlayback":
                SyncPlayingPreviewPosition();
                break;
            case "ContinuePlayback":
                ContinuePlayingPreviewPosition();
                break;
        }
    }

    private void SeekToPreviewFrame()
    {
        var viewModel = ViewModel;

        _playWhenOpened = false;
        _playAudioWhenOpened = false;
        _isPreviewPlaying = false;
        _clipEndTimer.Stop();
        _seekFrameRenderTimer.Stop();
        _seekRequestDebounceTimer.Stop();
        _seekVisualLayerFrameRenderTimer.Stop();
        _timelineFrameTimer.Stop();
        PreviewAudioElement.Stop();

        if (viewModel?.PreviewPlayableMediaSource is null)
        {
            PreviewMediaElement.Stop();
            StopVisualLayerVideos();
            return;
        }

        _pendingSeekSourceStart = viewModel.PreviewSourceStart;
        PreviewMediaElement.IsMuted = true;
        PauseVisualLayerVideos(mute: true);
        _seekRequestDebounceTimer.Start();
    }

    private void PlayFromClipStart()
    {
        var viewModel = ViewModel;
        if (viewModel?.PreviewMediaSource is null)
            return;

        _isPreviewPlaying = true;
        _playWhenOpened = true;
        _seekRequestDebounceTimer.Stop();
        _seekFrameRenderTimer.Stop();
        _seekVisualLayerFrameRenderTimer.Stop();
        PreviewMediaElement.IsMuted = viewModel.IsPreviewVideoAudioMuted;
        PreviewMediaElement.Volume = viewModel.PreviewVideoVolume;
        _timelineFrameTimer.Stop();
        PreviewMediaElement.Position = viewModel.PreviewSourceStart;
        PreviewMediaElement.Play();
        PlayVisualLayerVideos();
        PlayAudioFromCurrentTimelineSegment();
        _clipEndTimer.Start();
    }

    private void StopAtClipStart()
    {
        var viewModel = ViewModel;

        _playWhenOpened = false;
        _playAudioWhenOpened = false;
        _isPreviewPlaying = false;
        _clipEndTimer.Stop();
        _seekFrameRenderTimer.Stop();
        _seekRequestDebounceTimer.Stop();
        _seekVisualLayerFrameRenderTimer.Stop();
        _timelineFrameTimer.Stop();
        PreviewMediaElement.Stop();
        ApplyVideoMuteState();
        ApplyVideoVolume();
        StopVisualLayerVideos();
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
        {
            RenderSeekFrame(viewModel.PreviewSourceStart);
            return;
        }

        _seekFrameRenderTimer.Stop();
        PreviewMediaElement.IsMuted = viewModel.IsPreviewVideoAudioMuted;
        PreviewMediaElement.Volume = viewModel.PreviewVideoVolume;
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
        _isPreviewPlaying = true;
        _playWhenOpened = false;
        _clipEndTimer.Stop();
        _seekFrameRenderTimer.Stop();
        _seekRequestDebounceTimer.Stop();
        _seekVisualLayerFrameRenderTimer.Stop();
        PreviewMediaElement.Stop();
        ApplyVideoMuteState();
        PlayVisualLayerVideos();
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
        PreviewAudioElement.Volume = viewModel.PreviewAudioVolume;
        PreviewAudioElement.Position = viewModel.PreviewAudioSourceStart;
        PreviewAudioElement.Play();
    }

    private void SyncPlayingPreviewPosition()
    {
        var viewModel = ViewModel;
        if (viewModel?.PreviewMediaSource is null)
            return;

        _isPreviewPlaying = true;
        _playWhenOpened = true;
        _seekRequestDebounceTimer.Stop();
        _seekFrameRenderTimer.Stop();
        _seekVisualLayerFrameRenderTimer.Stop();
        _timelineFrameTimer.Stop();
        ApplyVideoMuteState();
        ApplyVideoVolume();
        PreviewMediaElement.Position = viewModel.PreviewSourceStart;
        PreviewMediaElement.Play();
        PlayVisualLayerVideos();
        PlayAudioFromCurrentTimelineSegment();
        _clipEndTimer.Start();
    }

    private void ContinuePlayingPreviewPosition()
    {
        var viewModel = ViewModel;
        if (viewModel?.PreviewMediaSource is null)
            return;

        _isPreviewPlaying = true;
        _playWhenOpened = true;
        _seekRequestDebounceTimer.Stop();
        _seekFrameRenderTimer.Stop();
        _seekVisualLayerFrameRenderTimer.Stop();
        _timelineFrameTimer.Stop();
        ApplyVideoMuteState();
        ApplyVideoVolume();
        PlayVisualLayerVideos();
        PlayAudioFromCurrentTimelineSegment();
        _clipEndTimer.Start();
    }

    private void PreviewAudioElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        PreviewAudioElement.Position = viewModel.PreviewAudioSourceStart;
        PreviewAudioElement.Volume = viewModel.PreviewAudioVolume;

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
        var viewModel = ViewModel;
        if (viewModel is null)
            return;

        if (viewModel.TryContinueTimelinePlayback())
            return;

        viewModel.CompletePreviewPlayback();
    }

    private void RenderSeekFrame(TimeSpan sourceStart)
    {
        _seekFrameRenderTimer.Stop();
        PreviewMediaElement.Position = sourceStart;
        PreviewMediaElement.IsMuted = true;
        PreviewMediaElement.Play();
        _seekFrameRenderTimer.Start();
    }

    private void SeekRequestDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _seekRequestDebounceTimer.Stop();

        if (_isPreviewPlaying || _playWhenOpened)
            return;

        RenderSeekFrame(_pendingSeekSourceStart);
        SeekVisualLayerVideos();
    }

    private void SeekFrameRenderTimer_Tick(object? sender, EventArgs e)
    {
        _seekFrameRenderTimer.Stop();
        ApplyVideoMuteState();

        if (_isPreviewPlaying || _playWhenOpened)
            return;

        PreviewMediaElement.Pause();
    }

    private void SeekVisualLayerFrameRenderTimer_Tick(object? sender, EventArgs e)
    {
        _seekVisualLayerFrameRenderTimer.Stop();

        if (_isPreviewPlaying)
            return;

        foreach (var mediaElement in _visualLayerMediaElements.ToList())
        {
            mediaElement.IsMuted = ViewModel?.IsPreviewVideoAudioMuted ?? false;
            mediaElement.Pause();
        }
    }

    private void ApplyVideoMuteState()
    {
        var isMuted = ViewModel?.IsPreviewVideoAudioMuted ?? false;
        PreviewMediaElement.IsMuted = isMuted;

        foreach (var mediaElement in _visualLayerMediaElements.ToList())
            mediaElement.IsMuted = isMuted;
    }

    private void ApplyVideoVolume()
    {
        var volume = ViewModel?.PreviewVideoVolume ?? 1.0;
        PreviewMediaElement.Volume = volume;

        foreach (var mediaElement in _visualLayerMediaElements.ToList())
            mediaElement.Volume = volume;
    }

    private void ApplyAudioVolume()
    {
        PreviewAudioElement.Volume = ViewModel?.PreviewAudioVolume ?? 1.0;
    }

    private void PreviewVisualLayerMediaElement_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement mediaElement)
            return;

        if (mediaElement.DataContext is not PreviewVisualLayerViewModel layer || !layer.IsVideo)
            return;

        if (!_visualLayerMediaElements.Contains(mediaElement))
            _visualLayerMediaElements.Add(mediaElement);

        if (_isPreviewPlaying)
        {
            PlayVisualLayerVideo(mediaElement);
            return;
        }

        SeekVisualLayerVideo(mediaElement);
    }

    private void PreviewVisualLayerMediaElement_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement mediaElement)
            return;

        mediaElement.Stop();
        _visualLayerMediaElements.Remove(mediaElement);
    }

    private void PreviewVisualLayerMediaElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement mediaElement)
            return;

        if (mediaElement.DataContext is not PreviewVisualLayerViewModel layer || !layer.IsVideo)
            return;

        if (_isPreviewPlaying)
        {
            PlayVisualLayerVideo(mediaElement);
            return;
        }

        SeekVisualLayerVideo(mediaElement);
    }

    private void PreviewVisualLayerMediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not MediaElement mediaElement)
            return;

        mediaElement.Stop();
        _visualLayerMediaElements.Remove(mediaElement);
    }

    private void PlayVisualLayerVideos()
    {
        foreach (var mediaElement in _visualLayerMediaElements.ToList())
            PlayVisualLayerVideo(mediaElement);
    }

    private void PlayVisualLayerVideo(MediaElement mediaElement)
    {
        if (mediaElement.DataContext is not PreviewVisualLayerViewModel layer || !layer.IsVideo)
            return;

        mediaElement.IsMuted = ViewModel?.IsPreviewVideoAudioMuted ?? false;
        mediaElement.Volume = ViewModel?.PreviewVideoVolume ?? 1.0;
        mediaElement.Position = layer.SourceStart;
        mediaElement.Play();
    }

    private void PauseVisualLayerVideos(bool mute = false)
    {
        foreach (var mediaElement in _visualLayerMediaElements.ToList())
        {
            if (mute)
                mediaElement.IsMuted = true;

            mediaElement.Pause();
        }
    }

    private void StopVisualLayerVideos()
    {
        _seekVisualLayerFrameRenderTimer.Stop();

        foreach (var mediaElement in _visualLayerMediaElements.ToList())
        {
            mediaElement.IsMuted = false;
            mediaElement.Stop();
        }
    }

    private void SeekVisualLayerVideos()
    {
        foreach (var mediaElement in _visualLayerMediaElements.ToList())
            SeekVisualLayerVideo(mediaElement);
    }

    private void SeekVisualLayerVideo(MediaElement mediaElement)
    {
        if (mediaElement.DataContext is not PreviewVisualLayerViewModel layer || !layer.IsVideo)
            return;

        _seekVisualLayerFrameRenderTimer.Stop();
        mediaElement.Position = layer.SourceStart;
        mediaElement.IsMuted = true;
        mediaElement.Play();
        _seekVisualLayerFrameRenderTimer.Start();
    }

    private void ProgramMonitor_Unloaded(object sender, RoutedEventArgs e)
    {
        _isPreviewPlaying = false;
        _clipEndTimer.Stop();
        _seekFrameRenderTimer.Stop();
        _seekRequestDebounceTimer.Stop();
        _seekVisualLayerFrameRenderTimer.Stop();
        _timelineFrameTimer.Stop();
        PreviewMediaElement.Stop();
        PreviewMediaElement.IsMuted = false;
        StopVisualLayerVideos();
        _visualLayerMediaElements.Clear();
        _playAudioWhenOpened = false;
        PreviewAudioElement.Stop();

        if (DataContext is INotifyPropertyChanged notify)
            notify.PropertyChanged -= ViewModel_PropertyChanged;
    }
}
