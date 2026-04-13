namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Отметка линейки времени на таймлайне.
/// </summary>
public sealed class TimelineRulerMarkItemViewModel
{
    /// <summary>
    /// 	Подпись отметки времени.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// 	Позиция отметки слева в пикселях.
    /// </summary>
    public required double Left { get; init; }

    /// <summary>
    /// 	Высота риски отметки.
    /// </summary>
    public required double TickHeight { get; init; }

    /// <summary>
    /// 	Прозрачность подписи.
    /// </summary>
    public required double LabelOpacity { get; init; }

    /// <summary>
    /// 	Прозрачность риски.
    /// </summary>
    public required double TickOpacity { get; init; }
}
