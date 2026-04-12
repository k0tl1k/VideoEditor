using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
    private readonly record struct VisualLayerCandidate(TimelineClip Clip, MediaAsset Asset, int TrackIndex);

    private readonly IMediaImportService _mediaImportService;
    private readonly IMediaThumbnailService _mediaThumbnailService;
    private readonly ITimelineExportService _timelineExportService;
    private readonly RelayCommand _browseExportOutputPathCommand;
    private readonly RelayCommand _exportProjectCommand;
    private readonly RelayCommand _insertSelectedMediaToTimelineCommand;
    private readonly RelayCommand _playPreviewCommand;
    private readonly RelayCommand _pausePreviewCommand;
    private readonly RelayCommand _clearTimelineSelectionCommand;
    private readonly RelayCommand _resetSelectedClipFrameCommand;
    private readonly RelayCommand _stopPreviewCommand;
    private readonly RelayCommand _zoomInTimelineCommand;
    private readonly RelayCommand _zoomOutTimelineCommand;

    private ImportedMediaItemViewModel? _selectedImportedMedia;
    private Domain.Enums.MediaType? _previewMediaType;
    private Guid? _previewClipId;
    private bool _isTimelineGapPreview;
    private bool _isTimelinePlaybackActive;
    private bool _isPreviewVideoAudioMuted;
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

    public ICommand ResetSelectedClipFrameCommand { get; }

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

    public bool HasTimelineSelection => _selectedTimelineClipId is not null;

    public bool IsInspectorPlaceholderVisible => !HasSelectedVisualClip && !HasSelectedAudioClip;

    public double SelectedClipAudioVolumePercent
    {
        get => (_selectedAudioClip?.AudioVolume ?? 1.0) * 100;
        set
        {
            if (_selectedAudioClip is null)
                return;

            var volume = Math.Clamp(value / 100, 0, 2.0);
            if (Math.Abs(_selectedAudioClip.AudioVolume - volume) < 0.001)
                return;

            _selectedAudioClip.AudioVolume = volume;
            RefreshSelectedAudioClipProperties();
            ConfigureAudioForTimelinePosition(TimelinePlaybackPosition);
        }
    }

    public double SelectedClipFrameX
    {
        get => _selectedVisualClip?.FrameX ?? 0;
        set
        {
            if (_selectedVisualClip is null || Math.Abs(_selectedVisualClip.FrameX - value) < 0.001)
                return;

            _selectedVisualClip.FrameX = value;
            RefreshSelectedClipFrameProperties();
            _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
        }
    }

    public double SelectedClipFrameY
    {
        get => _selectedVisualClip?.FrameY ?? 0;
        set
        {
            if (_selectedVisualClip is null || Math.Abs(_selectedVisualClip.FrameY - value) < 0.001)
                return;

            _selectedVisualClip.FrameY = value;
            RefreshSelectedClipFrameProperties();
            _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
        }
    }

    public double SelectedClipFrameScalePercent
    {
        get => (_selectedVisualClip?.FrameScale ?? 1.0) * 100;
        set
        {
            if (_selectedVisualClip is null)
                return;

            var scale = Math.Clamp(value / 100, 0.1, 3.0);
            if (Math.Abs(_selectedVisualClip.FrameScale - scale) < 0.001)
                return;

            _selectedVisualClip.FrameScale = scale;
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
        return _selectedVisualClip is not null &&
            (Math.Abs(_selectedVisualClip.FrameX) > 0.001 ||
                Math.Abs(_selectedVisualClip.FrameY) > 0.001 ||
                Math.Abs(_selectedVisualClip.FrameScale - 1.0) > 0.001);
    }

    private void ResetSelectedClipFrame()
    {
        if (_selectedVisualClip is null)
            return;

        _selectedVisualClip.FrameX = 0;
        _selectedVisualClip.FrameY = 0;
        _selectedVisualClip.FrameScale = 1.0;
        RefreshSelectedClipFrameProperties();
        _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
        PreviewStatusText = "Selected clip transform reset.";
    }

    private bool CanClearTimelineSelection()
    {
        return HasTimelineSelection;
    }

    public void ClearTimelineSelection()
    {
        if (!HasTimelineSelection)
            return;

        _selectedTimelineClipId = null;
        _selectedAudioClip = null;
        _selectedVisualClip = null;
        RefreshSelectedClipFrameProperties();
        RefreshSelectedAudioClipProperties();
        RefreshTimelineTracks();
        RefreshTimelineSelectionCommandState();
        PreviewStatusText = "Timeline selection cleared.";
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
        ITimelineExportService timelineExportService)
    {
        _mediaImportService = mediaImportService;
        _mediaThumbnailService = mediaThumbnailService;
        _timelineExportService = timelineExportService;

        CurrentProject = projectBootstrapService.CreateDefaultProject("Diploma Project");
        ProjectDirectory = projectPathService.BuildProjectDirectory(CurrentProject.Name);
        _exportOutputPath = Path.Combine(ProjectDirectory, "Exports", $"{CurrentProject.Name}.mp4");
        _browseExportOutputPathCommand = new RelayCommand(BrowseExportOutputPath);
        _exportProjectCommand = new RelayCommand(ExportProject, CanExportProject);
        _insertSelectedMediaToTimelineCommand = new RelayCommand(InsertSelectedMediaToTimeline, CanInsertSelectedMediaToTimeline);
        _playPreviewCommand = new RelayCommand(PlayPreview, CanPlayPreview);
        _pausePreviewCommand = new RelayCommand(PausePreview, CanControlPreview);
        _clearTimelineSelectionCommand = new RelayCommand(ClearTimelineSelection, CanClearTimelineSelection);
        _resetSelectedClipFrameCommand = new RelayCommand(ResetSelectedClipFrame, CanResetSelectedClipFrame);
        _stopPreviewCommand = new RelayCommand(StopPreview, CanControlPreview);
        _zoomInTimelineCommand = new RelayCommand(ZoomInTimeline, CanZoomInTimeline);
        _zoomOutTimelineCommand = new RelayCommand(ZoomOutTimeline, CanZoomOutTimeline);

        ImportMediaCommand = new RelayCommand(ImportMedia);
        BrowseExportOutputPathCommand = _browseExportOutputPathCommand;
        ExportProjectCommand = _exportProjectCommand;
        InsertSelectedMediaToTimelineCommand = _insertSelectedMediaToTimelineCommand;
        PlayPreviewCommand = _playPreviewCommand;
        PausePreviewCommand = _pausePreviewCommand;
        StopPreviewCommand = _stopPreviewCommand;
        ClearTimelineSelectionCommand = _clearTimelineSelectionCommand;
        ResetSelectedClipFrameCommand = _resetSelectedClipFrameCommand;
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
            PreviewStatusText = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
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
                    Width = Math.Max(96, clip.SourceDuration.TotalSeconds * CurrentTimelinePixelsPerSecond),
                    IsSelected = clip.Id == _selectedTimelineClipId
                });
            }

            if (IsTrackOfKind(track, "Video"))
                VideoTimelineTracks.Insert(0, trackItem);
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

    private static string FormatSecondsLabel(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.Hours > 0
            ? $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes:D2}:{time.Seconds:D2}";
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
        RefreshTimelineTracks();
        RefreshCurrentTimelinePreviewAfterEdit();
    }

    private void ToggleTrackEnabled(string trackName)
    {
        var track = CurrentProject.Tracks.FirstOrDefault(x =>
            x.Name.Equals(trackName, StringComparison.OrdinalIgnoreCase));
        if (track is null)
            return;

        var wasTimelinePlaybackActive = _isTimelinePlaybackActive;
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

        if (_previewClipId == clipId ||
            linkedClipIds.Contains(_previewClipId.GetValueOrDefault()) ||
            _selectedVisualClip?.Id == clipId ||
            linkedClipIds.Contains(_selectedVisualClip?.Id ?? Guid.Empty) ||
            _selectedAudioClip?.Id == clipId ||
            linkedClipIds.Contains(_selectedAudioClip?.Id ?? Guid.Empty))
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
        LoadTimelinePreviewAtPosition(timelinePosition);
    }

    public void SelectClipForPreview(Guid clipId)
    {
        var clipInfo = FindClipWithTrack(clipId);
        if (clipInfo is null)
            return;

        var (track, clip) = clipInfo.Value;
        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
            return;

        var isAudioTrack = IsTrackOfKind(track, "Audio");
        _selectedTimelineClipId = clip.Id;
        _selectedAudioClip = isAudioTrack ? clip : null;
        _selectedVisualClip = isAudioTrack || asset.Type == Domain.Enums.MediaType.Audio ? null : clip;
        RefreshSelectedClipFrameProperties();
        RefreshSelectedAudioClipProperties();
        RefreshTimelineTracks();
        RefreshTimelineSelectionCommandState();
        PreviewStatusText = isAudioTrack || asset.Type == Domain.Enums.MediaType.Audio
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
        var offsetInsideClip = timelinePosition - clip.TimelineStart;
        if (offsetInsideClip < TimeSpan.Zero)
            offsetInsideClip = TimeSpan.Zero;

        PreviewSourceDuration = clip.SourceDuration - offsetInsideClip;

        if (asset.Type == Domain.Enums.MediaType.Image)
        {
            PreviewStatusText = "Showing image on timeline.";
            RequestPreviewPlayback("ImageFrame");
            return;
        }

        PreviewStatusText = "Playing timeline.";
        RequestPreviewPlayback("Play");
    }

    private bool TryLoadPreviewClip(
        TimelineClip clip,
        TimeSpan timelinePosition,
        bool preserveExistingLayers = false)
    {
        var asset = CurrentProject.MediaAssets.FirstOrDefault(x => x.Id == clip.MediaAssetId);
        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
            return false;

        var offsetInsideClip = timelinePosition - clip.TimelineStart;
        if (offsetInsideClip < TimeSpan.Zero)
            offsetInsideClip = TimeSpan.Zero;

        var nextSource = new Uri(asset.FilePath, UriKind.Absolute);
        var isSamePreviewClip = _previewClipId == clip.Id && Equals(PreviewMediaSource, nextSource);

        _previewClipId = clip.Id;
        _previewBaseVisualClip = clip;
        _previewMediaType = asset.Type;
        _previewTimelinePosition = timelinePosition;
        PreviewGapDuration = TimeSpan.Zero;
        IsTimelineGapPreview = false;
        PreviewSourceStart = clip.SourceStart + offsetInsideClip;
        PreviewSourceDuration = clip.SourceDuration - offsetInsideClip;
        TimelinePlaybackPosition = timelinePosition;
        PreviewTitle = asset.DisplayName;
        IsPreviewVideoAudioMuted = ShouldMuteEmbeddedVideoAudio(clip, asset, timelinePosition);
        PreviewVideoVolume = ResolveEmbeddedVideoAudioVolume(clip, asset, timelinePosition);
        if (!isSamePreviewClip)
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
            .Where(x => x.TimelineEnd > timelinePosition)
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
                .Where(x => x.TimelineStart <= timelinePosition && x.TimelineEnd > timelinePosition)
                .OrderBy(x => x.TimelineStart)
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
        var isSamePlayableClip = previousClipId == visualClip.Id &&
            Equals(previousMediaSource, PreviewMediaSource) &&
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
            .Where(x => x.TimelineStart <= timelinePosition && x.TimelineEnd > timelinePosition)
            .OrderBy(x => x.TimelineStart)
            .FirstOrDefault();
    }

    private TimelineClip? FindNextAudioClipAtOrAfter(TimeSpan timelinePosition)
    {
        return CurrentProject.Tracks
            .Where(x => IsEnabledTrackOfKind(x, "Audio"))
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
            IsPreviewVideoAudioMuted = ShouldMuteActiveEmbeddedVideoAudio(timelinePosition);
            PreviewVideoVolume = ResolveActiveEmbeddedVideoAudioVolume(timelinePosition);
            return;
        }

        var activeVisualClip = FindVisualClipAt(timelinePosition);
        if (activeVisualClip is not null && activeVisualClip.MediaAssetId == audioClip.MediaAssetId)
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

        var offsetInsideClip = timelinePosition - audioClip.TimelineStart;
        if (offsetInsideClip < TimeSpan.Zero)
            offsetInsideClip = TimeSpan.Zero;

        PreviewAudioSource = new Uri(asset.FilePath, UriKind.Absolute);
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
        _previewBaseVisualClip = null;
        _selectedAudioClip = null;
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
        RefreshPreviewFrameProperties();
        RefreshPreviewVisualLayers(TimelinePlaybackPosition, preserveExistingLayers: true);
        _resetSelectedClipFrameCommand.RaiseCanExecuteChanged();
    }

    private void RefreshSelectedAudioClipProperties()
    {
        OnPropertyChanged(nameof(HasSelectedAudioClip));
        OnPropertyChanged(nameof(IsInspectorPlaceholderVisible));
        OnPropertyChanged(nameof(SelectedClipAudioVolumePercent));
    }

    private void RefreshTimelineSelectionCommandState()
    {
        OnPropertyChanged(nameof(HasTimelineSelection));
        OnPropertyChanged(nameof(IsInspectorPlaceholderVisible));
        _clearTimelineSelectionCommand.RaiseCanExecuteChanged();
    }

    private void RefreshPreviewFrameProperties()
    {
        OnPropertyChanged(nameof(PreviewFrameX));
        OnPropertyChanged(nameof(PreviewFrameY));
        OnPropertyChanged(nameof(PreviewFrameScale));
    }

    private double CurrentTimelinePixelsPerSecond => TimelineZoomLevels[_timelineZoomIndex];

    private void RefreshTimelinePresentation()
    {
        BuildTimelineRuler();
        RefreshTimelineTracks();
        RefreshPreviewVisualLayers(TimelinePlaybackPosition, preserveExistingLayers: true);
        _zoomInTimelineCommand?.RaiseCanExecuteChanged();
        _zoomOutTimelineCommand?.RaiseCanExecuteChanged();
        _playPreviewCommand?.RaiseCanExecuteChanged();
        _pausePreviewCommand?.RaiseCanExecuteChanged();
        _stopPreviewCommand?.RaiseCanExecuteChanged();
        _exportProjectCommand?.RaiseCanExecuteChanged();
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







