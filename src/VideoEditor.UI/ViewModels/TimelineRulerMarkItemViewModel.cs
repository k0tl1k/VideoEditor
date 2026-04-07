namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Отметка линейки времени на таймлайне.
/// </summary>
public sealed class TimelineRulerMarkItemViewModel
{
    public required string Label { get; init; }

    public required double Left { get; init; }

    public required double TickHeight { get; init; }

    public required double LabelOpacity { get; init; }

    public required double TickOpacity { get; init; }
}
