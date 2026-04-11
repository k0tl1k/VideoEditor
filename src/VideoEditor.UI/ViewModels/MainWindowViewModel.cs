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
    private readonly RelayCommand _zoomInTimelineCommand;
    private readonly RelayCommand _zoomOutTimelineCommand;

    private ImportedMediaItemViewModel? _selectedImportedMedia;
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

    /// <summary>
    /// 	Текущее текстовое описание масштаба таймлайна.
    /// </summary>
    public string TimelineScaleLabel => CurrentTimelinePixelsPerSecond >= 480 ? "Frames" : "Seconds";

    /// <summary>
    /// 	Максимальный индекс масштаба таймлайна.
    /// </summary>
    public int TimelineZoomMaxIndex => TimelineZoomLevels.Length - 1;

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
        _zoomInTimelineCommand = new RelayCommand(ZoomInTimeline, CanZoomInTimeline);
        _zoomOutTimelineCommand = new RelayCommand(ZoomOutTimeline, CanZoomOutTimeline);

        ImportMediaCommand = new RelayCommand(ImportMedia);
        InsertSelectedMediaToTimelineCommand = _insertSelectedMediaToTimelineCommand;
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
            AddClipToTrack(audioTrack, asset, TimeSpan.Zero, ResolveClipDuration(asset));
            RefreshTimelinePresentation();
            return;
        }

        if (asset.Type == Domain.Enums.MediaType.Image)
        {
            var videoTrack = EnsureTrack("Video");
            AddClipToTrack(videoTrack, asset, TimeSpan.Zero, ResolveClipDuration(asset));
            RefreshTimelinePresentation();
            return;
        }

        InsertVideoWithAudio(asset);
        RefreshTimelinePresentation();
    }

    private void InsertVideoWithAudio(MediaAsset asset)
    {
        var videoTrack = EnsureTrack("Video");
        var audioTrack = EnsureTrack("Audio");
        var duration = ResolveClipDuration(asset);

        AddClipToTrack(videoTrack, asset, TimeSpan.Zero, duration);
        AddClipToTrack(audioTrack, asset, TimeSpan.Zero, duration);
    }

    private void AddClipToTrack(TimelineTrack track, MediaAsset asset, TimeSpan timelineStart, TimeSpan duration)
    {
        track.Clips.Add(new TimelineClip
        {
            MediaAssetId = asset.Id,
            SourceStart = TimeSpan.Zero,
            SourceDuration = duration,
            TimelineStart = timelineStart
        });
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

        var oldStart = clip.TimelineStart;
        var newStart = TimeSpan.FromSeconds(Math.Max(0, left / CurrentTimelinePixelsPerSecond));

        if (!ReferenceEquals(sourceTrack, targetTrack))
        {
            sourceTrack.Clips.Remove(clip);
            targetTrack.Clips.Add(clip);
        }

        clip.TimelineStart = newStart;
        MoveLinkedClip(clip.MediaAssetId, clip.Id, oldStart, newStart);
        RefreshTimelinePresentation();
    }

    private void MoveLinkedClip(Guid mediaAssetId, Guid movedClipId, TimeSpan oldStart, TimeSpan newStart)
    {
        var linkedClip = CurrentProject.Tracks
            .SelectMany(x => x.Clips)
            .FirstOrDefault(x =>
                x.Id != movedClipId &&
                x.MediaAssetId == mediaAssetId &&
                x.TimelineStart == oldStart);

        if (linkedClip is null)
            return;

        linkedClip.TimelineStart = newStart;
    }

    private double CurrentTimelinePixelsPerSecond => TimelineZoomLevels[_timelineZoomIndex];

    private void RefreshTimelinePresentation()
    {
        BuildTimelineRuler();
        RefreshTimelineTracks();
        _zoomInTimelineCommand?.RaiseCanExecuteChanged();
        _zoomOutTimelineCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(TimelineZoomMaxIndex));
    }

    private void RefreshTimelineVisualScale()
    {
        RefreshTimelinePresentation();
        _zoomInTimelineCommand.RaiseCanExecuteChanged();
        _zoomOutTimelineCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(TimelineZoomIndex));
        OnPropertyChanged(nameof(TimelineCanvasWidth));
        OnPropertyChanged(nameof(TimelineZoomMaxIndex));
        OnPropertyChanged(nameof(TimelineScaleLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
