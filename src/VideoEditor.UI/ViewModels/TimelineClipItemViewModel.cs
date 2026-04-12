namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Элемент клипа для отображения на таймлайне.
/// </summary>
public sealed class TimelineClipItemViewModel
{
    public required Guid ClipId { get; init; }

    public required Guid MediaAssetId { get; init; }

    public required string TrackName { get; init; }

    public required string DisplayName { get; init; }

    public required string BackgroundColor { get; init; }

    public required double Width { get; init; }

    public required double Left { get; init; }

    public required string AccentColor { get; init; }

    public required bool IsSelected { get; init; }

    public string BorderColor => IsSelected ? "#F6D365" : AccentColor;

    public string BorderThickness => IsSelected ? "3" : "1";
}
