using System.Collections.ObjectModel;

namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Элемент дорожки для отображения на таймлайне.
/// </summary>
public sealed class TimelineTrackItemViewModel
{
    public required string TrackName { get; init; }

    public required string Header { get; init; }

    public required string Title { get; init; }

    public ObservableCollection<TimelineClipItemViewModel> Clips { get; } = new();
}
