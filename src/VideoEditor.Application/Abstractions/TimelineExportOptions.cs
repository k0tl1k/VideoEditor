namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	Параметры экспорта таймлайна через FFmpeg.
/// </summary>
public sealed class TimelineExportOptions
{
    /// <summary>
    /// 	Ширина выходного видео.
    /// </summary>
    public int Width { get; init; } = 1920;

    /// <summary>
    /// 	Высота выходного видео.
    /// </summary>
    public int Height { get; init; } = 1080;

    /// <summary>
    /// 	Частота кадров выходного видео.
    /// </summary>
    public int FrameRate { get; init; } = 30;

    /// <summary>
    /// 	Начало экспортируемого диапазона.
    /// </summary>
    public TimeSpan RangeStart { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// 	Конец экспортируемого диапазона.
    /// </summary>
    public TimeSpan? RangeEnd { get; init; }

    /// <summary>
    /// 	CRF-качество для кодеков FFmpeg.
    /// </summary>
    public int ConstantRateFactor { get; init; } = 23;

    /// <summary>
    /// 	Название выбранного видеокодека.
    /// </summary>
    public string VideoCodec { get; init; } = "H.264";

    /// <summary>
    /// 	Пресет скорости кодирования.
    /// </summary>
    public string Preset { get; init; } = "medium";

    /// <summary>
    /// 	Битрейт аудио в килобитах.
    /// </summary>
    public int AudioBitrateKbps { get; init; } = 192;
}
