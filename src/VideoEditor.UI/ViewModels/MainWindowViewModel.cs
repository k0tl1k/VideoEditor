using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
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
    private const int TimelineMinimumSeconds = 180;
    private const int TimelineEndPaddingSeconds = 30;
    private const int TimelineMaxComfortableCanvasWidth = 90000;
    private const int TimelineFrameRate = 30;
    private const int InspectorUndoCoalescingMilliseconds = 700;
    private static readonly TimeSpan TimelineBoundaryTolerance = TimeSpan.FromSeconds(1d / TimelineFrameRate);
    private static readonly TimeSpan DefaultVideoClipDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DefaultAudioClipDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DefaultImageClipDuration = TimeSpan.FromSeconds(5);
    private static readonly double[] TimelineZoomLevels =
    [
        0.02, 0.05, 0.1, 0.2, 0.5,
        1, 2, 4, 8, 12, 20, 32,
        48, 64, 96, 128, 240, 480,
        960, 1440
    ];
    private readonly record struct VisualLayerCandidate(TimelineClip Clip, MediaAsset Asset, int TrackIndex);
    public readonly record struct ClipDragPlacement(double Left, bool IsAnchored);
    private sealed record ProjectSnapshot(
        IReadOnlyList<MediaAssetSnapshot> MediaAssets,
        IReadOnlyList<TimelineTrackSnapshot> Tracks,
        IReadOnlyList<Guid> SelectedClipIds,
        Guid? SelectedClipId,
        TimeSpan TimelinePlaybackPosition);
    private sealed record MediaAssetSnapshot(
        Guid Id,
        string FilePath,
        string DisplayName,
        Domain.Enums.MediaType Type,
        TimeSpan Duration);
    private sealed record TimelineTrackSnapshot(
        Guid Id,
        string Name,
        bool IsEnabled,
        IReadOnlyList<TimelineClipSnapshot> Clips);
    private sealed record TimelineClipSnapshot(
        Guid Id,
        Guid MediaAssetId,
        Guid? LinkedGroupId,
        TimeSpan SourceStart,
        TimeSpan SourceDuration,
        TimeSpan TrimBaselineSourceStart,
        TimeSpan TrimBaselineSourceDuration,
        TimeSpan TimelineStart,
        double FrameX,
        double FrameY,
        double FrameScale,
        double AudioVolume,
        bool MuteEmbeddedAudio);
    private sealed record CopiedTimelineClip(string TrackName, TimelineClipSnapshot Clip, TimeSpan Offset);

    private readonly IMediaImportService _mediaImportService;
    private readonly IMediaThumbnailService _mediaThumbnailService;
    private readonly IAudioWaveformService _audioWaveformService;
    private readonly ITimelineExportService _timelineExportService;
    private readonly RelayCommand _browseExportOutputPathCommand;
    private readonly RelayCommand _exportProjectCommand;
    private readonly RelayCommand _insertSelectedMediaToTimelineCommand;
    private readonly RelayCommand _splitSelectedClipCommand;
    private readonly RelayCommand _playPreviewCommand;
    private readonly RelayCommand _pausePreviewCommand;
    private readonly RelayCommand _clearTimelineSelectionCommand;
    private readonly RelayCommand _deleteSelectedTimelineClipsCommand;
    private readonly RelayCommand _resetSelectedClipFrameCommand;
    private readonly RelayCommand _resetSelectedClipTrimCommand;
    private readonly RelayCommand _selectAllTimelineClipsCommand;
    private readonly RelayCommand _stopPreviewCommand;
    private readonly RelayCommand _selectTimelineMoveToolCommand;
    private readonly RelayCommand _selectTimelineSelectToolCommand;
    private readonly RelayCommand _selectTimelineRazorToolCommand;
    private readonly RelayCommand _undoCommand;
    private readonly RelayCommand _redoCommand;
    private readonly RelayCommand _zoomInTimelineCommand;
    private readonly RelayCommand _zoomOutTimelineCommand;
    private readonly Stack<ProjectSnapshot> _undoStack = new();
    private readonly Stack<ProjectSnapshot> _redoStack = new();
    private readonly List<CopiedTimelineClip> _copiedTimelineClips = new();

    private string? _coalescedUndoKey;
    private Guid? _coalescedUndoClipId;
    private DateTime _lastCoalescedUndoUtc;
    private ImportedMediaItemViewModel? _selectedImportedMedia;
    private string _importedMediaSearchText = string.Empty;
    private Domain.Enums.MediaType? _previewMediaType;
    private Guid? _previewClipId;
    private bool _isTimelineGapPreview;
    private bool _isTimelinePlaybackActive;
    private bool _isPreviewVideoAudioMuted;
    private bool _isRestoringHistory;
    private Guid? _draggedTimelineClipId;
    private readonly HashSet<Guid> _draggedTimelineClipIds = new();
    private readonly HashSet<Guid> _selectedTimelineClipIds = new();
    private TimelineToolMode _timelineToolMode = TimelineToolMode.Move;
    private bool _isExporting;
    private double _exportProgressPercent;
    private string _exportProgressText = "Export is idle.";
    private int _exportFrameRate = 30;
    private int _exportConstantRateFactor = 23;
    private int _exportAudioBitrateKbps = 192;
    private string _exportVideoCodec = "H.264";
    private string _exportFileType = "MP4";
    private string _exportPreset = "medium";
    private string _exportOutputPath = string.Empty;
    private string _exportResolutionPreset = "1920x1080";
    private int _exportWidth = 1920;
    private int _exportHeight = 1080;
    private bool _useCustomExportRange;
    private double _exportRangeStartSeconds;
    private double _exportRangeEndSeconds;
    private TimeSpan _previewGapDuration;
    private Uri? _previewAudioSource;
    private TimeSpan _previewAudioSourceDuration;
    private TimeSpan _previewAudioSourceStart;
    private double _previewAudioVolume = 1.0;
    private Uri? _previewMediaSource;
    private TimeSpan _previewSourceDuration;
    private TimeSpan _previewSourceStart;
    private double _previewVideoVolume = 1.0;
    private TimeSpan _previewTimelinePosition;
    private TimeSpan _timelinePlaybackPosition;
    private TimelineClip? _previewBaseVisualClip;
    private TimelineClip? _selectedAudioClip;
    private Guid? _selectedTimelineClipId;
    private TimelineClip? _selectedVisualClip;
    private string _previewPlaybackRequest = "Stop";
    private int _previewPlaybackRequestVersion;
    private string _previewStatusText = "Add a clip to the timeline, then select it for preview.";
    private string _previewTitle = "No clip selected";
    private int _timelineZoomIndex = 11;

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
    /// 	Отфильтрованный view импортированных медиа.
    /// </summary>
    public ICollectionView ImportedMediaView { get; }

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
            var clampedValue = Math.Clamp(value, 0, TimelineZoomMaxIndex);
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

    public ObservableCollection<PreviewVisualLayerViewModel> PreviewVisualLayers { get; } = new();

    /// <summary>
    /// 	Команда импорта медиафайлов.
    /// </summary>
    public ICommand ImportMediaCommand { get; }

    public ICommand BrowseExportOutputPathCommand { get; }

    public ICommand ExportProjectCommand { get; }

    /// <summary>
    /// 	Команда вставки выбранного медиафайла на таймлайн.
    /// </summary>
    public ICommand InsertSelectedMediaToTimelineCommand { get; }

    public ICommand SplitSelectedClipCommand { get; }

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

    public ICommand ClearTimelineSelectionCommand { get; }

    public ICommand DeleteSelectedTimelineClipsCommand { get; }

    public ICommand ResetSelectedClipFrameCommand { get; }

    public ICommand ResetSelectedClipTrimCommand { get; }

    public ICommand SelectAllTimelineClipsCommand { get; }

    public ICommand SelectTimelineMoveToolCommand { get; }

    public ICommand SelectTimelineSelectToolCommand { get; }

    public ICommand SelectTimelineRazorToolCommand { get; }

    public ICommand UndoCommand { get; }

    public ICommand RedoCommand { get; }

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
    public double TimelineCanvasWidth => ResolveTimelineCanvasDuration().TotalSeconds * CurrentTimelinePixelsPerSecond;

    public double TimelineDurationSeconds => ResolveTimelineCanvasDuration().TotalSeconds;

    public double TimelinePixelsPerSecond => CurrentTimelinePixelsPerSecond;

    public TimelineToolMode CurrentTimelineToolMode
    {
        get => _timelineToolMode;
        private set
        {
            if (_timelineToolMode == value)
                return;

            _timelineToolMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSelectTimelineToolActive));
            OnPropertyChanged(nameof(IsMoveTimelineToolActive));
            OnPropertyChanged(nameof(IsRazorTimelineToolActive));
        }
    }

    public bool IsMoveTimelineToolActive => CurrentTimelineToolMode == TimelineToolMode.Move;

    public bool IsSelectTimelineToolActive => CurrentTimelineToolMode == TimelineToolMode.Select;

    public bool IsRazorTimelineToolActive => CurrentTimelineToolMode == TimelineToolMode.Razor;

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
            RefreshSplitCommandState();
        }
    }

    public double TimelinePlayheadCanvasLeft => TimelinePlaybackPosition.TotalSeconds * CurrentTimelinePixelsPerSecond;

    public string TimelinePlaybackPositionLabel => FormatTimecodeLabel(
        (int)Math.Floor(TimelinePlaybackPosition.TotalSeconds),
        TimelinePlaybackPosition.Milliseconds * TimelineFrameRate / 1000);

    /// <summary>
    /// 	Текущее текстовое описание масштаба таймлайна.
    /// </summary>
    public string TimelineScaleLabel => CurrentTimelinePixelsPerSecond >= 480
        ? $"Frames · {CurrentTimelinePixelsPerSecond:0.#}px/s"
        : $"Seconds · {CurrentTimelinePixelsPerSecond:0.#}px/s";

    /// <summary>
    /// 	Максимальный индекс масштаба таймлайна.
    /// </summary>
    public int TimelineZoomMaxIndex => ResolveTimelineZoomMaxIndex();

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

    public double PreviewAudioVolume
    {
        get => _previewAudioVolume;
        private set
        {
            var clampedValue = Math.Clamp(value, 0, 2.0);
            if (Math.Abs(_previewAudioVolume - clampedValue) < 0.001)
                return;

            _previewAudioVolume = clampedValue;
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

    public double PreviewVideoVolume
    {
        get => _previewVideoVolume;
        private set
        {
            var clampedValue = Math.Clamp(value, 0, 2.0);
            if (Math.Abs(_previewVideoVolume - clampedValue) < 0.001)
                return;

            _previewVideoVolume = clampedValue;
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

    public bool IsPreviewVideoAudioMuted
    {
        get => _isPreviewVideoAudioMuted;
        private set
        {
            if (_isPreviewVideoAudioMuted == value)
                return;

            _isPreviewVideoAudioMuted = value;
            OnPropertyChanged();
        }
    }

    public bool IsPreviewImage => HasPreviewMedia && _previewMediaType == Domain.Enums.MediaType.Image;

    public bool IsPreviewPlayableMedia => HasPreviewMedia && _previewMediaType != Domain.Enums.MediaType.Image;

    public bool IsPreviewPlaceholderVisible => !HasPreviewMedia && !IsTimelineGapPreview;

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (_isExporting == value)
                return;

            _isExporting = value;
            OnPropertyChanged();
            _exportProjectCommand.RaiseCanExecuteChanged();
        }
    }

    public double ExportProgressPercent
    {
        get => _exportProgressPercent;
        private set
        {
            var clampedValue = Math.Clamp(value, 0, 100);
            if (Math.Abs(_exportProgressPercent - clampedValue) < 0.01)
                return;

            _exportProgressPercent = clampedValue;
            OnPropertyChanged();
        }
    }

    public string ExportProgressText
    {
        get => _exportProgressText;
        private set
        {
            if (_exportProgressText == value)
                return;

            _exportProgressText = value;
            OnPropertyChanged();
        }
    }

    public int ExportFrameRate
    {
        get => _exportFrameRate;
        set
        {
            var clampedValue = Math.Clamp(value, 24, 60);
            if (_exportFrameRate == clampedValue)
                return;

            _exportFrameRate = clampedValue;
            OnPropertyChanged();
        }
    }

    public int ExportConstantRateFactor
    {
        get => _exportConstantRateFactor;
        set
        {
            var clampedValue = Math.Clamp(value, 18, 30);
            if (_exportConstantRateFactor == clampedValue)
                return;

            _exportConstantRateFactor = clampedValue;
            OnPropertyChanged();
        }
    }

    public int ExportAudioBitrateKbps
    {
        get => _exportAudioBitrateKbps;
        set
        {
            var clampedValue = Math.Clamp(value, 96, 320);
            if (_exportAudioBitrateKbps == clampedValue)
                return;

            _exportAudioBitrateKbps = clampedValue;
            OnPropertyChanged();
        }
    }

    public string ExportPreset
    {
        get => _exportPreset;
        set
        {
            var normalizedValue = NormalizeExportPreset(value);
            if (_exportPreset == normalizedValue)
                return;

            _exportPreset = normalizedValue;
            OnPropertyChanged();
        }
    }

    public string ExportVideoCodec
    {
        get => _exportVideoCodec;
        set
        {
            var normalizedValue = NormalizeExportVideoCodec(value);
            if (_exportVideoCodec == normalizedValue)
                return;

            _exportVideoCodec = normalizedValue;
            OnPropertyChanged();
        }
    }

    public string ExportFileType
    {
        get => _exportFileType;
        set
        {
            var normalizedValue = NormalizeExportFileType(value);
            if (_exportFileType == normalizedValue)
                return;

            _exportFileType = normalizedValue;
            OnPropertyChanged();
            UpdateExportOutputPathExtension();
        }
    }

    public string ExportOutputPath
    {
        get => _exportOutputPath;
        set
        {
            if (_exportOutputPath == value)
                return;

            _exportOutputPath = value;
            OnPropertyChanged();
            _exportProjectCommand.RaiseCanExecuteChanged();
        }
    }

    public string ExportResolutionPreset
    {
        get => _exportResolutionPreset;
        set
        {
            var normalizedValue = NormalizeExportResolutionPreset(value);
            if (_exportResolutionPreset == normalizedValue)
                return;

            _exportResolutionPreset = normalizedValue;
            ApplyExportResolutionPreset(normalizedValue);
            OnPropertyChanged();
        }
    }

    public int ExportWidth
    {
        get => _exportWidth;
        set
        {
            var clampedValue = Math.Clamp(value, 320, 7680);
            if (_exportWidth == clampedValue)
                return;

            _exportWidth = clampedValue;
            OnPropertyChanged();
        }
    }

    public int ExportHeight
    {
        get => _exportHeight;
        set
        {
            var clampedValue = Math.Clamp(value, 240, 4320);
            if (_exportHeight == clampedValue)
                return;

            _exportHeight = clampedValue;
            OnPropertyChanged();
        }
    }

    public double ExportRangeStartSeconds
    {
        get => _exportRangeStartSeconds;
        set
        {
            var clampedValue = Math.Max(0, value);
            if (Math.Abs(_exportRangeStartSeconds - clampedValue) < 0.001)
                return;

            _exportRangeStartSeconds = clampedValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExportRangeLabel));
        }
    }

    public bool UseCustomExportRange
    {
        get => _useCustomExportRange;
        set
        {
            if (_useCustomExportRange == value)
                return;

            _useCustomExportRange = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExportRangeLabel));
        }
    }

    public double ExportRangeEndSeconds
    {
        get => _exportRangeEndSeconds;
        set
        {
            var clampedValue = Math.Max(0, value);
            if (Math.Abs(_exportRangeEndSeconds - clampedValue) < 0.001)
                return;

            _exportRangeEndSeconds = clampedValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExportRangeLabel));
        }
    }

    public string ExportRangeLabel => UseCustomExportRange
        ? $"Range: {FormatSecondsLabel(ExportRangeStartSeconds)} - {FormatSecondsLabel(ExportRangeEndSeconds)}"
        : "Range: full timeline";

    public Uri? PreviewImageSource => IsPreviewImage ? PreviewMediaSource : null;

    public Uri? PreviewPlayableMediaSource => IsPreviewPlayableMedia ? PreviewMediaSource : null;

    public bool HasSelectedVisualClip => _selectedVisualClip is not null;

    public bool HasSelectedAudioClip => _selectedAudioClip is not null;

    public bool HasTimelineSelection => _selectedTimelineClipIds.Count > 0;

    public int SelectedTimelineClipCount => _selectedTimelineClipIds.Count;

    public bool IsInspectorPlaceholderVisible => !HasSelectedVisualClip && !HasSelectedAudioClip;

    public double SelectedClipTrimStartSeconds
    {
        get => GetSelectedTrimClip()?.SourceStart.TotalSeconds ?? 0;
        set => UpdateSelectedClipTrim(value, SelectedClipTrimEndSeconds);
    }

    public double SelectedClipTrimEndSeconds
    {
        get
        {
            var clip = GetSelectedTrimClip();
            return clip is null ? 0 : (clip.SourceStart + clip.SourceDuration).TotalSeconds;
        }
        set => UpdateSelectedClipTrim(SelectedClipTrimStartSeconds, value);
    }

    public double SelectedClipTrimDurationSeconds
    {
        get => GetSelectedTrimClip()?.SourceDuration.TotalSeconds ?? 0;
        set
        {
            var clip = GetSelectedTrimClip();
            var asset = GetSelectedTrimAsset();
            if (clip is null || asset is null)
                return;

            var durationSeconds = Math.Max(1.0 / TimelineFrameRate, value);
            UpdateSelectedClipTrim(clip.SourceStart.TotalSeconds, clip.SourceStart.TotalSeconds + durationSeconds);
        }
    }

    public string SelectedClipTrimLabel
    {
        get
        {
            var clip = GetSelectedTrimClip();
            if (clip is null)
                return "Trim: not selected";

            return $"Trim: {FormatSecondsLabel(clip.SourceStart.TotalSeconds)} - {FormatSecondsLabel((clip.SourceStart + clip.SourceDuration).TotalSeconds)}";
        }
    }

    public double SelectedClipAudioVolumePercent
    {
        get => (_selectedAudioClip?.AudioVolume ?? 1.0) * 100;
        set
        {
            var selectedAudioClips = GetSelectedAudioClips().ToList();
            if (selectedAudioClips.Count == 0)
                return;

            var volume = Math.Clamp(value / 100, 0, 2.0);
            if (selectedAudioClips.All(clip => Math.Abs(clip.AudioVolume - volume) < 0.001))
                return;

            SaveCoalescedUndoSnapshot("audio-volume", _selectedAudioClip?.Id ?? selectedAudioClips[0].Id);
            foreach (var clip in selectedAudioClips)
                clip.AudioVolume = volume;

            RefreshSelectedAudioClipProperties();
            ConfigureAudioForTimelinePosition(TimelinePlaybackPosition);
        }
    }

    public double SelectedClipFrameX
    {
        get => _selectedVisualClip?.FrameX ?? 0;
        set
        {
            var selectedVisualClips = GetSelectedVisualClips().ToList();
            if (selectedVisualClips.Count == 0 ||
                selectedVisualClips.All(clip => Math.Abs(clip.FrameX - value) < 0.001))
                return;

            SaveCoalescedUndoSnapshot("frame-x", _selectedVisualClip?.Id ?? selectedVisualClips[0].Id);
            foreach (var clip in selectedVisualClips)
                clip.FrameX = value;

            RefreshSelectedClipFrameProperties();
            _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
        }
    }

    public double SelectedClipFrameY
    {
        get => _selectedVisualClip?.FrameY ?? 0;
        set
        {
            var selectedVisualClips = GetSelectedVisualClips().ToList();
            if (selectedVisualClips.Count == 0 ||
                selectedVisualClips.All(clip => Math.Abs(clip.FrameY - value) < 0.001))
                return;

            SaveCoalescedUndoSnapshot("frame-y", _selectedVisualClip?.Id ?? selectedVisualClips[0].Id);
            foreach (var clip in selectedVisualClips)
                clip.FrameY = value;

            RefreshSelectedClipFrameProperties();
            _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
        }
    }

    public double SelectedClipFrameScalePercent
    {
        get => (_selectedVisualClip?.FrameScale ?? 1.0) * 100;
        set
        {
            var selectedVisualClips = GetSelectedVisualClips().ToList();
            if (selectedVisualClips.Count == 0)
                return;

            var scale = Math.Clamp(value / 100, 0.1, 3.0);
            if (selectedVisualClips.All(clip => Math.Abs(clip.FrameScale - scale) < 0.001))
                return;

            SaveCoalescedUndoSnapshot("frame-scale", _selectedVisualClip?.Id ?? selectedVisualClips[0].Id);
            foreach (var clip in selectedVisualClips)
                clip.FrameScale = scale;

            RefreshSelectedClipFrameProperties();
            _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
        }
    }

    public double PreviewFrameX => _previewBaseVisualClip?.FrameX ?? 0;

    public double PreviewFrameY => _previewBaseVisualClip?.FrameY ?? 0;

    public double PreviewFrameScale => _previewBaseVisualClip?.FrameScale ?? 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool CanResetSelectedClipFrame()
    {
        return GetSelectedVisualClips().Any(clip =>
            Math.Abs(clip.FrameX) > 0.001 ||
            Math.Abs(clip.FrameY) > 0.001 ||
            Math.Abs(clip.FrameScale - 1.0) > 0.001);
    }

    private bool CanResetSelectedClipTrim()
    {
        return GetSelectedTrimTargets().Any(x =>
            Math.Abs((x.Clip.SourceStart - x.Clip.TrimBaselineSourceStart).TotalSeconds) > 0.001 ||
            Math.Abs((x.Clip.SourceDuration - x.Clip.TrimBaselineSourceDuration).TotalSeconds) > 0.001);
    }

    private bool CanUndo()
    {
        return _undoStack.Count > 0;
    }

    private bool CanRedo()
    {
        return _redoStack.Count > 0;
    }

    private void Undo()
    {
        if (!CanUndo())
            return;

        ResetCoalescedUndoState();
        _redoStack.Push(CaptureProjectSnapshot());
        RestoreProjectSnapshot(_undoStack.Pop());
        PreviewStatusText = "Undo applied.";
    }

    private void Redo()
    {
        if (!CanRedo())
            return;

        ResetCoalescedUndoState();
        _undoStack.Push(CaptureProjectSnapshot());
        RestoreProjectSnapshot(_redoStack.Pop());
        PreviewStatusText = "Redo applied.";
    }

    private void SaveUndoSnapshot(bool resetCoalescing = true)
    {
        if (_isRestoringHistory)
            return;

        _undoStack.Push(CaptureProjectSnapshot());
        _redoStack.Clear();
        if (resetCoalescing)
            ResetCoalescedUndoState();

        RefreshHistoryCommandState();
    }

    private void SaveCoalescedUndoSnapshot(string key, Guid clipId)
    {
        if (_isRestoringHistory)
            return;

        var now = DateTime.UtcNow;
        var isSameBatch = _coalescedUndoKey == key &&
            _coalescedUndoClipId == clipId &&
            now - _lastCoalescedUndoUtc <= TimeSpan.FromMilliseconds(InspectorUndoCoalescingMilliseconds);

        if (!isSameBatch)
            SaveUndoSnapshot(resetCoalescing: false);

        _coalescedUndoKey = key;
        _coalescedUndoClipId = clipId;
        _lastCoalescedUndoUtc = now;
    }

    private void ResetCoalescedUndoState()
    {
        _coalescedUndoKey = null;
        _coalescedUndoClipId = null;
        _lastCoalescedUndoUtc = default;
    }

    private ProjectSnapshot CaptureProjectSnapshot()
    {
        var mediaAssets = CurrentProject.MediaAssets
            .Select(asset => new MediaAssetSnapshot(
                asset.Id,
                asset.FilePath,
                asset.DisplayName,
                asset.Type,
                asset.Duration))
            .ToList();

          var tracks = CurrentProject.Tracks
              .Select(track => new TimelineTrackSnapshot(
                  track.Id,
                  track.Name,
                  track.IsEnabled,
                  track.Clips
                    .Select(CaptureTimelineClipSnapshot)
                      .ToList()))
              .ToList();

        return new ProjectSnapshot(
            mediaAssets,
            tracks,
            _selectedTimelineClipIds.ToList(),
            _selectedTimelineClipId,
              TimelinePlaybackPosition);
      }

    private static TimelineClipSnapshot CaptureTimelineClipSnapshot(TimelineClip clip)
    {
        return new TimelineClipSnapshot(
            clip.Id,
            clip.MediaAssetId,
            clip.LinkedGroupId,
            clip.SourceStart,
            clip.SourceDuration,
            clip.TrimBaselineSourceStart,
            clip.TrimBaselineSourceDuration,
            clip.TimelineStart,
            clip.FrameX,
            clip.FrameY,
            clip.FrameScale,
            clip.AudioVolume,
            clip.MuteEmbeddedAudio);
    }

      private void RestoreProjectSnapshot(ProjectSnapshot snapshot)
    {
        _isRestoringHistory = true;
        try
        {
            CurrentProject.MediaAssets.Clear();
            foreach (var asset in snapshot.MediaAssets)
            {
                CurrentProject.MediaAssets.Add(new MediaAsset
                {
                    Id = asset.Id,
                    FilePath = asset.FilePath,
                    DisplayName = asset.DisplayName,
                    Type = asset.Type,
                    Duration = asset.Duration
                });
            }

            CurrentProject.Tracks.Clear();
            foreach (var trackSnapshot in snapshot.Tracks)
            {
                var track = new TimelineTrack
                {
                    Id = trackSnapshot.Id,
                    Name = trackSnapshot.Name,
                    IsEnabled = trackSnapshot.IsEnabled
                };

                foreach (var clip in trackSnapshot.Clips)
                {
                    track.Clips.Add(new TimelineClip
                    {
                        Id = clip.Id,
                        MediaAssetId = clip.MediaAssetId,
                        LinkedGroupId = clip.LinkedGroupId,
                        SourceStart = clip.SourceStart,
                        SourceDuration = clip.SourceDuration,
                        TrimBaselineSourceStart = clip.TrimBaselineSourceStart,
                        TrimBaselineSourceDuration = clip.TrimBaselineSourceDuration,
                        TimelineStart = clip.TimelineStart,
                        FrameX = clip.FrameX,
                        FrameY = clip.FrameY,
                        FrameScale = clip.FrameScale,
                        AudioVolume = clip.AudioVolume,
                        MuteEmbeddedAudio = clip.MuteEmbeddedAudio
                    });
                }

                CurrentProject.Tracks.Add(track);
            }

            RebuildImportedMediaItems();
            _selectedTimelineClipIds.Clear();
            foreach (var selectedClipId in snapshot.SelectedClipIds)
            {
                if (FindClipWithTrack(selectedClipId) is not null)
                    _selectedTimelineClipIds.Add(selectedClipId);
            }

            _selectedTimelineClipId = snapshot.SelectedClipId is not null &&
                FindClipWithTrack(snapshot.SelectedClipId.Value) is not null
                    ? snapshot.SelectedClipId
                    : _selectedTimelineClipIds.FirstOrDefault();

            if (_selectedTimelineClipId == Guid.Empty)
                _selectedTimelineClipId = null;

            RefreshActiveSelectionFromSelectedId();
            RefreshSelectedClipFrameProperties();
            RefreshSelectedAudioClipProperties();
            RefreshSelectedClipTrimProperties();
            RefreshTimelinePresentation();
            RefreshTimelineSelectionCommandState();
            RefreshHistoryCommandState();
            LoadTimelinePreviewAtPosition(snapshot.TimelinePlaybackPosition);
        }
        finally
        {
            _isRestoringHistory = false;
        }
    }

    private void RebuildImportedMediaItems()
    {
        ImportedMedia.Clear();
        foreach (var asset in CurrentProject.MediaAssets)
        {
            var thumbnailPath = _mediaThumbnailService.GetThumbnailPath(asset);
            ImportedMedia.Add(new ImportedMediaItemViewModel(asset, thumbnailPath));
        }

        ImportedMediaView.Refresh();
    }

    private void RefreshHistoryCommandState()
    {
        _undoCommand.RaiseCanExecuteChanged();
        _redoCommand.RaiseCanExecuteChanged();
    }

    private void ResetSelectedClipFrame()
    {
        var selectedVisualClips = GetSelectedVisualClips().ToList();
        if (selectedVisualClips.Count == 0)
            return;

        SaveUndoSnapshot();
        foreach (var clip in selectedVisualClips)
        {
            clip.FrameX = 0;
            clip.FrameY = 0;
            clip.FrameScale = 1.0;
        }

        RefreshSelectedClipFrameProperties();
        _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
        PreviewStatusText = "Selected clip transform reset.";
    }

    private void ResetSelectedClipTrim()
    {
        var trimTargets = GetSelectedTrimTargets().ToList();
        if (trimTargets.Count == 0)
            return;

        SaveUndoSnapshot();
        foreach (var (clip, asset) in trimTargets)
        {
            ApplyTrimToClip(
                clip,
                asset,
                clip.TrimBaselineSourceStart.TotalSeconds,
                (clip.TrimBaselineSourceStart + clip.TrimBaselineSourceDuration).TotalSeconds,
                saveUndo: false,
                refreshAfterApply: false);
        }

        RefreshTimelinePresentation();
        RefreshSelectedClipTrimProperties();
        RefreshCurrentTimelinePreviewAfterEdit();
        PreviewStatusText = "Selected clip trim reset.";
    }

    private bool CanClearTimelineSelection()
    {
        return HasTimelineSelection;
    }

    private bool CanDeleteSelectedTimelineClips()
    {
        return HasTimelineSelection;
    }

    private bool CanSelectAllTimelineClips()
    {
        return CurrentProject.Tracks.Any(track => track.Clips.Count > 0);
    }

    private void SelectTimelineMoveTool()
    {
        CurrentTimelineToolMode = TimelineToolMode.Move;
        PreviewStatusText = "Timeline tool: Move.";
    }

    private void SelectTimelineSelectTool()
    {
        CurrentTimelineToolMode = TimelineToolMode.Select;
        PreviewStatusText = "Timeline tool: Select.";
    }

    private void SelectTimelineRazorTool()
    {
        CurrentTimelineToolMode = TimelineToolMode.Razor;
        PreviewStatusText = "Timeline tool: Razor.";
    }

    public void ClearTimelineSelection()
    {
        if (!HasTimelineSelection)
            return;

        _selectedTimelineClipIds.Clear();
        _selectedTimelineClipId = null;
        _selectedAudioClip = null;
        _selectedVisualClip = null;
        RefreshSelectedClipFrameProperties();
        RefreshSelectedAudioClipProperties();
        RefreshSelectedClipTrimProperties();
        RefreshTimelineTracks();
        RefreshTimelineSelectionCommandState();
        PreviewStatusText = "Timeline selection cleared.";
    }

    public bool IsTimelineClipSelected(Guid clipId)
    {
        return _selectedTimelineClipIds.Contains(clipId);
    }

    public void SelectTimelineClipsInTrackRange(string trackName, double left, double right, bool addToSelection)
    {
        var start = TimeSpan.FromSeconds(Math.Max(0, Math.Min(left, right) / CurrentTimelinePixelsPerSecond));
        var end = TimeSpan.FromSeconds(Math.Max(0, Math.Max(left, right) / CurrentTimelinePixelsPerSecond));

        if (!addToSelection)
            _selectedTimelineClipIds.Clear();

        foreach (var clip in CurrentProject.Tracks.SelectMany(track => track.Clips))
        {
            if (clip.TimelineEnd < start || clip.TimelineStart > end)
                continue;

            _selectedTimelineClipIds.Add(clip.Id);
        }

        _selectedTimelineClipId = _selectedTimelineClipIds.LastOrDefault();
        if (_selectedTimelineClipId == Guid.Empty)
            _selectedTimelineClipId = null;

        RefreshActiveSelectionFromSelectedId();
        RefreshSelectedClipFrameProperties();
        RefreshSelectedAudioClipProperties();
        RefreshSelectedClipTrimProperties();
        RefreshTimelineTracks();
        RefreshTimelineSelectionCommandState();
        PreviewStatusText = HasTimelineSelection
            ? $"Selected {SelectedTimelineClipCount} timeline clip(s)."
            : "Timeline selection cleared.";
    }

    private void SelectAllTimelineClips()
    {
        _selectedTimelineClipIds.Clear();

        foreach (var clip in CurrentProject.Tracks.SelectMany(track => track.Clips))
            _selectedTimelineClipIds.Add(clip.Id);

        _selectedTimelineClipId = _selectedTimelineClipIds.FirstOrDefault();
        if (_selectedTimelineClipId == Guid.Empty)
            _selectedTimelineClipId = null;

        RefreshActiveSelectionFromSelectedId();
        RefreshTimelineTracks();
        RefreshTimelineSelectionCommandState();
        PreviewStatusText = $"Selected {SelectedTimelineClipCount} timeline clip(s).";
    }

    private void DeleteSelectedTimelineClips()
    {
        if (!HasTimelineSelection)
            return;

        SaveUndoSnapshot();
        var idsToDelete = new HashSet<Guid>(_selectedTimelineClipIds);

        var shouldClearPreview = _previewClipId is not null && idsToDelete.Contains(_previewClipId.Value);

        foreach (var track in CurrentProject.Tracks)
        {
            for (var index = track.Clips.Count - 1; index >= 0; index--)
            {
                if (idsToDelete.Contains(track.Clips[index].Id))
                    track.Clips.RemoveAt(index);
            }
        }

        _selectedTimelineClipIds.Clear();
        _selectedTimelineClipId = null;
        _selectedAudioClip = null;
        _selectedVisualClip = null;

        if (shouldClearPreview)
            ClearPreviewAfterDeletedClip();
        else
        {
            RefreshSelectedClipFrameProperties();
            RefreshSelectedAudioClipProperties();
            RefreshSelectedClipTrimProperties();
            RefreshTimelineSelectionCommandState();
        }

        RefreshTimelinePresentation();
        PreviewStatusText = $"Deleted {idsToDelete.Count} timeline clip(s).";
    }

    public bool CopySelectedTimelineClips()
    {
        var selectedClips = GetSelectedClipsWithTracks()
            .OrderBy(x => x.Clip.TimelineStart)
            .ThenBy(x => x.Track.Name)
            .ToList();

        if (selectedClips.Count == 0)
            return false;

        var earliestStart = selectedClips.Min(x => x.Clip.TimelineStart);
        _copiedTimelineClips.Clear();
        foreach (var (track, clip) in selectedClips)
        {
            _copiedTimelineClips.Add(new CopiedTimelineClip(
                track.Name,
                CaptureTimelineClipSnapshot(clip),
                clip.TimelineStart - earliestStart));
        }

        PreviewStatusText = $"Copied {selectedClips.Count} timeline clip(s).";
        return true;
    }

    public bool PasteCopiedTimelineClips()
    {
        if (_copiedTimelineClips.Count == 0)
            return false;

        SaveUndoSnapshot();
        var pasteStart = SnapTimelineTime(TimelinePlaybackPosition);
        var linkedGroupCounts = _copiedTimelineClips
            .Select(x => x.Clip.LinkedGroupId)
            .Where(x => x is not null)
            .GroupBy(x => x!.Value)
            .ToDictionary(x => x.Key, x => x.Count());
        var linkedGroupMap = linkedGroupCounts
            .Where(x => x.Value > 1)
            .ToDictionary(x => x.Key, _ => Guid.NewGuid());
        var insertedClipIds = new List<Guid>();

        foreach (var copiedClip in _copiedTimelineClips)
        {
            var targetTrack = CurrentProject.Tracks.FirstOrDefault(x =>
                x.Name.Equals(copiedClip.TrackName, StringComparison.OrdinalIgnoreCase));
            if (targetTrack is null)
                continue;

            var newLinkedGroupId = copiedClip.Clip.LinkedGroupId is Guid oldLinkedGroupId &&
                linkedGroupMap.TryGetValue(oldLinkedGroupId, out var mappedLinkedGroupId)
                    ? mappedLinkedGroupId
                    : (Guid?)null;

            var newClip = new TimelineClip
            {
                MediaAssetId = copiedClip.Clip.MediaAssetId,
                LinkedGroupId = newLinkedGroupId,
                SourceStart = copiedClip.Clip.SourceStart,
                SourceDuration = copiedClip.Clip.SourceDuration,
                TrimBaselineSourceStart = copiedClip.Clip.TrimBaselineSourceStart,
                TrimBaselineSourceDuration = copiedClip.Clip.TrimBaselineSourceDuration,
                TimelineStart = pasteStart + copiedClip.Offset,
                FrameX = copiedClip.Clip.FrameX,
                FrameY = copiedClip.Clip.FrameY,
                FrameScale = copiedClip.Clip.FrameScale,
                AudioVolume = copiedClip.Clip.AudioVolume,
                MuteEmbeddedAudio = copiedClip.Clip.MuteEmbeddedAudio
            };

            targetTrack.Clips.Add(newClip);
            insertedClipIds.Add(newClip.Id);
        }

        if (insertedClipIds.Count == 0)
            return false;

        _selectedTimelineClipIds.Clear();
        foreach (var insertedClipId in insertedClipIds)
            _selectedTimelineClipIds.Add(insertedClipId);

        _selectedTimelineClipId = insertedClipIds.FirstOrDefault();
        RefreshActiveSelectionFromSelectedId();
        RefreshSelectedClipFrameProperties();
        RefreshSelectedAudioClipProperties();
        RefreshSelectedClipTrimProperties();
        RefreshTimelineSelectionCommandState();
        RefreshTimelinePresentation();
        RefreshCurrentTimelinePreviewAfterEdit();
        PreviewStatusText = $"Pasted {insertedClipIds.Count} timeline clip(s).";
        return true;
    }

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
        IMediaThumbnailService mediaThumbnailService,
        IAudioWaveformService audioWaveformService,
        ITimelineExportService timelineExportService)
    {
        _mediaImportService = mediaImportService;
        _mediaThumbnailService = mediaThumbnailService;
        _audioWaveformService = audioWaveformService;
        _timelineExportService = timelineExportService;

        CurrentProject = projectBootstrapService.CreateDefaultProject("Diploma Project");
        ProjectDirectory = projectPathService.BuildProjectDirectory(CurrentProject.Name);
        _exportOutputPath = Path.Combine(ProjectDirectory, "Exports", $"{CurrentProject.Name}.mp4");
        _browseExportOutputPathCommand = new RelayCommand(BrowseExportOutputPath);
        _exportProjectCommand = new RelayCommand(ExportProject, CanExportProject);
        _insertSelectedMediaToTimelineCommand = new RelayCommand(InsertSelectedMediaToTimeline, CanInsertSelectedMediaToTimeline);
        _splitSelectedClipCommand = new RelayCommand(SplitSelectedClip, CanSplitSelectedClip);
        _playPreviewCommand = new RelayCommand(PlayPreview, CanPlayPreview);
        _pausePreviewCommand = new RelayCommand(PausePreview, CanControlPreview);
        _clearTimelineSelectionCommand = new RelayCommand(ClearTimelineSelection, CanClearTimelineSelection);
        _deleteSelectedTimelineClipsCommand = new RelayCommand(DeleteSelectedTimelineClips, CanDeleteSelectedTimelineClips);
        _resetSelectedClipFrameCommand = new RelayCommand(ResetSelectedClipFrame, CanResetSelectedClipFrame);
        _resetSelectedClipTrimCommand = new RelayCommand(ResetSelectedClipTrim, CanResetSelectedClipTrim);
        _selectAllTimelineClipsCommand = new RelayCommand(SelectAllTimelineClips, CanSelectAllTimelineClips);
        _selectTimelineMoveToolCommand = new RelayCommand(SelectTimelineMoveTool);
        _selectTimelineSelectToolCommand = new RelayCommand(SelectTimelineSelectTool);
        _selectTimelineRazorToolCommand = new RelayCommand(SelectTimelineRazorTool);
        _stopPreviewCommand = new RelayCommand(StopPreview, CanControlPreview);
        _undoCommand = new RelayCommand(Undo, CanUndo);
        _redoCommand = new RelayCommand(Redo, CanRedo);
          _zoomInTimelineCommand = new RelayCommand(ZoomInTimeline, CanZoomInTimeline);
          _zoomOutTimelineCommand = new RelayCommand(ZoomOutTimeline, CanZoomOutTimeline);

        ImportedMediaView = CollectionViewSource.GetDefaultView(ImportedMedia);
        ImportedMediaView.Filter = FilterImportedMedia;

        ImportMediaCommand = new RelayCommand(ImportMedia);
        BrowseExportOutputPathCommand = _browseExportOutputPathCommand;
        ExportProjectCommand = _exportProjectCommand;
        InsertSelectedMediaToTimelineCommand = _insertSelectedMediaToTimelineCommand;
        SplitSelectedClipCommand = _splitSelectedClipCommand;
        PlayPreviewCommand = _playPreviewCommand;
        PausePreviewCommand = _pausePreviewCommand;
        StopPreviewCommand = _stopPreviewCommand;
        ClearTimelineSelectionCommand = _clearTimelineSelectionCommand;
        DeleteSelectedTimelineClipsCommand = _deleteSelectedTimelineClipsCommand;
        ResetSelectedClipFrameCommand = _resetSelectedClipFrameCommand;
        ResetSelectedClipTrimCommand = _resetSelectedClipTrimCommand;
        SelectAllTimelineClipsCommand = _selectAllTimelineClipsCommand;
        SelectTimelineMoveToolCommand = _selectTimelineMoveToolCommand;
        SelectTimelineSelectToolCommand = _selectTimelineSelectToolCommand;
        SelectTimelineRazorToolCommand = _selectTimelineRazorToolCommand;
        UndoCommand = _undoCommand;
          RedoCommand = _redoCommand;
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

        ImportMediaFiles(dialog.FileNames);
    }

    /// <summary>
    /// 	Импортирует медиафайлы из указанных путей.
    /// </summary>
    /// <param name="filePaths"> Пути к файлам для импорта. </param>
    public void ImportMediaFiles(IEnumerable<string> filePaths)
    {
        var requestedPaths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requestedPaths.Count == 0)
        {
            PreviewStatusText = "Import skipped: no files selected.";
            return;
        }

        var missingCount = requestedPaths.Count(path => !File.Exists(path));
        var unsupportedCount = requestedPaths.Count(path =>
            File.Exists(path) && !MediaFileFormats.IsSupportedExtension(Path.GetExtension(path)));

        IReadOnlyList<MediaAsset> imported;
        try
        {
            imported = _mediaImportService.Import(requestedPaths);
        }
        catch (Exception ex)
        {
            PreviewStatusText = $"Import failed: {BuildFriendlyErrorMessage(ex)}";
            return;
        }

        var addedCount = 0;
        var duplicateCount = 0;
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
                    var refreshedThumbnail = TryGetThumbnailPath(existingAsset);
                    if (!string.IsNullOrWhiteSpace(refreshedThumbnail))
                    {
                        var index = ImportedMedia.IndexOf(existingItem);
                        ImportedMedia[index] = new ImportedMediaItemViewModel(existingAsset, refreshedThumbnail);
                    }
                }

                duplicateCount++;
                continue;
            }

            CurrentProject.MediaAssets.Add(asset);
            var thumbnailPath = TryGetThumbnailPath(asset);
            ImportedMedia.Add(new ImportedMediaItemViewModel(asset, thumbnailPath));
            ImportedMediaView.Refresh();
            addedCount++;
        }

        PreviewStatusText = BuildImportStatusText(addedCount, duplicateCount, unsupportedCount, missingCount);
    }

    /// <summary>
    /// 	Текст поиска по проекту.
    /// </summary>
    public string ImportedMediaSearchText
    {
        get => _importedMediaSearchText;
        set
        {
            if (_importedMediaSearchText == value)
                return;

            _importedMediaSearchText = value;
            ImportedMediaView.Refresh();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 	Удаляет импортированный медиа-ассет и все связанные с ним клипы.
    /// </summary>
    /// <param name="assetId"> Идентификатор медиа-ассета. </param>
    public void RemoveImportedMedia(Guid assetId)
    {
        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == assetId);
        if (asset is null)
            return;

        SaveUndoSnapshot();
        var relatedClipIds = CurrentProject.Tracks
            .SelectMany(track => track.Clips)
            .Where(clip => clip.MediaAssetId == assetId)
            .Select(clip => clip.Id)
            .ToHashSet();

        foreach (var track in CurrentProject.Tracks)
        {
            var clipsToRemove = track.Clips
                .Where(clip => clip.MediaAssetId == assetId)
                .ToList();

            foreach (var clip in clipsToRemove)
                track.Clips.Remove(clip);
        }

        CurrentProject.MediaAssets.Remove(asset);

        var importedItem = ImportedMedia.FirstOrDefault(x => x.Asset.Id == assetId);
        if (importedItem is not null)
            ImportedMedia.Remove(importedItem);
        ImportedMediaView.Refresh();

        if (SelectedImportedMedia?.Asset.Id == assetId)
            SelectedImportedMedia = null;

        if ((_previewClipId is not null && relatedClipIds.Contains(_previewClipId.Value)) ||
            (_selectedVisualClip is not null && relatedClipIds.Contains(_selectedVisualClip.Id)) ||
            (_selectedAudioClip is not null && relatedClipIds.Contains(_selectedAudioClip.Id)))
        {
            ClearPreviewAfterDeletedClip();
        }

        RefreshTimelinePresentation();
    }

    /// <summary>
    /// 	Считает количество клипов на таймлайне, связанных с медиа-ассетом.
    /// </summary>
    /// <param name="assetId"> Идентификатор медиа-ассета. </param>
    /// <returns> Количество связанных клипов. </returns>
    public int GetImportedMediaUsageCount(Guid assetId)
    {
        return CurrentProject.Tracks
            .SelectMany(track => track.Clips)
            .Count(clip => clip.MediaAssetId == assetId);
    }

    private bool CanInsertSelectedMediaToTimeline()
    {
        return SelectedImportedMedia is not null;
    }

    private bool FilterImportedMedia(object item)
    {
        if (item is not ImportedMediaItemViewModel media)
            return false;

        if (string.IsNullOrWhiteSpace(ImportedMediaSearchText))
            return true;

        var query = ImportedMediaSearchText.Trim();
        return media.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               media.TypeLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               media.Asset.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private string? TryGetThumbnailPath(MediaAsset asset)
    {
        try
        {
            return _mediaThumbnailService.GetThumbnailPath(asset);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildImportStatusText(int addedCount, int duplicateCount, int unsupportedCount, int missingCount)
    {
        var parts = new List<string>();
        if (addedCount > 0)
            parts.Add($"imported {addedCount}");

        if (duplicateCount > 0)
            parts.Add($"duplicates {duplicateCount}");

        if (unsupportedCount > 0)
            parts.Add($"unsupported {unsupportedCount}");

        if (missingCount > 0)
            parts.Add($"missing {missingCount}");

        return parts.Count == 0
            ? "Import skipped: no supported media files found."
            : $"Import: {string.Join(", ", parts)}.";
    }

    private static string BuildFriendlyErrorMessage(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => "access to the file or folder was denied.",
            DirectoryNotFoundException => "target folder was not found.",
            FileNotFoundException => "one of the media files was not found.",
            IOException => string.IsNullOrWhiteSpace(ex.Message) ? "file operation failed." : ex.Message,
            InvalidOperationException => string.IsNullOrWhiteSpace(ex.Message) ? "operation could not be completed." : ex.Message,
            _ => string.IsNullOrWhiteSpace(ex.Message) ? "unexpected error." : ex.Message
        };
    }

    private bool CanExportProject()
    {
        return !IsExporting &&
            !string.IsNullOrWhiteSpace(ExportOutputPath) &&
            CurrentProject.Tracks
                .Where(x => x.IsEnabled)
                .SelectMany(x => x.Clips)
                .Any();
    }

    private void BrowseExportOutputPath()
    {
        var extension = ResolveExportFileExtension();
        var dialog = new SaveFileDialog
        {
            Filter = ResolveExportFileDialogFilter(),
            FileName = string.IsNullOrWhiteSpace(ExportOutputPath)
                ? $"{CurrentProject.Name}.{extension}"
                : Path.GetFileName(ExportOutputPath),
            InitialDirectory = ResolveInitialExportDirectory(),
            AddExtension = true,
            DefaultExt = $".{extension}",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
            return;

        ExportOutputPath = dialog.FileName;
    }

    private async void ExportProject()
    {
        if (IsExporting)
            return;

        try
        {
            var validationError = ValidateExportRequest();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                PreviewStatusText = validationError;
                return;
            }

            if (UseCustomExportRange && ExportRangeEndSeconds <= ExportRangeStartSeconds)
            {
                PreviewStatusText = "Export failed: end time must be greater than start time.";
                return;
            }

            IsExporting = true;
            ExportProgressPercent = 0;
            ExportProgressText = "Export: 0%";
            PreviewStatusText = "Export started...";
            var outputDirectory = Path.GetDirectoryName(ExportOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var options = new TimelineExportOptions
            {
                Width = ExportWidth,
                Height = ExportHeight,
                FrameRate = ExportFrameRate,
                RangeStart = UseCustomExportRange ? TimeSpan.FromSeconds(ExportRangeStartSeconds) : TimeSpan.Zero,
                RangeEnd = UseCustomExportRange ? TimeSpan.FromSeconds(ExportRangeEndSeconds) : null,
                ConstantRateFactor = ExportConstantRateFactor,
                VideoCodec = ExportVideoCodec,
                Preset = ExportPreset,
                AudioBitrateKbps = ExportAudioBitrateKbps
            };
            var progress = new Progress<TimelineExportProgress>(UpdateExportProgress);
            await _timelineExportService.ExportAsync(CurrentProject, ExportOutputPath, options, progress);
            ExportProgressPercent = 100;
            ExportProgressText = "Export: 100%";
            PreviewStatusText = $"Export finished: {ExportOutputPath}";
        }
        catch (Exception ex)
        {
            ExportProgressText = "Export failed.";
            PreviewStatusText = $"Export failed: {BuildFriendlyErrorMessage(ex)}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private string? ValidateExportRequest()
    {
        if (string.IsNullOrWhiteSpace(ExportOutputPath))
            return "Export failed: choose an output file first.";

        var enabledClips = CurrentProject.Tracks
            .Where(track => track.IsEnabled)
            .SelectMany(track => track.Clips)
            .ToList();
        if (enabledClips.Count == 0)
            return "Export failed: timeline has no enabled clips.";

        foreach (var clip in enabledClips)
        {
            var asset = CurrentProject.MediaAssets.FirstOrDefault(item => item.Id == clip.MediaAssetId);
            if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
                return "Export failed: one timeline media file is missing.";

            if (!File.Exists(asset.FilePath))
                return $"Export failed: source file not found ({Path.GetFileName(asset.FilePath)}).";
        }

        return null;
    }

    private string? ResolveInitialExportDirectory()
    {
        var outputDirectory = Path.GetDirectoryName(ExportOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory))
            return outputDirectory;

        return Directory.Exists(ProjectDirectory) ? ProjectDirectory : null;
    }

    private void UpdateExportOutputPathExtension()
    {
        if (string.IsNullOrWhiteSpace(ExportOutputPath))
            return;

        ExportOutputPath = Path.ChangeExtension(ExportOutputPath, ResolveExportFileExtension());
    }

    private string ResolveExportFileExtension()
    {
        return ExportFileType switch
        {
            "MOV" => "mov",
            "MKV" => "mkv",
            _ => "mp4"
        };
    }

    private string ResolveExportFileDialogFilter()
    {
        return ExportFileType switch
        {
            "MOV" => "QuickTime video (*.mov)|*.mov",
            "MKV" => "Matroska video (*.mkv)|*.mkv",
            _ => "MP4 video (*.mp4)|*.mp4"
        };
    }

    private void UpdateExportProgress(TimelineExportProgress progress)
    {
        ExportProgressPercent = progress.Percent;
        ExportProgressText = $"Export: {progress.Percent:0}% ({progress.RenderedDuration:mm\\:ss})";
        PreviewStatusText = ExportProgressText;
    }

    private void InsertSelectedMediaToTimeline()
    {
        var asset = SelectedImportedMedia?.Asset;
        if (asset is null)
            return;

        InsertMediaToTimeline(asset, ResolveAppendStartForAsset(asset));
    }

    public void InsertMediaToTimeline(MediaAsset asset, double targetLeft)
    {
        var snappedLeft = targetLeft <= 24 ? 0 : targetLeft;
        var timelineStart = TimeSpan.FromSeconds(Math.Max(0, snappedLeft / CurrentTimelinePixelsPerSecond));
        InsertMediaToTimeline(asset, timelineStart);
    }

    private TimelineClip InsertMediaToTimeline(MediaAsset asset, TimeSpan timelineStart)
    {
        SaveUndoSnapshot();
        TimelineClip insertedClip;

        if (asset.Type == Domain.Enums.MediaType.Audio)
        {
            var audioTrack = EnsureTrack("Audio");
            insertedClip = AddClipToTrack(audioTrack, asset, timelineStart, ResolveClipDuration(asset));
        }
        else if (asset.Type == Domain.Enums.MediaType.Image)
        {
            var videoTrack = EnsureTrack("Video");
            insertedClip = AddClipToTrack(videoTrack, asset, timelineStart, ResolveClipDuration(asset));
        }
        else
        {
            insertedClip = InsertVideoWithAudio(asset, timelineStart);
        }

        FitTimelineZoomForLongContent();
        RefreshTimelinePresentation();
        SelectClipForPreviewIfNone(insertedClip.Id);
        return insertedClip;
    }

    private TimelineClip InsertVideoWithAudio(MediaAsset asset, TimeSpan timelineStart)
    {
        var videoTrack = EnsureTrack("Video");
        var audioTrack = EnsureTrack("Audio");
        var duration = ResolveClipDuration(asset);
        var linkedGroupId = Guid.NewGuid();

        var videoClip = AddClipToTrack(videoTrack, asset, timelineStart, duration, linkedGroupId);
        AddClipToTrack(audioTrack, asset, timelineStart, duration, linkedGroupId);
        return videoClip;
    }

    private TimeSpan ResolveAppendStartForAsset(MediaAsset asset)
    {
        return asset.Type == Domain.Enums.MediaType.Audio
            ? ResolveAppendStart(EnsureTrack("Audio"))
            : ResolveAppendStart(EnsureTrack("Video"));
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

        clip.CaptureTrimBaseline();
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
        SaveUndoSnapshot();
        CurrentProject.Tracks.Add(new TimelineTrack
        {
            Name = BuildTrackName("Video", GetNextTrackIndex("Video"))
        });

        RefreshTimelinePresentation();
    }

    private void AddAudioTrack()
    {
        SaveUndoSnapshot();
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
        return _timelineZoomIndex < TimelineZoomMaxIndex;
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

    private void FitTimelineZoomForLongContent()
    {
        var fittedIndex = ResolveTimelineFitZoomIndex();
        var maxIndex = ResolveTimelineZoomMaxIndex();
        var nextIndex = Math.Min(fittedIndex, maxIndex);
        if (nextIndex >= _timelineZoomIndex)
        {
            if (_timelineZoomIndex <= maxIndex)
                return;

            nextIndex = maxIndex;
        }

        _timelineZoomIndex = nextIndex;
        OnPropertyChanged(nameof(TimelineZoomIndex));
        OnPropertyChanged(nameof(TimelineZoomMaxIndex));
        OnPropertyChanged(nameof(TimelineScaleLabel));
    }

    private void BuildTimelineRuler()
    {
        TimelineRulerMarks.Clear();
        var canvasDuration = ResolveTimelineCanvasDuration();
        var totalSeconds = Math.Max(1, (int)Math.Ceiling(canvasDuration.TotalSeconds));

        if (CurrentTimelinePixelsPerSecond >= 480)
        {
            var totalFrames = totalSeconds * TimelineFrameRate;
            var frameStep = ResolveTimelineFrameTickStep(totalFrames);
            for (var frame = 0; frame <= totalFrames; frame += frameStep)
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

        var tickStep = ResolveTimelineSecondTickStep(totalSeconds);
        var labelStep = ResolveTimelineLabelStep(tickStep);

        for (var second = 0; second <= totalSeconds; second += tickStep)
        {
            var isMajor = second % Math.Max(tickStep, labelStep) == 0;
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
                Title = track.Name,
                IsEnabled = track.IsEnabled,
                IsVideoTrack = IsTrackOfKind(track, "Video"),
                ToggleTrackEnabledCommand = new RelayCommand(() => ToggleTrackEnabled(track.Name))
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
                    Width = Math.Max(4, clip.SourceDuration.TotalSeconds * CurrentTimelinePixelsPerSecond),
                    IsSelected = _selectedTimelineClipIds.Contains(clip.Id),
                    IsLinkedClip = clip.LinkedGroupId is not null,
                    IsDragging = _draggedTimelineClipIds.Contains(clip.Id),
                    WaveformPeaks = IsTrackOfKind(track, "Audio")
                        ? ResolveClipWaveformPeaks(asset, clip)
                        : Array.Empty<double>()
                });
            }

            if (IsTrackOfKind(track, "Video"))
                VideoTimelineTracks.Insert(0, trackItem);
            else
                AudioTimelineTracks.Add(trackItem);
        }

        OnPropertyChanged(nameof(TimelineCanvasWidth));
    }

    private void UpdateTimelineClipDragState()
    {
        foreach (var track in VideoTimelineTracks.Concat(AudioTimelineTracks))
        {
            foreach (var clip in track.Clips)
                clip.IsDragging = _draggedTimelineClipIds.Contains(clip.ClipId);
        }
    }

    private IReadOnlyList<double> ResolveClipWaveformPeaks(MediaAsset asset, TimelineClip clip)
    {
        var waveform = _audioWaveformService.GetWaveform(asset, ProjectDirectory);
        if (waveform.Peaks.Count == 0 || asset.Duration <= TimeSpan.Zero)
            return Array.Empty<double>();

        var startRatio = Math.Clamp(clip.SourceStart.TotalSeconds / asset.Duration.TotalSeconds, 0, 1);
        var endRatio = Math.Clamp((clip.SourceStart + clip.SourceDuration).TotalSeconds / asset.Duration.TotalSeconds, startRatio, 1);
        var startIndex = Math.Min(waveform.Peaks.Count - 1, (int)Math.Floor(startRatio * waveform.Peaks.Count));
        var endIndex = Math.Min(waveform.Peaks.Count, Math.Max(startIndex + 1, (int)Math.Ceiling(endRatio * waveform.Peaks.Count)));
        var count = endIndex - startIndex;

        if (count <= 0)
            return Array.Empty<double>();

        var peaks = new double[count];
        for (var i = 0; i < count; i++)
            peaks[i] = waveform.Peaks[startIndex + i];

        return peaks;
    }

    private int ResolveTimelineSecondTickStep(int totalSeconds)
    {
        var tickStep = Math.Max(1, (int)Math.Ceiling(12 / CurrentTimelinePixelsPerSecond));
        while (totalSeconds / tickStep > 5000)
            tickStep *= 2;

        return NormalizeTimelineStep(tickStep);
    }

    private int ResolveTimelineFrameTickStep(int totalFrames)
    {
        var frameStep = CurrentTimelinePixelsPerSecond >= 960 ? 1 : 5;
        while (totalFrames / frameStep > 9000)
            frameStep *= 2;

        return frameStep;
    }

    public void BeginTimelineClipDrag(Guid clipId)
    {
        _draggedTimelineClipIds.Clear();
        _draggedTimelineClipId = clipId;

        if (_selectedTimelineClipIds.Contains(clipId) && _selectedTimelineClipIds.Count > 1)
        {
            foreach (var selectedClipId in _selectedTimelineClipIds)
            {
                _draggedTimelineClipIds.Add(selectedClipId);

                var selectedClipInfo = FindClipWithTrack(selectedClipId);
                if (selectedClipInfo is null)
                    continue;

                foreach (var linkedClipId in FindLinkedClipIds(selectedClipInfo.Value.Clip))
                    _draggedTimelineClipIds.Add(linkedClipId);
            }
        }
        else
        {
            _draggedTimelineClipIds.Add(clipId);

            var clipInfo = FindClipWithTrack(clipId);
            if (clipInfo is not null)
            {
                foreach (var linkedClipId in FindLinkedClipIds(clipInfo.Value.Clip))
                    _draggedTimelineClipIds.Add(linkedClipId);
            }
        }

        UpdateTimelineClipDragState();
    }

    public void EndTimelineClipDrag(Guid clipId)
    {
        if (_draggedTimelineClipId != clipId)
            return;

        _draggedTimelineClipId = null;
        _draggedTimelineClipIds.Clear();
        UpdateTimelineClipDragState();
    }

    public ClipDragPlacement ResolveClipDragPlacement(string targetTrackName, Guid clipId, double desiredLeft)
    {
        var sourceTrack = CurrentProject.Tracks.FirstOrDefault(x => x.Clips.Any(c => c.Id == clipId));
        if (sourceTrack is null)
            return new ClipDragPlacement(Math.Max(0, desiredLeft), false);

        var clip = sourceTrack.Clips.FirstOrDefault(x => x.Id == clipId);
        if (clip is null)
            return new ClipDragPlacement(Math.Max(0, desiredLeft), false);

        var targetTrack = CurrentProject.Tracks.FirstOrDefault(x =>
            x.Name.Equals(targetTrackName, StringComparison.OrdinalIgnoreCase));
        if (targetTrack is null || !AreTracksSameKind(sourceTrack, targetTrack))
            return new ClipDragPlacement(Math.Max(0, desiredLeft), false);

        var movingClipIds = ResolveMovingClipIds(clipId);
        var movingClipsOnTargetTrack = targetTrack.Clips
            .Where(x => movingClipIds.Contains(x.Id))
            .ToList();

        if (movingClipsOnTargetTrack.Count == 0)
            movingClipsOnTargetTrack.Add(clip);

        var groupStartOffset = movingClipsOnTargetTrack.Min(x => x.TimelineStart) - clip.TimelineStart;
        var groupEndOffset = movingClipsOnTargetTrack.Max(x => x.TimelineEnd) - clip.TimelineStart;
        var groupDuration = groupEndOffset - groupStartOffset;

        var desiredStart = desiredLeft <= 24
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Max(0, desiredLeft / CurrentTimelinePixelsPerSecond));
        var snappedClipStart = SnapTimelineTime(desiredStart);
        var snappedGroupStart = snappedClipStart + groupStartOffset;
        if (snappedGroupStart < TimeSpan.Zero)
            snappedGroupStart = TimeSpan.Zero;

        var snapThreshold = TimeSpan.FromSeconds(Math.Clamp(18 / CurrentTimelinePixelsPerSecond, 0.18, 0.55));

        var bestGroupStart = snappedGroupStart;
        var bestDistance = double.MaxValue;
        var isAnchored = false;
        var candidates = new List<TimeSpan> { snappedGroupStart };

        foreach (var otherClip in targetTrack.Clips.Where(x => !movingClipIds.Contains(x.Id)))
        {
            candidates.Add(otherClip.TimelineEnd);
            candidates.Add(otherClip.TimelineStart - groupDuration);
        }

        foreach (var candidate in candidates)
        {
            if (candidate < TimeSpan.Zero)
                continue;

            var distance = Math.Abs((candidate - snappedGroupStart).TotalSeconds);
            if (distance > snapThreshold.TotalSeconds || distance >= bestDistance)
                continue;

            bestGroupStart = candidate;
            bestDistance = distance;
            isAnchored = !candidate.Equals(snappedGroupStart);
        }

        var boundedGroupStart = ResolveNonOverlappingClipStart(targetTrack, movingClipIds, bestGroupStart, groupDuration);
        if (boundedGroupStart != bestGroupStart)
        {
            bestGroupStart = boundedGroupStart;
            isAnchored = true;
        }

        var bestClipStart = bestGroupStart - groupStartOffset;
        return new ClipDragPlacement(Math.Max(0, bestClipStart.TotalSeconds * CurrentTimelinePixelsPerSecond), isAnchored);
    }

    private HashSet<Guid> ResolveMovingClipIds(Guid clipId)
    {
        var movingClipIds = new HashSet<Guid>();
        if (_selectedTimelineClipIds.Contains(clipId) && _selectedTimelineClipIds.Count > 1)
        {
            foreach (var selectedClipId in _selectedTimelineClipIds)
                movingClipIds.Add(selectedClipId);
        }
        else
        {
            movingClipIds.Add(clipId);
        }

        foreach (var movingClipId in movingClipIds.ToList())
        {
            var clipInfo = FindClipWithTrack(movingClipId);
            if (clipInfo is null)
                continue;

            foreach (var linkedClipId in FindLinkedClipIds(clipInfo.Value.Clip))
                movingClipIds.Add(linkedClipId);
        }

        return movingClipIds;
    }

    private static TimeSpan ResolveNonOverlappingClipStart(
        TimelineTrack targetTrack,
        Guid clipId,
        TimeSpan desiredStart,
        TimeSpan duration)
    {
        return ResolveNonOverlappingClipStart(targetTrack, new HashSet<Guid> { clipId }, desiredStart, duration);
    }

    private static TimeSpan ResolveNonOverlappingClipStart(
        TimelineTrack targetTrack,
        IReadOnlySet<Guid> movingClipIds,
        TimeSpan desiredStart,
        TimeSpan duration)
    {
        var boundedStart = desiredStart;

        foreach (var otherClip in targetTrack.Clips
                     .Where(x => !movingClipIds.Contains(x.Id))
                     .OrderBy(x => x.TimelineStart))
        {
            var boundedEnd = boundedStart + duration;
            if (boundedEnd <= otherClip.TimelineStart || boundedStart >= otherClip.TimelineEnd)
                continue;

            var beforeOtherClip = otherClip.TimelineStart - duration;
            var afterOtherClip = otherClip.TimelineEnd;

            if (beforeOtherClip < TimeSpan.Zero)
            {
                boundedStart = afterOtherClip;
                continue;
            }

            boundedStart = Math.Abs((desiredStart - beforeOtherClip).TotalSeconds) <=
                Math.Abs((desiredStart - afterOtherClip).TotalSeconds)
                    ? beforeOtherClip
                    : afterOtherClip;
        }

        return boundedStart < TimeSpan.Zero ? TimeSpan.Zero : boundedStart;
    }

    public double ResolveSnappedClipLeft(string targetTrackName, Guid clipId, double desiredLeft)
    {
        return ResolveClipDragPlacement(targetTrackName, clipId, desiredLeft).Left;
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

    private static bool IsEnabledTrackOfKind(TimelineTrack track, string kind)
    {
        return track.IsEnabled && IsTrackOfKind(track, kind);
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

    private static string NormalizeExportPreset(string value)
    {
        var normalizedValue = value.Trim().ToLowerInvariant();
        return normalizedValue is "ultrafast" or "superfast" or "veryfast" or "faster" or "fast" or "medium" or "slow"
            ? normalizedValue
            : "medium";
    }

    private static string NormalizeExportVideoCodec(string value)
    {
        var normalizedValue = value.Trim().ToUpperInvariant();
        return normalizedValue switch
        {
            "H.265" or "HEVC" or "H265" => "H.265",
            "H.264 NVENC" or "H264 NVENC" or "H.264 (NVIDIA NVENC)" => "H.264 NVENC",
            "H.265 NVENC" or "HEVC NVENC" or "H265 NVENC" or "H.265 (NVIDIA NVENC)" => "H.265 NVENC",
            "H.264 AMF" or "H264 AMF" or "H.264 (AMD AMF)" => "H.264 AMF",
            "H.265 AMF" or "HEVC AMF" or "H265 AMF" or "H.265 (AMD AMF)" => "H.265 AMF",
            "H.264 QSV" or "H264 QSV" or "H.264 (INTEL QSV)" => "H.264 QSV",
            "H.265 QSV" or "HEVC QSV" or "H265 QSV" or "H.265 (INTEL QSV)" => "H.265 QSV",
            "MPEG-4" or "MPEG4" => "MPEG-4",
            _ => "H.264"
        };
    }

    private static string NormalizeExportFileType(string value)
    {
        var normalizedValue = value.Trim().ToUpperInvariant();
        return normalizedValue is "MOV" or "MKV" ? normalizedValue : "MP4";
    }

    private void ApplyExportResolutionPreset(string preset)
    {
        switch (preset)
        {
            case "3840x2160":
                ExportWidth = 3840;
                ExportHeight = 2160;
                break;
            case "2560x1440":
                ExportWidth = 2560;
                ExportHeight = 1440;
                break;
            case "1280x720":
                ExportWidth = 1280;
                ExportHeight = 720;
                break;
            case "1080x1920":
                ExportWidth = 1080;
                ExportHeight = 1920;
                break;
            case "Custom":
                break;
            default:
                ExportWidth = 1920;
                ExportHeight = 1080;
                break;
        }
    }

    private static string NormalizeExportResolutionPreset(string value)
    {
        return value.Trim() switch
        {
            "3840x2160" => "3840x2160",
            "2560x1440" => "2560x1440",
            "1280x720" => "1280x720",
            "1080x1920" => "1080x1920",
            "Custom" => "Custom",
            _ => "1920x1080"
        };
    }

    private int ResolveTimelineLabelStep(int tickStep)
    {
        var labelStep = Math.Max(tickStep, (int)Math.Ceiling(82 / CurrentTimelinePixelsPerSecond));
        return NormalizeTimelineStep(labelStep);
    }

    private static int NormalizeTimelineStep(int step)
    {
        if (step <= 1)
            return 1;

        var normalizedSteps = new[]
        {
            2, 5, 10, 15, 30,
            60, 120, 300, 600, 900,
            1800, 3600, 7200
        };

        return normalizedSteps.FirstOrDefault(x => x >= step) is var normalized && normalized > 0
            ? normalized
            : step;
    }

    private static string FormatTimecodeLabel(int second, int frame = 0)
    {
        var time = TimeSpan.FromSeconds(second);
        return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}:{frame:D2}";
    }

    private static string FormatSecondsLabel(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.Hours > 0
            ? $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    private static TimeSpan SnapTimelineTime(TimeSpan time)
    {
        return TimeSpan.FromSeconds(SnapTimelineSeconds(time.TotalSeconds));
    }

    private static double SnapTimelineSeconds(double seconds)
    {
        return Math.Round(seconds * TimelineFrameRate, MidpointRounding.AwayFromZero) / TimelineFrameRate;
    }

    private static bool IsTimelinePositionInsideClip(TimelineClip clip, TimeSpan position)
    {
        return clip.TimelineStart <= position &&
            clip.TimelineEnd > position;
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
        var snappedLeft = ResolveSnappedClipLeft(targetTrackName, clipId, left);
        var newStart = TimeSpan.FromSeconds(Math.Max(0, snappedLeft / CurrentTimelinePixelsPerSecond));
        if (sourceTrack == targetTrack &&
            Math.Abs((clip.TimelineStart - newStart).TotalSeconds) < 0.001)
            return;

        SaveUndoSnapshot();
        var selectedClipIdsToMove = new HashSet<Guid>(_selectedTimelineClipIds);
        if (selectedClipIdsToMove.Count == 0 || !selectedClipIdsToMove.Contains(clipId))
            selectedClipIdsToMove.Add(clipId);

        foreach (var selectedClipId in selectedClipIdsToMove.ToList())
        {
            var selectedClipInfo = FindClipWithTrack(selectedClipId);
            if (selectedClipInfo is null)
                continue;

            foreach (var linkedClipId in FindLinkedClipIds(selectedClipInfo.Value.Clip))
                selectedClipIdsToMove.Add(linkedClipId);
        }

        var clipsToMove = selectedClipIdsToMove
            .Select(FindClipWithTrack)
            .Where(x => x is not null)
            .Select(x => x!.Value.Clip)
            .DistinctBy(x => x.Id)
            .ToList();

        if (clipsToMove.Count > 1)
        {
            var delta = newStart - oldStart;
            var earliestStart = clipsToMove.Min(x => x.TimelineStart);
            if (earliestStart + delta < TimeSpan.Zero)
                delta = -earliestStart;

            foreach (var selectedClip in clipsToMove)
                selectedClip.TimelineStart += delta;

            RefreshTimelineTracks();
            RefreshCurrentTimelinePreviewAfterEdit();
            return;
        }

        if (!ReferenceEquals(sourceTrack, targetTrack))
        {
            sourceTrack.Clips.Remove(clip);
            targetTrack.Clips.Add(clip);
        }

        clip.TimelineStart = newStart;
        MoveLinkedClip(clip, newStart);
        RefreshTimelineTracks();
        RefreshCurrentTimelinePreviewAfterEdit();
    }

    public bool TrimClipEdge(Guid clipId, bool trimLeftEdge, double canvasLeft, bool saveUndo = true)
    {
        var clipInfo = FindClipWithTrack(clipId);
        if (clipInfo is null)
            return false;

        var (track, clip) = clipInfo.Value;
        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
        if (asset is null)
            return false;

        var minDuration = TimeSpan.FromSeconds(1.0 / TimelineFrameRate);
        var targetPosition = SnapTimelineTime(TimeSpan.FromSeconds(Math.Max(0, canvasLeft / CurrentTimelinePixelsPerSecond)));
        var relatedClips = ResolveLinkedTimelineClips(track, clip).ToList();
        var relatedIds = relatedClips.Select(x => x.Clip.Id).ToHashSet();
        var previousBoundary = ResolvePreviousClipBoundary(relatedClips, relatedIds);
        var nextBoundary = ResolveNextClipBoundary(relatedClips, relatedIds);
        var oldStart = clip.TimelineStart;
        var oldEnd = clip.TimelineEnd;
        var oldSourceStart = clip.SourceStart;
        var oldSourceDuration = clip.SourceDuration;
        var sourceLimit = asset.Duration > TimeSpan.Zero
            ? asset.Duration
            : oldSourceStart + oldSourceDuration;

        TimeSpan newStart;
        TimeSpan newSourceStart;
        TimeSpan newSourceDuration;

        if (trimLeftEdge)
        {
            var earliestFromSource = oldStart - oldSourceStart;
            if (earliestFromSource < TimeSpan.Zero)
                earliestFromSource = TimeSpan.Zero;

            var minimumStart = MaxTimeSpan(TimeSpan.Zero, previousBoundary, earliestFromSource);
            var maximumStart = oldEnd - minDuration;
            if (maximumStart < minimumStart)
                maximumStart = minimumStart;

            newStart = ClampTimeSpan(targetPosition, minimumStart, maximumStart);
            var delta = newStart - oldStart;
            newSourceStart = oldSourceStart + delta;
            newSourceDuration = oldSourceDuration - delta;
        }
        else
        {
            var maximumEnd = oldStart + (sourceLimit - oldSourceStart);
            if (nextBoundary is not null && nextBoundary.Value < maximumEnd)
                maximumEnd = nextBoundary.Value;

            var minimumEnd = oldStart + minDuration;
            if (maximumEnd < minimumEnd)
                maximumEnd = minimumEnd;

            var newEnd = ClampTimeSpan(targetPosition, minimumEnd, maximumEnd);
            newStart = oldStart;
            newSourceStart = oldSourceStart;
            newSourceDuration = newEnd - oldStart;
        }

        newSourceStart = SnapTimelineTime(newSourceStart);
        newSourceDuration = SnapTimelineTime(newSourceDuration);
        newStart = SnapTimelineTime(newStart);

        if (newSourceDuration < minDuration)
            newSourceDuration = minDuration;

        var isSameTrim =
            Math.Abs((clip.TimelineStart - newStart).TotalSeconds) < 0.001 &&
            Math.Abs((clip.SourceStart - newSourceStart).TotalSeconds) < 0.001 &&
            Math.Abs((clip.SourceDuration - newSourceDuration).TotalSeconds) < 0.001;

        if (isSameTrim)
            return false;

        if (saveUndo)
            SaveUndoSnapshot();

        foreach (var (_, relatedClip) in relatedClips)
        {
            if (trimLeftEdge)
                relatedClip.TimelineStart = newStart;

            relatedClip.SourceStart = newSourceStart;
            relatedClip.SourceDuration = newSourceDuration;
        }

        RefreshTimelinePresentation();
        RefreshSelectedClipTrimProperties();
        RefreshCurrentTimelinePreviewAfterEdit();
        PreviewStatusText = trimLeftEdge ? "Clip start trimmed." : "Clip end trimmed.";
        return true;
    }

    private bool CanSplitSelectedClip()
    {
        return FindClipToSplitAtCurrentPosition() is not null;
    }

    private void SplitSelectedClip()
    {
        var target = FindClipToSplitAtCurrentPosition();
        if (target is null)
            return;

        var (track, clip) = target.Value;
        var splitPosition = TimelinePlaybackPosition;
        if (splitPosition <= clip.TimelineStart || splitPosition >= clip.TimelineEnd)
            return;

        SaveUndoSnapshot();
        SplitClipGroup(track, clip, splitPosition);
    }

    public bool SplitClipAtTimelinePosition(Guid clipId, TimeSpan splitPosition)
    {
        var clipInfo = FindClipWithTrack(clipId);
        if (clipInfo is null)
            return false;

        var (track, clip) = clipInfo.Value;
        if (splitPosition <= clip.TimelineStart || splitPosition >= clip.TimelineEnd)
            return false;

        SaveUndoSnapshot();
        SplitClipGroup(track, clip, splitPosition);
        return true;
    }

    private void SplitClipGroup(TimelineTrack sourceTrack, TimelineClip clip, TimeSpan splitPosition)
    {
        splitPosition = SnapTimelineTime(splitPosition);

        var relatedClips = clip.LinkedGroupId is null
            ? new List<(TimelineTrack Track, TimelineClip Clip)> { (sourceTrack, clip) }
            : CurrentProject.Tracks
                .SelectMany(track => track.Clips.Select(existing => (Track: track, Clip: existing)))
                .Where(x => x.Clip.LinkedGroupId == clip.LinkedGroupId)
                .OrderBy(x => x.Track.Name)
                .ToList();

        var rightGroupId = relatedClips.Count > 1 ? Guid.NewGuid() : (Guid?)null;
        TimelineClip? selectedRightClip = null;

        foreach (var (track, currentClip) in relatedClips)
        {
            if (splitPosition <= currentClip.TimelineStart || splitPosition >= currentClip.TimelineEnd)
                continue;

            var offsetSeconds = (splitPosition - currentClip.TimelineStart).TotalSeconds;
            var leftDuration = TimeSpan.FromSeconds(offsetSeconds);
            var rightDuration = currentClip.SourceDuration - leftDuration;

            if (rightDuration <= TimeSpan.Zero || leftDuration <= TimeSpan.Zero)
                continue;

            currentClip.SourceDuration = leftDuration;
            currentClip.CaptureTrimBaseline();

            var rightClip = new TimelineClip
            {
                MediaAssetId = currentClip.MediaAssetId,
                LinkedGroupId = rightGroupId ?? currentClip.LinkedGroupId,
                SourceStart = currentClip.SourceStart + leftDuration,
                SourceDuration = rightDuration,
                TimelineStart = splitPosition,
                FrameX = currentClip.FrameX,
                FrameY = currentClip.FrameY,
                FrameScale = currentClip.FrameScale,
                AudioVolume = currentClip.AudioVolume
            };

            rightClip.CaptureTrimBaseline();
            track.Clips.Add(rightClip);

            if (ReferenceEquals(track, sourceTrack) && currentClip.Id == clip.Id)
                selectedRightClip = rightClip;
        }

        RefreshTimelinePresentation();

        if (selectedRightClip is not null)
        {
            SelectClipForPreview(selectedRightClip.Id);
        }
        else
        {
            RefreshCurrentTimelinePreviewAfterEdit();
        }

        PreviewStatusText = "Clip split.";
    }

    private (TimelineTrack Track, TimelineClip Clip)? FindClipToSplitAtCurrentPosition()
    {
        var selectedClipInfo = _selectedTimelineClipId is null
            ? null
            : FindClipWithTrack(_selectedTimelineClipId.Value);

        if (selectedClipInfo is not null)
        {
            var (_, selectedClip) = selectedClipInfo.Value;
            if (selectedClip.TimelineStart < TimelinePlaybackPosition && selectedClip.TimelineEnd > TimelinePlaybackPosition)
                return selectedClipInfo;
        }

        var clipAtPlayhead = FindVisualClipAt(TimelinePlaybackPosition);
        if (clipAtPlayhead is not null)
            return FindClipWithTrack(clipAtPlayhead.Id);

        var audioClipAtPlayhead = FindAudioClipAt(TimelinePlaybackPosition);
        if (audioClipAtPlayhead is not null)
            return FindClipWithTrack(audioClipAtPlayhead.Id);

        return null;
    }

    private bool ApplyTrimToClip(
        TimelineClip clip,
        MediaAsset asset,
        double startSeconds,
        double endSeconds,
        bool saveUndo = true,
        bool refreshAfterApply = true)
    {
        var clipDurationLimit = asset.Duration > TimeSpan.Zero
            ? asset.Duration.TotalSeconds
            : Math.Max((clip.SourceStart + clip.SourceDuration).TotalSeconds, endSeconds);

        var minDurationSeconds = 1.0 / TimelineFrameRate;
        var clampedStart = SnapTimelineSeconds(Math.Clamp(startSeconds, 0, Math.Max(0, clipDurationLimit - minDurationSeconds)));
        var clampedEnd = SnapTimelineSeconds(Math.Clamp(endSeconds, clampedStart + minDurationSeconds, clipDurationLimit));

        if (clampedEnd <= clampedStart)
            clampedEnd = Math.Min(clipDurationLimit, clampedStart + minDurationSeconds);

        var newSourceStart = TimeSpan.FromSeconds(clampedStart);
        var newSourceDuration = SnapTimelineTime(TimeSpan.FromSeconds(Math.Max(minDurationSeconds, clampedEnd - clampedStart)));

        var isSameTrim =
            Math.Abs((clip.SourceStart - newSourceStart).TotalSeconds) < 0.001 &&
            Math.Abs((clip.SourceDuration - newSourceDuration).TotalSeconds) < 0.001;

        if (isSameTrim)
            return false;

        if (saveUndo)
            SaveUndoSnapshot();

        clip.SourceStart = newSourceStart;
        clip.SourceDuration = newSourceDuration;

        foreach (var linkedClipId in FindLinkedClipIds(clip))
        {
            var linkedClipInfo = FindClipWithTrack(linkedClipId);
            if (linkedClipInfo is null)
                continue;

            linkedClipInfo.Value.Clip.SourceStart = newSourceStart;
            linkedClipInfo.Value.Clip.SourceDuration = newSourceDuration;
        }

        if (refreshAfterApply)
        {
            RefreshTimelinePresentation();
            RefreshSelectedClipTrimProperties();
            RefreshCurrentTimelinePreviewAfterEdit();
            PreviewStatusText = "Selected clip trimmed.";
        }

        return true;
    }

    private void ToggleTrackEnabled(string trackName)
    {
        var track = CurrentProject.Tracks.FirstOrDefault(x =>
            x.Name.Equals(trackName, StringComparison.OrdinalIgnoreCase));
        if (track is null)
            return;

        var wasTimelinePlaybackActive = _isTimelinePlaybackActive;
        SaveUndoSnapshot();
        track.IsEnabled = !track.IsEnabled;
        RefreshTimelinePresentation();

        if (IsTrackOfKind(track, "Video"))
        {
            if (wasTimelinePlaybackActive)
            {
                _isTimelinePlaybackActive = true;
                _previewTimelinePosition = TimelinePlaybackPosition;
                PlayTimelineFromPosition(TimelinePlaybackPosition);
            }
            else
            {
                LoadTimelinePreviewAtPosition(TimelinePlaybackPosition);
            }

            return;
        }

        ConfigureAudioForTimelinePosition(TimelinePlaybackPosition);

        if (wasTimelinePlaybackActive)
            RequestPreviewPlayback("SyncAudio");
    }

    public void DeleteClip(Guid clipId, bool deleteLinkedClips = true)
    {
        var clipInfo = FindClipWithTrack(clipId);
        if (clipInfo is null)
            return;

        SaveUndoSnapshot();
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

        _selectedTimelineClipIds.Remove(clipId);
        foreach (var linkedClipId in linkedClipIds)
            _selectedTimelineClipIds.Remove(linkedClipId);

        if (_previewClipId == clipId ||
            linkedClipIds.Contains(_previewClipId.GetValueOrDefault()) ||
            _selectedVisualClip?.Id == clipId ||
            linkedClipIds.Contains(_selectedVisualClip?.Id ?? Guid.Empty) ||
            _selectedAudioClip?.Id == clipId ||
            linkedClipIds.Contains(_selectedAudioClip?.Id ?? Guid.Empty))
            ClearPreviewAfterDeletedClip();

        RefreshTimelineSelectionCommandState();
        RefreshTimelinePresentation();
    }

    public void UnlinkClip(Guid clipId)
    {
        var clipInfo = FindClipWithTrack(clipId);
        if (clipInfo is null)
            return;

        SaveUndoSnapshot();
        var relatedClipIds = new HashSet<Guid> { clipId };
        foreach (var linkedClipId in FindLinkedClipIds(clipInfo.Value.Clip))
            relatedClipIds.Add(linkedClipId);

        foreach (var relatedClipId in relatedClipIds)
        {
            var linkedInfo = FindClipWithTrack(relatedClipId);
            if (linkedInfo is null)
                continue;

            var (track, clip) = linkedInfo.Value;
            var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
            if (asset is not null && asset.Type == Domain.Enums.MediaType.Video && IsTrackOfKind(track, "Video"))
                clip.MuteEmbeddedAudio = true;

            clip.LinkedGroupId = null;
        }

        RefreshTimelinePresentation();
        RefreshCurrentTimelinePreviewAfterEdit();
        PreviewStatusText = "Clip link removed.";
    }

    public void SetTimelinePlaybackPositionFromCanvasLeft(double canvasLeft)
    {
        var timelinePosition = TimeSpan.FromSeconds(Math.Max(0, canvasLeft / CurrentTimelinePixelsPerSecond));
        SetTimelinePlaybackPosition(timelinePosition);
    }

    private void SetTimelinePlaybackPosition(TimeSpan timelinePosition)
    {
        LoadTimelinePreviewAtPosition(timelinePosition);
    }

    public void SelectClipForPreview(Guid clipId, bool toggleSelection = false)
    {
        var clipInfo = FindClipWithTrack(clipId);
        if (clipInfo is null)
            return;

        var (track, clip) = clipInfo.Value;
        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
            return;

        var isAudioTrack = IsTrackOfKind(track, "Audio");
        if (toggleSelection)
        {
            if (!_selectedTimelineClipIds.Add(clip.Id))
                _selectedTimelineClipIds.Remove(clip.Id);
        }
        else
        {
            _selectedTimelineClipIds.Clear();
            _selectedTimelineClipIds.Add(clip.Id);
        }

        _selectedTimelineClipId = _selectedTimelineClipIds.Contains(clip.Id)
            ? clip.Id
            : _selectedTimelineClipIds.FirstOrDefault();
        if (_selectedTimelineClipId == Guid.Empty)
            _selectedTimelineClipId = null;

        RefreshActiveSelectionFromSelectedId();
        RefreshSelectedClipFrameProperties();
        RefreshSelectedAudioClipProperties();
        RefreshSelectedClipTrimProperties();
        RefreshTimelineTracks();
        RefreshTimelineSelectionCommandState();
        PreviewStatusText = SelectedTimelineClipCount > 1
            ? $"Selected {SelectedTimelineClipCount} timeline clip(s)."
            : isAudioTrack || asset.Type == Domain.Enums.MediaType.Audio
                ? "Audio clip selected."
                : "Clip selected.";
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
        RefreshPreviewVisualLayers(position, preserveExistingLayers: true);
    }

    public void CompletePreviewImageFrame()
    {
        if (!_isTimelinePlaybackActive)
            return;

        CompletePreviewPlayback();
    }

    public bool TryContinueTimelinePlayback()
    {
        if (!_isTimelinePlaybackActive || _previewClipId is null)
            return false;

        var currentClipInfo = FindClipWithTrack(_previewClipId.Value);
        if (currentClipInfo is null)
            return false;

        var currentClip = currentClipInfo.Value.Clip;
        var nextClip = FindNextTimelinePreviewClip();
        if (nextClip is null)
            return false;

        if (currentClip.MediaAssetId != nextClip.MediaAssetId)
            return false;

        var gap = nextClip.TimelineStart - currentClip.TimelineEnd;
        if (gap < -TimelineBoundaryTolerance || gap > TimelineBoundaryTolerance)
            return false;

        var sourceGap = nextClip.SourceStart - (currentClip.SourceStart + currentClip.SourceDuration);
        if (sourceGap < -TimelineBoundaryTolerance || sourceGap > TimelineBoundaryTolerance)
            return false;

        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == nextClip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
            return false;

        var nextSource = new Uri(asset.FilePath, UriKind.Absolute);
        var previousPreviewSource = PreviewMediaSource;

        _previewClipId = nextClip.Id;
        _previewBaseVisualClip = nextClip;
        _previewMediaType = asset.Type;
        _previewTimelinePosition = nextClip.TimelineStart;
        TimelinePlaybackPosition = nextClip.TimelineStart;
        PreviewGapDuration = TimeSpan.Zero;
        IsTimelineGapPreview = false;
        PreviewSourceStart = nextClip.SourceStart;
        PreviewSourceDuration = nextClip.SourceDuration;
        PreviewTitle = asset.DisplayName;
        IsPreviewVideoAudioMuted = ShouldMuteEmbeddedVideoAudio(nextClip, asset, nextClip.TimelineStart);
        PreviewVideoVolume = ResolveEmbeddedVideoAudioVolume(nextClip, asset, nextClip.TimelineStart);

        if (!Equals(previousPreviewSource, nextSource))
            PreviewMediaSource = nextSource;

        ConfigureAudioForTimelinePosition(nextClip.TimelineStart);
        RefreshPreviewVisualLayers(nextClip.TimelineStart, preserveExistingLayers: true);
        RefreshPreviewFrameProperties();
        RequestPreviewPlayback(asset.Type == Domain.Enums.MediaType.Image ? "ImageFrame" : "ContinuePlayback");
        return true;
    }

    public void FailPreviewPlayback(string? detail = null)
    {
        if (!HasPreviewMedia)
            return;

        PreviewStatusText = string.IsNullOrWhiteSpace(detail)
            ? "Preview failed: could not play this media file."
            : $"Preview failed: {detail}";
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
        _previewTimelinePosition = ResolvePlaybackStartPosition(TimelinePlaybackPosition);
        TimelinePlaybackPosition = _previewTimelinePosition;
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
        IsPreviewVideoAudioMuted = false;
        TimelinePlaybackPosition = TimeSpan.Zero;
        PreviewStatusText = "Preview stopped.";
        RequestPreviewPlayback("Stop");
    }

    private void PlayTimelineFromPosition(TimeSpan timelinePosition)
    {
        ConfigureAudioForTimelinePosition(timelinePosition);
        RefreshPreviewVisualLayers(timelinePosition, preserveExistingLayers: true);

        var visualClip = FindBaseVideoClipAt(timelinePosition) ?? FindVisualClipAt(timelinePosition);
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
            IsPreviewVideoAudioMuted = false;
            PreviewVisualLayers.Clear();
            PreviewTitle = "Timeline finished";
            PreviewStatusText = "Timeline playback finished.";
            TimelinePlaybackPosition = timelinePosition;
            RequestPreviewPlayback("Stop");
            return;
        }

        PlayTimelineGap(nextTimelineEvent.Value - timelinePosition, timelinePosition);
    }

    private void LoadTimelinePreviewAtPosition(TimeSpan timelinePosition)
    {
        _isTimelinePlaybackActive = false;
        _previewTimelinePosition = timelinePosition;
        ConfigureAudioForTimelinePosition(timelinePosition);
        RefreshPreviewVisualLayers(timelinePosition, preserveExistingLayers: true);

        var visualClip = FindBaseVideoClipAt(timelinePosition) ?? FindVisualClipAt(timelinePosition);
        if (visualClip is not null)
        {
            if (TryLoadPreviewClip(visualClip, timelinePosition, preserveExistingLayers: true))
            {
                PreviewStatusText = $"Timeline position set to {TimelinePlaybackPositionLabel}.";
                RequestPreviewPlayback("Seek");
                return;
            }

            _previewTimelinePosition = visualClip.TimelineEnd;
            LoadTimelinePreviewAtPosition(_previewTimelinePosition);
            return;
        }

        _previewClipId = null;
        _previewMediaType = null;
        _previewBaseVisualClip = null;
        IsTimelineGapPreview = true;
        PreviewGapDuration = TimeSpan.Zero;
        PreviewSourceStart = TimeSpan.Zero;
        PreviewSourceDuration = TimeSpan.Zero;
        PreviewMediaSource = null;
        IsPreviewVideoAudioMuted = false;
        PreviewTitle = "Timeline gap";
        TimelinePlaybackPosition = timelinePosition;
        RefreshSelectedClipFrameProperties();
        PreviewStatusText = $"Timeline position set to {TimelinePlaybackPositionLabel}.";
        RequestPreviewPlayback("Seek");
    }

    private void PlayTimelineGap(TimeSpan duration, TimeSpan timelinePosition)
    {
        _previewClipId = null;
        _previewMediaType = null;
        _previewBaseVisualClip = null;
        _previewTimelinePosition = timelinePosition;
        PreviewGapDuration = duration;
        IsTimelineGapPreview = true;
        PreviewMediaSource = null;
        IsPreviewVideoAudioMuted = false;
        PreviewTitle = "Timeline gap";
        RefreshSelectedClipFrameProperties();
        RefreshPreviewVisualLayers(timelinePosition);
        TimelinePlaybackPosition = timelinePosition;
        PreviewStatusText = $"Black screen for {duration.TotalSeconds:0.##} sec.";
        RequestPreviewPlayback("Gap");
    }

    private void SelectClipForTimelinePlayback(TimelineClip clip, TimeSpan timelinePosition)
    {
        if (!TryLoadPreviewClip(clip, timelinePosition, preserveExistingLayers: true))
        {
            _previewTimelinePosition = clip.TimelineEnd;
            PlayTimelineFromPosition(_previewTimelinePosition);
            return;
        }

        var asset = CurrentProject.MediaAssets.First(x => x.Id == clip.MediaAssetId);

        if (asset.Type == Domain.Enums.MediaType.Image)
        {
            PreviewStatusText = "Showing image on timeline.";
            RequestPreviewPlayback("ImageFrame");
            return;
        }

        PreviewStatusText = "Playing timeline.";
        RequestPreviewPlayback("SyncPlayback");
    }

    private bool TryLoadPreviewClip(
        TimelineClip clip,
        TimeSpan timelinePosition,
        bool preserveExistingLayers = false)
    {
        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
        {
            PreviewStatusText = "Preview failed: media asset is missing.";
            return false;
        }

        if (!File.Exists(asset.FilePath))
        {
            PreviewStatusText = $"Preview failed: source file not found ({Path.GetFileName(asset.FilePath)}).";
            return false;
        }

        var offsetInsideClip = timelinePosition - clip.TimelineStart;
        if (offsetInsideClip < TimeSpan.Zero)
            offsetInsideClip = TimeSpan.Zero;

        Uri nextSource;
        try
        {
            nextSource = new Uri(asset.FilePath, UriKind.Absolute);
        }
        catch (UriFormatException)
        {
            PreviewStatusText = $"Preview failed: invalid media path ({asset.DisplayName}).";
            return false;
        }

        var isSamePreviewClip = _previewClipId == clip.Id && Equals(PreviewMediaSource, nextSource);
        var isSamePreviewSource = Equals(PreviewMediaSource, nextSource);

        _previewClipId = clip.Id;
        _previewBaseVisualClip = clip;
        _previewMediaType = asset.Type;
        _previewTimelinePosition = timelinePosition;
        PreviewGapDuration = TimeSpan.Zero;
        IsTimelineGapPreview = false;
        PreviewSourceStart = clip.SourceStart + offsetInsideClip;
        PreviewSourceDuration = clip.SourceDuration - offsetInsideClip;
        var nextAudioEvent = ResolveNextTimelineEvent(
            timelinePosition,
            nextVisualClip: null,
            nextAudioClip: FindNextAudioClipAtOrAfter(timelinePosition));
        if (nextAudioEvent is not null &&
            nextAudioEvent.Value > timelinePosition + TimelineBoundaryTolerance &&
            nextAudioEvent.Value < clip.TimelineEnd - TimelineBoundaryTolerance)
            PreviewSourceDuration = nextAudioEvent.Value - timelinePosition;
        TimelinePlaybackPosition = timelinePosition;
        PreviewTitle = asset.DisplayName;
        IsPreviewVideoAudioMuted = ShouldMuteEmbeddedVideoAudio(clip, asset, timelinePosition);
        PreviewVideoVolume = ResolveEmbeddedVideoAudioVolume(clip, asset, timelinePosition);
        if (!isSamePreviewClip && !isSamePreviewSource)
            PreviewMediaSource = nextSource;

        OnPropertyChanged(nameof(IsPreviewImage));
        OnPropertyChanged(nameof(IsPreviewPlayableMedia));
        OnPropertyChanged(nameof(IsPreviewPlaceholderVisible));
        OnPropertyChanged(nameof(PreviewImageSource));
        OnPropertyChanged(nameof(PreviewPlayableMediaSource));
        RefreshPreviewFrameProperties();
        RefreshPreviewVisualLayers(timelinePosition, preserveExistingLayers);

        return true;
    }

    private bool ShouldMuteEmbeddedVideoAudio(TimelineClip visualClip, MediaAsset asset, TimeSpan timelinePosition)
    {
        if (asset.Type != Domain.Enums.MediaType.Video)
            return false;

        if (visualClip.MuteEmbeddedAudio)
            return true;

        return CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Audio"))
            .SelectMany(track => track.Clips.Select(clip => new { Track = track, Clip = clip }))
            .Any(x =>
                !x.Track.IsEnabled &&
                x.Clip.MediaAssetId == visualClip.MediaAssetId &&
                x.Clip.TimelineStart <= timelinePosition &&
                x.Clip.TimelineEnd > timelinePosition &&
                (visualClip.LinkedGroupId is null || x.Clip.LinkedGroupId == visualClip.LinkedGroupId));
    }

    private double ResolveEmbeddedVideoAudioVolume(TimelineClip visualClip, MediaAsset asset, TimeSpan timelinePosition)
    {
        if (asset.Type != Domain.Enums.MediaType.Video)
            return 1.0;

        if (visualClip.MuteEmbeddedAudio)
            return 0.0;

        var linkedAudioClip = CurrentProject.Tracks
            .Where(x => IsTrackOfKind(x, "Audio"))
            .SelectMany(track => track.Clips.Select(clip => new { Track = track, Clip = clip }))
            .Where(x =>
                x.Track.IsEnabled &&
                x.Clip.MediaAssetId == visualClip.MediaAssetId &&
                x.Clip.TimelineStart <= timelinePosition &&
                x.Clip.TimelineEnd > timelinePosition &&
                (visualClip.LinkedGroupId is null || x.Clip.LinkedGroupId == visualClip.LinkedGroupId))
            .Select(x => x.Clip)
            .FirstOrDefault();

        return linkedAudioClip?.AudioVolume ?? 1.0;
    }

    private TimelineClip? FindFirstPreviewableClip()
    {
        return CurrentProject.Tracks
            .Where(x => IsEnabledTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault()
            ?? CurrentProject.Tracks
                .Where(x => x.IsEnabled)
                .SelectMany(x => x.Clips)
                .OrderBy(x => x.TimelineStart)
                .FirstOrDefault();
    }

    private TimeSpan ResolvePlaybackStartPosition(TimeSpan requestedPosition)
    {
        if (FindVisualClipAt(requestedPosition) is not null ||
            FindAudioClipAt(requestedPosition) is not null ||
            FindTimelineClipAtOrAfter(requestedPosition) is not null ||
            FindNextAudioClipAtOrAfter(requestedPosition) is not null)
            return requestedPosition;

        var firstClip = FindFirstPreviewableClip();
        return firstClip?.TimelineStart ?? requestedPosition;
    }

    private TimelineClip? FindNextTimelinePreviewClip()
    {
        if (_previewClipId is null)
            return null;

        var currentClip = CurrentProject.Tracks
            .Where(x => IsEnabledTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .FirstOrDefault(x => x.Id == _previewClipId);

        if (currentClip is null)
            return null;

        return CurrentProject.Tracks
            .Where(x => IsEnabledTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .Where(x => x.Id != currentClip.Id && x.TimelineStart >= currentClip.TimelineEnd)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private TimelineClip? FindTimelineClipAtOrAfter(TimeSpan timelinePosition)
    {
        return CurrentProject.Tracks
            .Where(x => IsEnabledTrackOfKind(x, "Video"))
            .SelectMany(x => x.Clips)
            .Where(x => x.TimelineEnd > timelinePosition + TimelineBoundaryTolerance)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private TimelineClip? FindVisualClipAt(TimeSpan timelinePosition)
    {
        return FindVisualLayerCandidatesAt(timelinePosition)
            .FirstOrDefault()
            .Clip;
    }

    private TimelineClip? FindBaseVideoClipAt(TimeSpan timelinePosition)
    {
        return FindVisualLayerCandidatesAt(timelinePosition)
            .Where(x => x.Asset.Type == Domain.Enums.MediaType.Video)
            .LastOrDefault()
            .Clip;
    }

    private TimelineClip? FindTopVisualClipAt(TimeSpan timelinePosition)
    {
        return FindVisualLayerCandidatesAt(timelinePosition)
            .FirstOrDefault()
            .Clip;
    }

    private IReadOnlyList<VisualLayerCandidate> FindVisualLayerCandidatesAt(TimeSpan timelinePosition)
    {
        var result = new List<VisualLayerCandidate>();
        var videoTracks = CurrentProject.Tracks
            .Where(x => IsEnabledTrackOfKind(x, "Video"))
            .OrderByDescending(x => ExtractTrackIndex(x.Name))
            .ToList();

        for (var trackIndex = 0; trackIndex < videoTracks.Count; trackIndex++)
        {
            var track = videoTracks[trackIndex];
            var clip = track.Clips
                .Where(x => IsTimelinePositionInsideClip(x, timelinePosition))
                .OrderByDescending(x => x.TimelineStart)
                .FirstOrDefault();

            if (clip is null)
                continue;

            var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
            if (asset is null || asset.Type == Domain.Enums.MediaType.Audio)
                continue;

            result.Add(new VisualLayerCandidate(clip, asset, trackIndex));
        }

        return result;
    }

    private IReadOnlyList<(TimelineClip Clip, MediaAsset Asset, int ZIndex)> FindVisualLayersAboveBaseVideoAt(
        TimeSpan timelinePosition)
    {
        var candidates = FindVisualLayerCandidatesAt(timelinePosition);
        var baseVideo = candidates
            .Where(x => x.Asset.Type == Domain.Enums.MediaType.Video)
            .LastOrDefault();
        var visibleLayers = candidates
            .Where(x => baseVideo.Clip is null || x.TrackIndex < baseVideo.TrackIndex)
            .ToList();

        return visibleLayers
            .Select((x, index) => (x.Clip, x.Asset, visibleLayers.Count - index))
            .ToList();
    }

    private void RefreshPreviewVisualLayers(TimeSpan timelinePosition, bool preserveExistingLayers = false)
    {
        var layers = FindVisualLayersAboveBaseVideoAt(timelinePosition)
            .Select(x =>
            {
                var offsetInsideClip = timelinePosition - x.Clip.TimelineStart;
                if (offsetInsideClip < TimeSpan.Zero)
                    offsetInsideClip = TimeSpan.Zero;

                return new PreviewVisualLayerViewModel
                {
                    ClipId = x.Clip.Id,
                    Source = new Uri(x.Asset.FilePath, UriKind.Absolute),
                    MediaType = x.Asset.Type,
                    SourceStart = x.Clip.SourceStart + offsetInsideClip,
                    SourceDuration = x.Clip.SourceDuration - offsetInsideClip,
                    X = x.Clip.FrameX,
                    Y = x.Clip.FrameY,
                    Scale = x.Clip.FrameScale,
                    ZIndex = x.ZIndex
                };
            })
            .ToList();

        if (preserveExistingLayers)
        {
            ReconcilePreviewVisualLayers(layers);
            return;
        }

        PreviewVisualLayers.Clear();

        foreach (var layer in layers)
            PreviewVisualLayers.Add(layer);
    }

    private void RefreshCurrentTimelinePreviewAfterEdit()
    {
        var timelinePosition = TimelinePlaybackPosition;

        if (_isTimelinePlaybackActive)
        {
            RefreshPlayingTimelinePreviewAfterEdit(timelinePosition);
            return;
        }

        LoadTimelinePreviewAtPosition(timelinePosition);
    }

    private void RefreshPlayingTimelinePreviewAfterEdit(TimeSpan timelinePosition)
    {
        ConfigureAudioForTimelinePosition(timelinePosition);
        RefreshPreviewVisualLayers(timelinePosition, preserveExistingLayers: true);

        var visualClip = FindBaseVideoClipAt(timelinePosition) ?? FindVisualClipAt(timelinePosition);
        if (visualClip is null)
        {
            PlayTimelineFromPosition(timelinePosition);
            return;
        }

        var previousClipId = _previewClipId;
        var previousMediaSource = PreviewMediaSource;
        if (!TryLoadPreviewClip(visualClip, timelinePosition, preserveExistingLayers: true))
        {
            _previewTimelinePosition = visualClip.TimelineEnd;
            PlayTimelineFromPosition(_previewTimelinePosition);
            return;
        }

        var asset = CurrentProject.MediaAssets.First(x => x.Id == visualClip.MediaAssetId);
        var isSamePlayableClip = Equals(previousMediaSource, PreviewMediaSource) &&
            asset.Type != Domain.Enums.MediaType.Image;

        PreviewStatusText = asset.Type == Domain.Enums.MediaType.Image
            ? "Showing image on timeline."
            : "Playing timeline.";

        if (asset.Type == Domain.Enums.MediaType.Image)
        {
            RequestPreviewPlayback("ImageFrame");
            return;
        }

        RequestPreviewPlayback(isSamePlayableClip ? "SyncPlayback" : "Play");
    }

    private void ReconcilePreviewVisualLayers(IReadOnlyList<PreviewVisualLayerViewModel> layers)
    {
        for (var index = PreviewVisualLayers.Count - 1; index >= 0; index--)
        {
            var existing = PreviewVisualLayers[index];
            if (!layers.Any(next => IsSamePreviewLayer(existing, next)))
                PreviewVisualLayers.RemoveAt(index);
        }

        for (var desiredIndex = 0; desiredIndex < layers.Count; desiredIndex++)
        {
            var desired = layers[desiredIndex];
            var existingIndex = IndexOfPreviewLayer(desired);
            if (existingIndex < 0)
            {
                PreviewVisualLayers.Insert(desiredIndex, desired);
                continue;
            }

            if (existingIndex != desiredIndex)
                PreviewVisualLayers.Move(existingIndex, desiredIndex);

            UpdatePreviewVisualLayerInPlace(PreviewVisualLayers[desiredIndex], desired);
        }
    }

    private int IndexOfPreviewLayer(PreviewVisualLayerViewModel layer)
    {
        for (var index = 0; index < PreviewVisualLayers.Count; index++)
        {
            if (IsSamePreviewLayer(PreviewVisualLayers[index], layer))
                return index;
        }

        return -1;
    }

    private static bool IsSamePreviewLayer(PreviewVisualLayerViewModel left, PreviewVisualLayerViewModel right)
    {
        return left.ClipId == right.ClipId &&
            left.MediaType == right.MediaType &&
            Equals(left.Source, right.Source);
    }

    private static void UpdatePreviewVisualLayerInPlace(
        PreviewVisualLayerViewModel existing,
        PreviewVisualLayerViewModel next)
    {
        existing.X = next.X;
        existing.Y = next.Y;
        existing.Scale = next.Scale;
        existing.SourceStart = next.SourceStart;
        existing.SourceDuration = next.SourceDuration;
        existing.ZIndex = next.ZIndex;
    }

    private TimelineClip? FindAudioClipAt(TimeSpan timelinePosition)
    {
        return CurrentProject.Tracks
            .Where(x => IsEnabledTrackOfKind(x, "Audio"))
            .SelectMany(x => x.Clips)
            .Where(x => IsTimelinePositionInsideClip(x, timelinePosition))
            .OrderByDescending(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private TimelineClip? FindNextAudioClipAtOrAfter(TimeSpan timelinePosition)
    {
        return CurrentProject.Tracks
            .Where(x => IsEnabledTrackOfKind(x, "Audio"))
            .SelectMany(x => x.Clips)
            .Where(x => x.TimelineEnd > timelinePosition + TimelineBoundaryTolerance)
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
            IsPreviewVideoAudioMuted = ShouldMuteActiveEmbeddedVideoAudio(timelinePosition);
            PreviewVideoVolume = ResolveActiveEmbeddedVideoAudioVolume(timelinePosition);
            return;
        }

        var activeVisualClip = FindVisualClipAt(timelinePosition);
        if (activeVisualClip is not null &&
            activeVisualClip.MediaAssetId == audioClip.MediaAssetId &&
            AreClipsLinked(activeVisualClip, audioClip))
        {
            PreviewAudioSource = null;
            PreviewAudioSourceStart = TimeSpan.Zero;
            PreviewAudioSourceDuration = TimeSpan.Zero;
            IsPreviewVideoAudioMuted = ShouldMuteActiveEmbeddedVideoAudio(timelinePosition);
            PreviewVideoVolume = ResolveActiveEmbeddedVideoAudioVolume(timelinePosition);
            return;
        }

        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == audioClip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
        {
            PreviewAudioSource = null;
            PreviewAudioSourceStart = TimeSpan.Zero;
            PreviewAudioSourceDuration = TimeSpan.Zero;
            IsPreviewVideoAudioMuted = ShouldMuteActiveEmbeddedVideoAudio(timelinePosition);
            PreviewVideoVolume = ResolveActiveEmbeddedVideoAudioVolume(timelinePosition);
            return;
        }

        if (!File.Exists(asset.FilePath))
        {
            PreviewAudioSource = null;
            PreviewAudioSourceStart = TimeSpan.Zero;
            PreviewAudioSourceDuration = TimeSpan.Zero;
            IsPreviewVideoAudioMuted = ShouldMuteActiveEmbeddedVideoAudio(timelinePosition);
            PreviewVideoVolume = ResolveActiveEmbeddedVideoAudioVolume(timelinePosition);
            PreviewStatusText = $"Audio preview skipped: source file not found ({Path.GetFileName(asset.FilePath)}).";
            return;
        }

        var offsetInsideClip = timelinePosition - audioClip.TimelineStart;
        if (offsetInsideClip < TimeSpan.Zero)
            offsetInsideClip = TimeSpan.Zero;

        try
        {
            PreviewAudioSource = new Uri(asset.FilePath, UriKind.Absolute);
        }
        catch (UriFormatException)
        {
            PreviewAudioSource = null;
            PreviewStatusText = $"Audio preview skipped: invalid media path ({asset.DisplayName}).";
            return;
        }

        PreviewAudioSourceStart = audioClip.SourceStart + offsetInsideClip;
        PreviewAudioSourceDuration = audioClip.SourceDuration - offsetInsideClip;
        PreviewAudioVolume = audioClip.AudioVolume;
        IsPreviewVideoAudioMuted = ShouldMuteActiveEmbeddedVideoAudio(timelinePosition);
        PreviewVideoVolume = ResolveActiveEmbeddedVideoAudioVolume(timelinePosition);
    }

    private bool ShouldMuteActiveEmbeddedVideoAudio(TimeSpan timelinePosition)
    {
        var activeVisualClip = FindVisualClipAt(timelinePosition);
        if (activeVisualClip is null)
            return false;

        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == activeVisualClip.MediaAssetId);
        return asset is not null && ShouldMuteEmbeddedVideoAudio(activeVisualClip, asset, timelinePosition);
    }

    private double ResolveActiveEmbeddedVideoAudioVolume(TimeSpan timelinePosition)
    {
        var activeVisualClip = FindVisualClipAt(timelinePosition);
        if (activeVisualClip is null)
            return 1.0;

        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == activeVisualClip.MediaAssetId);
        return asset is null ? 1.0 : ResolveEmbeddedVideoAudioVolume(activeVisualClip, asset, timelinePosition);
    }

    private static bool AreClipsLinked(TimelineClip firstClip, TimelineClip secondClip)
    {
        return firstClip.LinkedGroupId is not null &&
            secondClip.LinkedGroupId is not null &&
            firstClip.LinkedGroupId == secondClip.LinkedGroupId;
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
            .Where(x => x > timelinePosition + TimelineBoundaryTolerance)
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

    private IEnumerable<(TimelineTrack Track, TimelineClip Clip)> ResolveLinkedTimelineClips(
        TimelineTrack track,
        TimelineClip clip)
    {
        if (clip.LinkedGroupId is null)
            return new[] { (track, clip) };

        return CurrentProject.Tracks
            .SelectMany(existingTrack => existingTrack.Clips.Select(existingClip => (Track: existingTrack, Clip: existingClip)))
            .Where(x => x.Clip.LinkedGroupId == clip.LinkedGroupId)
            .ToList();
    }

    private static TimeSpan ResolvePreviousClipBoundary(
        IEnumerable<(TimelineTrack Track, TimelineClip Clip)> trimTargets,
        IReadOnlySet<Guid> trimClipIds)
    {
        var boundary = TimeSpan.Zero;

        foreach (var (track, clip) in trimTargets)
        {
            var previousEnd = track.Clips
                .Where(x => !trimClipIds.Contains(x.Id) && x.TimelineEnd <= clip.TimelineStart)
                .Select(x => x.TimelineEnd)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max();

            if (previousEnd > boundary)
                boundary = previousEnd;
        }

        return boundary;
    }

    private static TimeSpan? ResolveNextClipBoundary(
        IEnumerable<(TimelineTrack Track, TimelineClip Clip)> trimTargets,
        IReadOnlySet<Guid> trimClipIds)
    {
        TimeSpan? boundary = null;

        foreach (var (track, clip) in trimTargets)
        {
            var nextStart = track.Clips
                .Where(x => !trimClipIds.Contains(x.Id) && x.TimelineStart >= clip.TimelineEnd)
                .Select(x => (TimeSpan?)x.TimelineStart)
                .OrderBy(x => x)
                .FirstOrDefault();

            if (nextStart is not null && (boundary is null || nextStart.Value < boundary.Value))
                boundary = nextStart.Value;
        }

        return boundary;
    }

    private static TimeSpan ClampTimeSpan(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum)
            return minimum;

        return value > maximum ? maximum : value;
    }

    private static TimeSpan MaxTimeSpan(params TimeSpan[] values)
    {
        return values.Max();
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

    private void RefreshActiveSelectionFromSelectedId()
    {
        _selectedAudioClip = null;
        _selectedVisualClip = null;

        if (_selectedTimelineClipId is null)
            return;

        var clipInfo = FindClipWithTrack(_selectedTimelineClipId.Value);
        if (clipInfo is null)
        {
            _selectedTimelineClipId = null;
            return;
        }

        var (track, clip) = clipInfo.Value;
        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
        if (asset is null)
        {
            _selectedTimelineClipId = null;
            return;
        }

        var isAudioTrack = IsTrackOfKind(track, "Audio");
        _selectedAudioClip = isAudioTrack ? clip : null;
        _selectedVisualClip = isAudioTrack || asset.Type == Domain.Enums.MediaType.Audio ? null : clip;
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

        return Enumerable.Empty<Guid>();
    }

    private void ClearPreviewAfterDeletedClip()
    {
        _previewClipId = null;
        _previewMediaType = null;
        _previewBaseVisualClip = null;
        _selectedAudioClip = null;
        _selectedTimelineClipIds.Clear();
        _selectedTimelineClipId = null;
        _selectedVisualClip = null;
        _isTimelinePlaybackActive = false;
        IsTimelineGapPreview = false;
        PreviewGapDuration = TimeSpan.Zero;
        PreviewAudioSource = null;
        PreviewAudioSourceStart = TimeSpan.Zero;
        PreviewAudioSourceDuration = TimeSpan.Zero;
        PreviewSourceStart = TimeSpan.Zero;
        PreviewSourceDuration = TimeSpan.Zero;
        PreviewMediaSource = null;
        IsPreviewVideoAudioMuted = false;
        PreviewTitle = "No clip selected";
        PreviewStatusText = "Clip deleted from timeline.";
        RefreshSelectedClipFrameProperties();
        RefreshSelectedAudioClipProperties();
        RefreshSelectedClipTrimProperties();
        RefreshTimelineSelectionCommandState();
        RefreshPreviewVisualLayers(TimelinePlaybackPosition);
        RequestPreviewPlayback("Stop");
    }

    private void RefreshSelectedClipFrameProperties()
    {
        OnPropertyChanged(nameof(HasSelectedVisualClip));
        OnPropertyChanged(nameof(IsInspectorPlaceholderVisible));
        OnPropertyChanged(nameof(SelectedClipFrameX));
        OnPropertyChanged(nameof(SelectedClipFrameY));
        OnPropertyChanged(nameof(SelectedClipFrameScalePercent));
        RefreshSelectedClipTrimProperties();
        RefreshPreviewFrameProperties();
        RefreshPreviewVisualLayers(TimelinePlaybackPosition, preserveExistingLayers: true);
        _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
        RefreshSplitCommandState();
    }

    private void RefreshSelectedAudioClipProperties()
    {
        OnPropertyChanged(nameof(HasSelectedAudioClip));
        OnPropertyChanged(nameof(IsInspectorPlaceholderVisible));
        OnPropertyChanged(nameof(SelectedClipAudioVolumePercent));
        RefreshSelectedClipTrimProperties();
        RefreshSplitCommandState();
    }

    private void RefreshSelectedClipTrimProperties()
    {
        OnPropertyChanged(nameof(HasTimelineSelection));
        OnPropertyChanged(nameof(IsInspectorPlaceholderVisible));
        OnPropertyChanged(nameof(SelectedClipTrimStartSeconds));
        OnPropertyChanged(nameof(SelectedClipTrimEndSeconds));
        OnPropertyChanged(nameof(SelectedClipTrimDurationSeconds));
        OnPropertyChanged(nameof(SelectedClipTrimLabel));
        _resetSelectedClipTrimCommand.RaiseCanExecuteChanged();
    }

    private void RefreshTimelineSelectionCommandState()
    {
        OnPropertyChanged(nameof(HasTimelineSelection));
        OnPropertyChanged(nameof(SelectedTimelineClipCount));
        OnPropertyChanged(nameof(IsInspectorPlaceholderVisible));
        _clearTimelineSelectionCommand.RaiseCanExecuteChanged();
        _deleteSelectedTimelineClipsCommand.RaiseCanExecuteChanged();
        RefreshSplitCommandState();
    }

    private void RefreshPreviewFrameProperties()
    {
        OnPropertyChanged(nameof(PreviewFrameX));
        OnPropertyChanged(nameof(PreviewFrameY));
        OnPropertyChanged(nameof(PreviewFrameScale));
    }

    private void RefreshSplitCommandState()
    {
        _splitSelectedClipCommand.RaiseCanExecuteChanged();
    }

    private TimelineClip? GetSelectedTrimClip()
    {
        return _selectedVisualClip ?? _selectedAudioClip;
    }

    private IEnumerable<TimelineClip> GetSelectedVisualClips()
    {
        return GetSelectedClipsWithTracks()
            .Where(x =>
            {
                var asset = CurrentProject.MediaAssets.FirstOrDefault(asset => asset.Id == x.Clip.MediaAssetId);
                return asset is not null &&
                    !IsTrackOfKind(x.Track, "Audio") &&
                    asset.Type != Domain.Enums.MediaType.Audio;
            })
            .Select(x => x.Clip);
    }

    private IEnumerable<TimelineClip> GetSelectedAudioClips()
    {
        return GetSelectedClipsWithTracks()
            .Where(x => IsTrackOfKind(x.Track, "Audio"))
            .Select(x => x.Clip);
    }

    private IEnumerable<(TimelineClip Clip, MediaAsset Asset)> GetSelectedTrimTargets()
    {
        var useAudioTargets = _selectedAudioClip is not null && _selectedVisualClip is null;
        var clips = useAudioTargets
            ? GetSelectedAudioClips()
            : GetSelectedVisualClips();

        foreach (var clip in clips)
        {
            var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
            if (asset is not null)
                yield return (clip, asset);
        }
    }

    private IEnumerable<(TimelineTrack Track, TimelineClip Clip)> GetSelectedClipsWithTracks()
    {
        foreach (var selectedClipId in _selectedTimelineClipIds)
        {
            var clipInfo = FindClipWithTrack(selectedClipId);
            if (clipInfo is not null)
                yield return clipInfo.Value;
        }
    }

    private MediaAsset? GetSelectedTrimAsset()
    {
        var clip = GetSelectedTrimClip();
        return clip is null
            ? null
            : CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
    }

    private void UpdateSelectedClipTrim(double startSeconds, double endSeconds)
    {
        var trimTargets = GetSelectedTrimTargets().ToList();
        if (trimTargets.Count == 0)
            return;

        SaveUndoSnapshot();
        var changed = false;
        foreach (var (clip, asset) in trimTargets)
        {
            changed |= ApplyTrimToClip(
                clip,
                asset,
                startSeconds,
                endSeconds,
                saveUndo: false,
                refreshAfterApply: false);
        }

        if (!changed)
            return;

        RefreshTimelinePresentation();
        RefreshSelectedClipTrimProperties();
        RefreshCurrentTimelinePreviewAfterEdit();
        PreviewStatusText = trimTargets.Count > 1
            ? "Selected clips trimmed."
            : "Selected clip trimmed.";
    }

    private double CurrentTimelinePixelsPerSecond => TimelineZoomLevels[_timelineZoomIndex];

    private TimeSpan ResolveTimelineCanvasDuration()
    {
        var projectEnd = CurrentProject.Tracks
            .SelectMany(track => track.Clips)
            .Select(clip => clip.TimelineEnd)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();

        var minimumDuration = TimeSpan.FromSeconds(TimelineMinimumSeconds);
        var contentDuration = projectEnd + TimeSpan.FromSeconds(TimelineEndPaddingSeconds);
        return contentDuration > minimumDuration ? contentDuration : minimumDuration;
    }

    private int ResolveTimelineFitZoomIndex()
    {
        var durationSeconds = ResolveTimelineCanvasDuration().TotalSeconds;
        if (durationSeconds <= 0)
            return _timelineZoomIndex;

        const double targetCanvasWidth = 2600;
        var targetPixelsPerSecond = targetCanvasWidth / durationSeconds;
        var fittedIndex = Array.FindLastIndex(TimelineZoomLevels, x => x <= targetPixelsPerSecond);
        return fittedIndex < 0 ? 0 : fittedIndex;
    }

    private int ResolveTimelineZoomMaxIndex()
    {
        var durationSeconds = ResolveTimelineCanvasDuration().TotalSeconds;
        if (durationSeconds <= 0)
            return TimelineZoomLevels.Length - 1;

        var maxPixelsPerSecond = TimelineMaxComfortableCanvasWidth / durationSeconds;
        var maxIndex = Array.FindLastIndex(TimelineZoomLevels, x => x <= maxPixelsPerSecond);
        return Math.Clamp(maxIndex < 0 ? 0 : maxIndex, 0, TimelineZoomLevels.Length - 1);
    }

    private void RefreshTimelinePresentation()
    {
        if (_timelineZoomIndex > TimelineZoomMaxIndex)
            _timelineZoomIndex = TimelineZoomMaxIndex;

        RefreshTimelineTracks();
        RefreshPreviewVisualLayers(TimelinePlaybackPosition, preserveExistingLayers: true);
        _zoomInTimelineCommand?.RaiseCanExecuteChanged();
        _zoomOutTimelineCommand?.RaiseCanExecuteChanged();
        _playPreviewCommand?.RaiseCanExecuteChanged();
        _pausePreviewCommand?.RaiseCanExecuteChanged();
        _stopPreviewCommand?.RaiseCanExecuteChanged();
        _exportProjectCommand?.RaiseCanExecuteChanged();
        _selectAllTimelineClipsCommand?.RaiseCanExecuteChanged();
        _deleteSelectedTimelineClipsCommand?.RaiseCanExecuteChanged();
        RefreshHistoryCommandState();
        OnPropertyChanged(nameof(TimelineZoomMaxIndex));
        OnPropertyChanged(nameof(TimelineDurationSeconds));
        OnPropertyChanged(nameof(TimelinePixelsPerSecond));
    }

    private void RefreshTimelineVisualScale()
    {
        RefreshTimelinePresentation();
        _zoomInTimelineCommand.RaiseCanExecuteChanged();
        _zoomOutTimelineCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(TimelineZoomIndex));
        OnPropertyChanged(nameof(TimelineCanvasWidth));
        OnPropertyChanged(nameof(TimelineDurationSeconds));
        OnPropertyChanged(nameof(TimelinePixelsPerSecond));
        OnPropertyChanged(nameof(TimelinePlayheadCanvasLeft));
        OnPropertyChanged(nameof(TimelineZoomMaxIndex));
        OnPropertyChanged(nameof(TimelineScaleLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}








