using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int TimelineVisibleSeconds = 90;
    private const double TimelinePlayheadLeft = 160;
    private const int TimelineFrameRate = 30;
    private static readonly TimeSpan DefaultVideoClipDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DefaultAudioClipDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DefaultImageClipDuration = TimeSpan.FromSeconds(5);
    private static readonly double[] TimelineZoomLevels = [32, 64, 128, 240, 480, 960];

    private readonly IMediaImportService _mediaImportService;
    private readonly IMediaThumbnailService _mediaThumbnailService;
    private readonly RelayCommand _insertSelectedMediaToTimelineCommand;
    private readonly RelayCommand _playPreviewCommand;
    private readonly RelayCommand _pausePreviewCommand;
    private readonly RelayCommand _stopPreviewCommand;
    private readonly RelayCommand _zoomInTimelineCommand;
    private readonly RelayCommand _zoomOutTimelineCommand;

    private ImportedMediaItemViewModel? _selectedImportedMedia;
    private Domain.Enums.MediaType? _previewMediaType;
    private Guid? _previewClipId;
    private bool _isTimelineGapPreview;
    private bool _isTimelinePlaybackActive;
    private TimeSpan _previewGapDuration;
    private Uri? _previewAudioSource;
    private TimeSpan _previewAudioSourceDuration;
    private TimeSpan _previewAudioSourceStart;
    private Uri? _previewMediaSource;
    private TimeSpan _previewSourceDuration;
    private TimeSpan _previewSourceStart;
    private TimeSpan _previewTimelinePosition;
    private TimeSpan _timelinePlaybackPosition;
    private string _previewPlaybackRequest = "Stop";
    private int _previewPlaybackRequestVersion;
    private string _previewStatusText = "Add a clip to the timeline, then select it for preview.";
    private string _previewTitle = "No clip selected";
    private int _timelineZoomIndex;

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
    /// 	Выбранный импортированный ассет.
    /// </summary>
    public ImportedMediaItemViewModel? SelectedImportedMedia
    {
        get => _selectedImportedMedia;
        set
        {
            if (_selectedImportedMedia == value)
                return;

            _selectedImportedMedia = value;
            _insertSelectedMediaToTimelineCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 	Текущий индекс масштаба таймлайна.
    /// </summary>
    public int TimelineZoomIndex
    {
        get => _timelineZoomIndex;
        set
        {
            var clampedValue = Math.Clamp(value, 0, TimelineZoomLevels.Length - 1);
            if (_timelineZoomIndex == clampedValue)
                return;

            _timelineZoomIndex = clampedValue;
            RefreshTimelineVisualScale();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 	Видеодорожки таймлайна для отображения в интерфейсе.
    /// </summary>
    public ObservableCollection<TimelineTrackItemViewModel> VideoTimelineTracks { get; } = new();

    /// <summary>
    /// 	Аудиодорожки таймлайна для отображения в интерфейсе.
    /// </summary>
    public ObservableCollection<TimelineTrackItemViewModel> AudioTimelineTracks { get; } = new();

    /// <summary>
    /// 	Отметки линейки времени таймлайна.
    /// </summary>
    public ObservableCollection<TimelineRulerMarkItemViewModel> TimelineRulerMarks { get; } = new();

    /// <summary>
    /// 	Команда импорта медиафайлов.
    /// </summary>
    public ICommand ImportMediaCommand { get; }

    /// <summary>
    /// 	Команда вставки выбранного медиафайла на таймлайн.
    /// </summary>
    public ICommand InsertSelectedMediaToTimelineCommand { get; }

    /// <summary>
    /// 	Команда запуска предпросмотра выбранного клипа таймлайна.
    /// </summary>
    public ICommand PlayPreviewCommand { get; }

    /// <summary>
    /// 	Команда паузы предпросмотра.
    /// </summary>
    public ICommand PausePreviewCommand { get; }

    /// <summary>
    /// 	Команда остановки предпросмотра.
    /// </summary>
    public ICommand StopPreviewCommand { get; }

    /// <summary>
    /// 	Команда добавления видеодорожки.
    /// </summary>
    public ICommand AddVideoTrackCommand { get; }

    /// <summary>
    /// 	Команда добавления аудиодорожки.
    /// </summary>
    public ICommand AddAudioTrackCommand { get; }

    /// <summary>
    /// 	Команда увеличения масштаба таймлайна.
    /// </summary>
    public ICommand ZoomInTimelineCommand { get; }

    /// <summary>
    /// 	Команда уменьшения масштаба таймлайна.
    /// </summary>
    public ICommand ZoomOutTimelineCommand { get; }

    /// <summary>
    /// 	Ширина холста таймлайна.
    /// </summary>
    public double TimelineCanvasWidth => TimelineVisibleSeconds * CurrentTimelinePixelsPerSecond + TimelinePlayheadLeft;

    public TimeSpan TimelinePlaybackPosition
    {
        get => _timelinePlaybackPosition;
        private set
        {
            if (_timelinePlaybackPosition == value)
                return;

            _timelinePlaybackPosition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimelinePlayheadCanvasLeft));
            OnPropertyChanged(nameof(TimelinePlaybackPositionLabel));
        }
    }

    public double TimelinePlayheadCanvasLeft => TimelinePlaybackPosition.TotalSeconds * CurrentTimelinePixelsPerSecond;

    public string TimelinePlaybackPositionLabel => FormatTimecodeLabel(
        (int)Math.Floor(TimelinePlaybackPosition.TotalSeconds),
        TimelinePlaybackPosition.Milliseconds * TimelineFrameRate / 1000);

    /// <summary>
    /// 	Текущее текстовое описание масштаба таймлайна.
    /// </summary>
    public string TimelineScaleLabel => CurrentTimelinePixelsPerSecond >= 480 ? "Frames" : "Seconds";

    /// <summary>
    /// 	Максимальный индекс масштаба таймлайна.
    /// </summary>
    public int TimelineZoomMaxIndex => TimelineZoomLevels.Length - 1;

    public bool IsTimelineGapPreview
    {
        get => _isTimelineGapPreview;
        private set
        {
            if (_isTimelineGapPreview == value)
                return;

            _isTimelineGapPreview = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPreviewPlaceholderVisible));
        }
    }

    public TimeSpan PreviewGapDuration
    {
        get => _previewGapDuration;
        private set
        {
            if (_previewGapDuration == value)
                return;

            _previewGapDuration = value;
            OnPropertyChanged();
        }
    }

    public Uri? PreviewAudioSource
    {
        get => _previewAudioSource;
        private set
        {
            if (Equals(_previewAudioSource, value))
                return;

            _previewAudioSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreviewAudio));
        }
    }

    public TimeSpan PreviewAudioSourceStart
    {
        get => _previewAudioSourceStart;
        private set
        {
            if (_previewAudioSourceStart == value)
                return;

            _previewAudioSourceStart = value;
            OnPropertyChanged();
        }
    }

    public TimeSpan PreviewAudioSourceDuration
    {
        get => _previewAudioSourceDuration;
        private set
        {
            if (_previewAudioSourceDuration == value)
                return;

            _previewAudioSourceDuration = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 	Источник медиафайла для монитора предпросмотра.
    /// </summary>
    public Uri? PreviewMediaSource
    {
        get => _previewMediaSource;
        private set
        {
            if (Equals(_previewMediaSource, value))
                return;

            _previewMediaSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreviewMedia));
            OnPropertyChanged(nameof(IsPreviewImage));
            OnPropertyChanged(nameof(IsPreviewPlayableMedia));
            OnPropertyChanged(nameof(IsPreviewPlaceholderVisible));
            OnPropertyChanged(nameof(PreviewImageSource));
            OnPropertyChanged(nameof(PreviewPlayableMediaSource));
        }
    }

    /// <summary>
    /// 	Начало фрагмента внутри исходного файла.
    /// </summary>
    public TimeSpan PreviewSourceStart
    {
        get => _previewSourceStart;
        private set
        {
            if (_previewSourceStart == value)
                return;

            _previewSourceStart = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 	Длительность фрагмента для предпросмотра.
    /// </summary>
    public TimeSpan PreviewSourceDuration
    {
        get => _previewSourceDuration;
        private set
        {
            if (_previewSourceDuration == value)
                return;

            _previewSourceDuration = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 	Последнее действие, которое должен выполнить монитор предпросмотра.
    /// </summary>
    public string PreviewPlaybackRequest
    {
        get => _previewPlaybackRequest;
        private set
        {
            if (_previewPlaybackRequest == value)
                return;

            _previewPlaybackRequest = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 	Версия команды предпросмотра, чтобы повторные Play/Stop не терялись в биндинге.
    /// </summary>
    public int PreviewPlaybackRequestVersion
    {
        get => _previewPlaybackRequestVersion;
        private set
        {
            if (_previewPlaybackRequestVersion == value)
                return;

            _previewPlaybackRequestVersion = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 	Заголовок текущего клипа в мониторе.
    /// </summary>
    public string PreviewTitle
    {
        get => _previewTitle;
        private set
        {
            if (_previewTitle == value)
                return;

            _previewTitle = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 	Текст состояния монитора предпросмотра.
    /// </summary>
    public string PreviewStatusText
    {
        get => _previewStatusText;
        private set
        {
            if (_previewStatusText == value)
                return;

            _previewStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool HasPreviewMedia => PreviewMediaSource is not null;

    public bool HasPreviewAudio => PreviewAudioSource is not null;

    public bool IsPreviewImage => HasPreviewMedia && _previewMediaType == Domain.Enums.MediaType.Image;

    public bool IsPreviewPlayableMedia => HasPreviewMedia && _previewMediaType != Domain.Enums.MediaType.Image;

    public bool IsPreviewPlaceholderVisible => !HasPreviewMedia && !IsTimelineGapPreview;

    public Uri? PreviewImageSource => IsPreviewImage ? PreviewMediaSource : null;

    public Uri? PreviewPlayableMediaSource => IsPreviewPlayableMedia ? PreviewMediaSource : null;

    public event PropertyChangedEventHandler? PropertyChanged;

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
        _insertSelectedMediaToTimelineCommand = new RelayCommand(InsertSelectedMediaToTimeline, CanInsertSelectedMediaToTimeline);
        _playPreviewCommand = new RelayCommand(PlayPreview, CanPlayPreview);
        _pausePreviewCommand = new RelayCommand(PausePreview, CanControlPreview);
        _stopPreviewCommand = new RelayCommand(StopPreview, CanControlPreview);
        _zoomInTimelineCommand = new RelayCommand(ZoomInTimeline, CanZoomInTimeline);
        _zoomOutTimelineCommand = new RelayCommand(ZoomOutTimeline, CanZoomOutTimeline);

        ImportMediaCommand = new RelayCommand(ImportMedia);
        InsertSelectedMediaToTimelineCommand = _insertSelectedMediaToTimelineCommand;
        PlayPreviewCommand = _playPreviewCommand;
        PausePreviewCommand = _pausePreviewCommand;
        StopPreviewCommand = _stopPreviewCommand;
        AddVideoTrackCommand = new RelayCommand(AddVideoTrack);
        AddAudioTrackCommand = new RelayCommand(AddAudioTrack);
        ZoomInTimelineCommand = _zoomInTimelineCommand;
        ZoomOutTimelineCommand = _zoomOutTimelineCommand;

        RefreshTimelinePresentation();
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

    private bool CanInsertSelectedMediaToTimeline()
    {
        return SelectedImportedMedia is not null;
    }

    private void InsertSelectedMediaToTimeline()
    {
        var asset = SelectedImportedMedia?.Asset;
        if (asset is null)
            return;

        if (asset.Type == Domain.Enums.MediaType.Audio)
        {
            var audioTrack = EnsureTrack("Audio");
            var clip = AddClipToTrack(audioTrack, asset, ResolveAppendStart(audioTrack), ResolveClipDuration(asset));
            RefreshTimelinePresentation();
            SelectClipForPreviewIfNone(clip.Id);
            return;
        }

        if (asset.Type == Domain.Enums.MediaType.Image)
        {
            var videoTrack = EnsureTrack("Video");
            var clip = AddClipToTrack(videoTrack, asset, ResolveAppendStart(videoTrack), ResolveClipDuration(asset));
            RefreshTimelinePresentation();
            SelectClipForPreviewIfNone(clip.Id);
            return;
        }

        var videoClip = InsertVideoWithAudio(asset);
        RefreshTimelinePresentation();
        SelectClipForPreviewIfNone(videoClip.Id);
    }

    private TimelineClip InsertVideoWithAudio(MediaAsset asset)
    {
        var videoTrack = EnsureTrack("Video");
        var audioTrack = EnsureTrack("Audio");
        var duration = ResolveClipDuration(asset);
        var timelineStart = ResolveAppendStart(videoTrack);
        var linkedGroupId = Guid.NewGuid();

        var videoClip = AddClipToTrack(videoTrack, asset, timelineStart, duration, linkedGroupId);
        AddClipToTrack(audioTrack, asset, timelineStart, duration, linkedGroupId);
        return videoClip;
    }

    private TimelineClip AddClipToTrack(
        TimelineTrack track,
        MediaAsset asset,
        TimeSpan timelineStart,
        TimeSpan duration,
        Guid? linkedGroupId = null)
    {
        var clip = new TimelineClip
        {
            MediaAssetId = asset.Id,
            LinkedGroupId = linkedGroupId,
            SourceStart = TimeSpan.Zero,
            SourceDuration = duration,
            TimelineStart = timelineStart
        };

        track.Clips.Add(clip);
        return clip;
    }

    private static TimeSpan ResolveAppendStart(TimelineTrack track)
    {
        return track.Clips.Count == 0
            ? TimeSpan.Zero
            : track.Clips.Max(x => x.TimelineEnd);
    }

    private TimelineTrack EnsureTrack(string kind)
    {
        var existingTrack = CurrentProject.Tracks.FirstOrDefault(x => IsTrackOfKind(x, kind));
        if (existingTrack is not null)
            return existingTrack;

        var newTrack = new TimelineTrack { Name = BuildTrackName(kind, GetNextTrackIndex(kind)) };
        CurrentProject.Tracks.Add(newTrack);
        return newTrack;
    }

    private void AddVideoTrack()
    {
        CurrentProject.Tracks.Add(new TimelineTrack
        {
            Name = BuildTrackName("Video", GetNextTrackIndex("Video"))
        });

        RefreshTimelinePresentation();
    }

    private void AddAudioTrack()
    {
        CurrentProject.Tracks.Add(new TimelineTrack
        {
            Name = BuildTrackName("Audio", GetNextTrackIndex("Audio"))
        });

        RefreshTimelinePresentation();
    }

    private int GetNextTrackIndex(string kind)
    {
        return CurrentProject.Tracks.Count(x => IsTrackOfKind(x, kind)) + 1;
    }

    private bool CanZoomInTimeline()
    {
        return _timelineZoomIndex < TimelineZoomLevels.Length - 1;
    }

    private bool CanZoomOutTimeline()
    {
        return _timelineZoomIndex > 0;
    }

    private void ZoomInTimeline()
    {
        if (!CanZoomInTimeline())
            return;

        TimelineZoomIndex++;
    }

    private void ZoomOutTimeline()
    {
        if (!CanZoomOutTimeline())
            return;

        TimelineZoomIndex--;
    }

    private void BuildTimelineRuler()
    {
        TimelineRulerMarks.Clear();

        if (CurrentTimelinePixelsPerSecond >= 480)
        {
            var totalFrames = TimelineVisibleSeconds * TimelineFrameRate;
            for (var frame = 0; frame <= totalFrames; frame++)
            {
                var isSecondMark = frame % TimelineFrameRate == 0;
                var isMajorFrame = frame % 5 == 0;

                TimelineRulerMarks.Add(new TimelineRulerMarkItemViewModel
                {
                    Left = frame * (CurrentTimelinePixelsPerSecond / TimelineFrameRate),
                    Label = isSecondMark ? FormatTimecodeLabel(frame / TimelineFrameRate, 0) : string.Empty,
                    TickHeight = isSecondMark ? 18 : isMajorFrame ? 11 : 7,
                    TickOpacity = isSecondMark ? 1 : isMajorFrame ? 0.7 : 0.35,
                    LabelOpacity = isSecondMark ? 1 : 0
                });
            }

            return;
        }

        var labelStep = ResolveTimelineLabelStep();

        for (var second = 0; second <= TimelineVisibleSeconds; second++)
        {
            var isMajor = second % 5 == 0;
            var hasLabel = second % labelStep == 0;
            TimelineRulerMarks.Add(new TimelineRulerMarkItemViewModel
            {
                Left = second * CurrentTimelinePixelsPerSecond,
                Label = hasLabel ? FormatTimecodeLabel(second) : string.Empty,
                TickHeight = isMajor ? 18 : 10,
                TickOpacity = isMajor ? 1 : 0.55,
                LabelOpacity = hasLabel ? 1 : 0
            });
        }
    }

    private void RefreshTimelineTracks()
    {
        VideoTimelineTracks.Clear();
        AudioTimelineTracks.Clear();

        foreach (var track in CurrentProject.Tracks)
        {
            var trackItem = new TimelineTrackItemViewModel
            {
                TrackName = track.Name,
                Header = BuildTrackHeader(track.Name),
                Title = track.Name
            };

            foreach (var clip in track.Clips.OrderBy(x => x.TimelineStart))
            {
                var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
                if (asset is null)
                    continue;

                trackItem.Clips.Add(new TimelineClipItemViewModel
                {
                    ClipId = clip.Id,
                    MediaAssetId = asset.Id,
                    TrackName = track.Name,
                    DisplayName = BuildClipDisplayName(track.Name, asset),
                    BackgroundColor = ResolveClipColor(track.Name, asset.Type),
                    AccentColor = ResolveClipAccentColor(track.Name, asset.Type),
                    Left = clip.TimelineStart.TotalSeconds * CurrentTimelinePixelsPerSecond,
                    Width = Math.Max(96, clip.SourceDuration.TotalSeconds * CurrentTimelinePixelsPerSecond)
                });
            }

            if (IsTrackOfKind(track, "Video"))
                VideoTimelineTracks.Add(trackItem);
            else
                AudioTimelineTracks.Add(trackItem);
        }

        OnPropertyChanged(nameof(TimelineCanvasWidth));
    }

    private static TimeSpan ResolveClipDuration(MediaAsset asset)
    {
        if (asset.Duration > TimeSpan.Zero)
            return asset.Duration;

        return asset.Type switch
        {
            Domain.Enums.MediaType.Image => DefaultImageClipDuration,
            Domain.Enums.MediaType.Audio => DefaultAudioClipDuration,
            _ => DefaultVideoClipDuration
        };
    }

    private static bool IsTrackOfKind(TimelineTrack track, string kind)
    {
        return track.Name.StartsWith(kind, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTrackName(string kind, int index)
    {
        return $"{kind} Track {index}";
    }

    private static string BuildTrackHeader(string trackName)
    {
        var index = ExtractTrackIndex(trackName);

        if (trackName.StartsWith("Video", StringComparison.OrdinalIgnoreCase))
            return $"V{index}";

        if (trackName.StartsWith("Audio", StringComparison.OrdinalIgnoreCase))
            return $"A{index}";

        return trackName;
    }

    private static int ExtractTrackIndex(string trackName)
    {
        var lastPart = trackName.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return int.TryParse(lastPart, out var index) ? index : 1;
    }

    private static string BuildClipDisplayName(string trackName, MediaAsset asset)
    {
        if (trackName.StartsWith("Audio", StringComparison.OrdinalIgnoreCase) &&
            asset.Type == Domain.Enums.MediaType.Video)
            return $"{asset.DisplayName} [Audio]";

        return asset.DisplayName;
    }

    private static string ResolveClipColor(string trackName, Domain.Enums.MediaType mediaType)
    {
        if (trackName.StartsWith("Audio", StringComparison.OrdinalIgnoreCase))
            return "#235246";

        return mediaType switch
        {
            Domain.Enums.MediaType.Image => "#5B4625",
            _ => "#32415A"
        };
    }

    private static string ResolveClipAccentColor(string trackName, Domain.Enums.MediaType mediaType)
    {
        if (trackName.StartsWith("Audio", StringComparison.OrdinalIgnoreCase))
            return "#4AD6A8";

        return mediaType switch
        {
            Domain.Enums.MediaType.Image => "#F0B35A",
            _ => "#58A6FF"
        };
    }

    private int ResolveTimelineLabelStep()
    {
        if (CurrentTimelinePixelsPerSecond >= 240)
            return 1;

        if (CurrentTimelinePixelsPerSecond >= 96)
            return 2;

        return 5;
    }

    private static string FormatTimecodeLabel(int second, int frame = 0)
    {
        var time = TimeSpan.FromSeconds(second);
        return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}:{frame:D2}";
    }

    public void MoveClip(Guid clipId, string targetTrackName, double left)
    {
        var sourceTrack = CurrentProject.Tracks.FirstOrDefault(x => x.Clips.Any(c => c.Id == clipId));
        if (sourceTrack is null)
            return;

        var clip = sourceTrack.Clips.FirstOrDefault(x => x.Id == clipId);
        if (clip is null)
            return;

        var targetTrack = CurrentProject.Tracks.FirstOrDefault(x =>
            x.Name.Equals(targetTrackName, StringComparison.OrdinalIgnoreCase));
        if (targetTrack is null)
            return;

        if (!AreTracksSameKind(sourceTrack, targetTrack))
            return;

        var oldStart = clip.TimelineStart;
        var newStart = TimeSpan.FromSeconds(Math.Max(0, left / CurrentTimelinePixelsPerSecond));

        if (!ReferenceEquals(sourceTrack, targetTrack))
        {
            sourceTrack.Clips.Remove(clip);
            targetTrack.Clips.Add(clip);
        }

        clip.TimelineStart = newStart;
        MoveLinkedClip(clip, newStart);
        RefreshTimelinePresentation();
    }

    public void DeleteClip(Guid clipId, bool deleteLinkedClips = true)
    {
        var clipInfo = FindClipWithTrack(clipId);
        if (clipInfo is null)
            return;

        var (track, clip) = clipInfo.Value;
        var linkedClipIds = deleteLinkedClips
            ? FindLinkedClipIds(clip).ToList()
            : new List<Guid>();

        track.Clips.Remove(clip);

        foreach (var linkedClipId in linkedClipIds)
        {
            var linkedInfo = FindClipWithTrack(linkedClipId);
            if (linkedInfo is null)
                continue;

            linkedInfo.Value.Track.Clips.Remove(linkedInfo.Value.Clip);
        }

        if (_previewClipId == clipId || linkedClipIds.Contains(_previewClipId.GetValueOrDefault()))
            ClearPreviewAfterDeletedClip();

        RefreshTimelinePresentation();
    }

    public void SetTimelinePlaybackPositionFromCanvasLeft(double canvasLeft)
    {
        var timelinePosition = TimeSpan.FromSeconds(Math.Max(0, canvasLeft / CurrentTimelinePixelsPerSecond));
        SetTimelinePlaybackPosition(timelinePosition);
    }

    private void SetTimelinePlaybackPosition(TimeSpan timelinePosition)
    {
        _isTimelinePlaybackActive = false;
        _previewTimelinePosition = timelinePosition;
        IsTimelineGapPreview = false;
        PreviewGapDuration = TimeSpan.Zero;
        TimelinePlaybackPosition = timelinePosition;
        PreviewStatusText = $"Timeline position set to {TimelinePlaybackPositionLabel}.";
        RequestPreviewPlayback("Stop");
    }

    public void SelectClipForPreview(Guid clipId)
    {
        var clip = CurrentProject.Tracks
            .SelectMany(x => x.Clips)
            .FirstOrDefault(x => x.Id == clipId);

        if (clip is null)
            return;

        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
            return;

        _previewClipId = clip.Id;
        IsTimelineGapPreview = false;
        _previewMediaType = asset.Type;
        PreviewSourceStart = clip.SourceStart;
        PreviewSourceDuration = clip.SourceDuration;
        _previewTimelinePosition = clip.TimelineStart;
        TimelinePlaybackPosition = clip.TimelineStart;
        PreviewTitle = asset.DisplayName;
        PreviewStatusText = asset.Type == Domain.Enums.MediaType.Image
            ? "Image clip selected."
            : "Clip selected. Press Play to preview.";
        PreviewMediaSource = new Uri(asset.FilePath, UriKind.Absolute);

        OnPropertyChanged(nameof(IsPreviewImage));
        OnPropertyChanged(nameof(IsPreviewPlayableMedia));
        OnPropertyChanged(nameof(IsPreviewPlaceholderVisible));
        OnPropertyChanged(nameof(PreviewImageSource));
        OnPropertyChanged(nameof(PreviewPlayableMediaSource));
        _playPreviewCommand.RaiseCanExecuteChanged();
        _pausePreviewCommand.RaiseCanExecuteChanged();
        _stopPreviewCommand.RaiseCanExecuteChanged();
    }

    private void SelectClipForPreviewIfNone(Guid clipId)
    {
        if (HasPreviewMedia)
            return;

        SelectClipForPreview(clipId);
    }

    public void CompletePreviewPlayback()
    {
        if (!_isTimelinePlaybackActive)
        {
            if (HasPreviewMedia)
                PreviewStatusText = "Preview finished.";

            return;
        }

        _previewTimelinePosition = IsTimelineGapPreview
            ? _previewTimelinePosition + PreviewGapDuration
            : _previewTimelinePosition + PreviewSourceDuration;

        PlayTimelineFromPosition(_previewTimelinePosition);
    }

    public void UpdateTimelinePlaybackProgress(TimeSpan elapsedInCurrentSegment)
    {
        if (!_isTimelinePlaybackActive)
            return;

        var position = _previewTimelinePosition + elapsedInCurrentSegment;
        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        TimelinePlaybackPosition = position;
    }

    public void CompletePreviewImageFrame()
    {
        if (!_isTimelinePlaybackActive)
            return;

        CompletePreviewPlayback();
    }

    public void FailPreviewPlayback()
    {
        if (!HasPreviewMedia)
            return;

        PreviewStatusText = "Could not play this media file.";
    }

    private bool CanPlayPreview()
    {
        return FindFirstPreviewableClip() is not null;
    }

    private bool CanControlPreview()
    {
        return HasPreviewMedia || IsTimelineGapPreview;
    }

    private void PlayPreview()
    {
        _isTimelinePlaybackActive = true;
        _previewTimelinePosition = TimelinePlaybackPosition;
        PlayTimelineFromPosition(_previewTimelinePosition);
    }

    private void PausePreview()
    {
        if (!HasPreviewMedia && !IsTimelineGapPreview)
            return;

        PreviewStatusText = IsTimelineGapPreview ? "Timeline gap paused." : "Preview paused.";
        RequestPreviewPlayback("Pause");
    }

    private void StopPreview()
    {
        if (!HasPreviewMedia && !IsTimelineGapPreview)
            return;

        _isTimelinePlaybackActive = false;
        IsTimelineGapPreview = false;
        PreviewGapDuration = TimeSpan.Zero;
        PreviewAudioSource = null;
        PreviewAudioSourceStart = TimeSpan.Zero;
        PreviewAudioSourceDuration = TimeSpan.Zero;
        TimelinePlaybackPosition = TimeSpan.Zero;
        PreviewStatusText = "Preview stopped.";
        RequestPreviewPlayback("Stop");
    }

    private void PlayTimelineFromPosition(TimeSpan timelinePosition)
    {
        ConfigureAudioForTimelinePosition(timelinePosition);

        var visualClip = FindVisualClipAt(timelinePosition);
        if (visualClip is not null)
        {
            SelectClipForTimelinePlayback(visualClip, timelinePosition);
            return;
        }

        var nextVisualClip = FindTimelineClipAtOrAfter(timelinePosition);
        var nextAudioClip = FindNextAudioClipAtOrAfter(timelinePosition);
        var nextTimelineEvent = ResolveNextTimelineEvent(timelinePosition, nextVisualClip, nextAudioClip);

        if (nextTimelineEvent is null)
        {
            _isTimelinePlaybackActive = false;
            IsTimelineGapPreview = false;
            PreviewGapDuration = TimeSpan.Zero;
            PreviewAudioSource = null;
            PreviewMediaSource = null;
            PreviewTitle = "Timeline finished";
            PreviewStatusText = "Timeline playback finished.";
            TimelinePlaybackPosition = timelinePosition;
            RequestPreviewPlayback("Stop");
            return;
        }

        PlayTimelineGap(nextTimelineEvent.Value - timelinePosition, timelinePosition);
    }

    private void PlayTimelineGap(TimeSpan duration, TimeSpan timelinePosition)
    {
        _previewClipId = null;
        _previewMediaType = null;
        _previewTimelinePosition = timelinePosition;
        PreviewGapDuration = duration;
        IsTimelineGapPreview = true;
        PreviewMediaSource = null;
        PreviewTitle = "Timeline gap";
        TimelinePlaybackPosition = timelinePosition;
        PreviewStatusText = $"Black screen for {duration.TotalSeconds:0.##} sec.";
        RequestPreviewPlayback("Gap");
    }

    private void SelectClipForTimelinePlayback(TimelineClip clip, TimeSpan timelinePosition)
    {
        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
        {
            _previewTimelinePosition = clip.TimelineEnd;
            PlayTimelineFromPosition(_previewTimelinePosition);
            return;
        }

        var offsetInsideClip = timelinePosition - clip.TimelineStart;
        if (offsetInsideClip < TimeSpan.Zero)
            offsetInsideClip = TimeSpan.Zero;

        _previewClipId = clip.Id;
        _previewMediaType = asset.Type;
        _previewTimelinePosition = timelinePosition;
        PreviewGapDuration = TimeSpan.Zero;
        IsTimelineGapPreview = false;
        PreviewSourceStart = clip.SourceStart + offsetInsideClip;
        PreviewSourceDuration = clip.SourceDuration - offsetInsideClip;
        TimelinePlaybackPosition = timelinePosition;
        PreviewTitle = asset.DisplayName;
        PreviewMediaSource = new Uri(asset.FilePath, UriKind.Absolute);

        if (IsPreviewImage)
        {
            PreviewStatusText = "Showing image on timeline.";
            RequestPreviewPlayback("ImageFrame");
            return;
        }

        PreviewStatusText = "Playing timeline.";
        RequestPreviewPlayback("Play");
    }

    private TimelineClip? FindFirstPreviewableClip()
    {
        return CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault()
            ?? CurrentProject.Tracks
                .SelectMany(x => x.Clips)
                .OrderBy(x => x.TimelineStart)
                .FirstOrDefault();
    }

    private TimelineClip? FindNextTimelinePreviewClip()
    {
        if (_previewClipId is null)
            return null;

        var currentClip = CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .FirstOrDefault(x => x.Id == _previewClipId);

        if (currentClip is null)
            return null;

        return CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .Where(x => x.Id != currentClip.Id && x.TimelineStart >= currentClip.TimelineEnd)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private TimelineClip? FindTimelineClipAtOrAfter(TimeSpan timelinePosition)
    {
        return CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .Where(x => x.TimelineEnd > timelinePosition)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private TimelineClip? FindVisualClipAt(TimeSpan timelinePosition)
    {
        return CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .Where(x => x.TimelineStart <= timelinePosition && x.TimelineEnd > timelinePosition)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private TimelineClip? FindAudioClipAt(TimeSpan timelinePosition)
    {
        return CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Audio"))
            .SelectMany(x => x.Clips)
            .Where(x => x.TimelineStart <= timelinePosition && x.TimelineEnd > timelinePosition)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private TimelineClip? FindNextAudioClipAtOrAfter(TimeSpan timelinePosition)
    {
        return CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Audio"))
            .SelectMany(x => x.Clips)
            .Where(x => x.TimelineEnd > timelinePosition)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private void ConfigureAudioForTimelinePosition(TimeSpan timelinePosition)
    {
        var audioClip = FindAudioClipAt(timelinePosition);
        if (audioClip is null)
        {
            PreviewAudioSource = null;
            PreviewAudioSourceStart = TimeSpan.Zero;
            PreviewAudioSourceDuration = TimeSpan.Zero;
            return;
        }

        var activeVisualClip = FindVisualClipAt(timelinePosition);
        if (activeVisualClip is not null && activeVisualClip.MediaAssetId == audioClip.MediaAssetId)
        {
            PreviewAudioSource = null;
            PreviewAudioSourceStart = TimeSpan.Zero;
            PreviewAudioSourceDuration = TimeSpan.Zero;
            return;
        }

        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == audioClip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
        {
            PreviewAudioSource = null;
            PreviewAudioSourceStart = TimeSpan.Zero;
            PreviewAudioSourceDuration = TimeSpan.Zero;
            return;
        }

        var offsetInsideClip = timelinePosition - audioClip.TimelineStart;
        if (offsetInsideClip < TimeSpan.Zero)
            offsetInsideClip = TimeSpan.Zero;

        PreviewAudioSource = new Uri(asset.FilePath, UriKind.Absolute);
        PreviewAudioSourceStart = audioClip.SourceStart + offsetInsideClip;
        PreviewAudioSourceDuration = audioClip.SourceDuration - offsetInsideClip;
    }

    private TimeSpan? ResolveNextTimelineEvent(
        TimeSpan timelinePosition,
        TimelineClip? nextVisualClip,
        TimelineClip? nextAudioClip)
    {
        var activeAudioClip = FindAudioClipAt(timelinePosition);
        var candidates = new List<TimeSpan>();

        if (nextVisualClip is not null)
            candidates.Add(nextVisualClip.TimelineStart);

        if (activeAudioClip is not null)
            candidates.Add(activeAudioClip.TimelineEnd);
        else if (nextAudioClip is not null)
            candidates.Add(nextAudioClip.TimelineStart);

        return candidates
            .Where(x => x > timelinePosition)
            .OrderBy(x => x)
            .Cast<TimeSpan?>()
            .FirstOrDefault();
    }

    private void RequestPreviewPlayback(string request)
    {
        PreviewPlaybackRequest = request;
        PreviewPlaybackRequestVersion++;
    }

    private void MoveLinkedClip(TimelineClip movedClip, TimeSpan newStart)
    {
        if (movedClip.LinkedGroupId is null)
            return;

        var linkedClip = CurrentProject.Tracks
            .SelectMany(x => x.Clips)
            .FirstOrDefault(x =>
                x.Id != movedClip.Id &&
                x.LinkedGroupId == movedClip.LinkedGroupId);

        if (linkedClip is null)
            return;

        linkedClip.TimelineStart = newStart;
    }

    private static bool AreTracksSameKind(TimelineTrack sourceTrack, TimelineTrack targetTrack)
    {
        return IsTrackOfKind(sourceTrack, "Video") && IsTrackOfKind(targetTrack, "Video") ||
               IsTrackOfKind(sourceTrack, "Audio") && IsTrackOfKind(targetTrack, "Audio");
    }

    private (TimelineTrack Track, TimelineClip Clip)? FindClipWithTrack(Guid clipId)
    {
        foreach (var track in CurrentProject.Tracks)
        {
            var clip = track.Clips.FirstOrDefault(x => x.Id == clipId);
            if (clip is not null)
                return (track, clip);
        }

        return null;
    }

    private IEnumerable<Guid> FindLinkedClipIds(TimelineClip clip)
    {
        if (clip.LinkedGroupId is not null)
        {
            return CurrentProject.Tracks
                .SelectMany(x => x.Clips)
                .Where(x => x.Id != clip.Id && x.LinkedGroupId == clip.LinkedGroupId)
                .Select(x => x.Id);
        }

        return CurrentProject.Tracks
            .SelectMany(x => x.Clips)
            .Where(x =>
                x.Id != clip.Id &&
                x.MediaAssetId == clip.MediaAssetId &&
                x.TimelineStart == clip.TimelineStart &&
                x.SourceStart == clip.SourceStart &&
                x.SourceDuration == clip.SourceDuration)
            .Select(x => x.Id);
    }

    private void ClearPreviewAfterDeletedClip()
    {
        _previewClipId = null;
        _previewMediaType = null;
        _isTimelinePlaybackActive = false;
        IsTimelineGapPreview = false;
        PreviewGapDuration = TimeSpan.Zero;
        PreviewAudioSource = null;
        PreviewAudioSourceStart = TimeSpan.Zero;
        PreviewAudioSourceDuration = TimeSpan.Zero;
        PreviewSourceStart = TimeSpan.Zero;
        PreviewSourceDuration = TimeSpan.Zero;
        PreviewMediaSource = null;
        PreviewTitle = "No clip selected";
        PreviewStatusText = "Clip deleted from timeline.";
        RequestPreviewPlayback("Stop");
    }

    private double CurrentTimelinePixelsPerSecond => TimelineZoomLevels[_timelineZoomIndex];

    private void RefreshTimelinePresentation()
    {
        BuildTimelineRuler();
        RefreshTimelineTracks();
        _zoomInTimelineCommand?.RaiseCanExecuteChanged();
        _zoomOutTimelineCommand?.RaiseCanExecuteChanged();
        _playPreviewCommand?.RaiseCanExecuteChanged();
        _pausePreviewCommand?.RaiseCanExecuteChanged();
        _stopPreviewCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(TimelineZoomMaxIndex));
    }

    private void RefreshTimelineVisualScale()
    {
        RefreshTimelinePresentation();
        _zoomInTimelineCommand.RaiseCanExecuteChanged();
        _zoomOutTimelineCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(TimelineZoomIndex));
        OnPropertyChanged(nameof(TimelineCanvasWidth));
        OnPropertyChanged(nameof(TimelinePlayheadCanvasLeft));
        OnPropertyChanged(nameof(TimelineZoomMaxIndex));
        OnPropertyChanged(nameof(TimelineScaleLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
