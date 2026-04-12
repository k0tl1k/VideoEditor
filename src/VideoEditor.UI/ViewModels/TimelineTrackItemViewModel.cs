using System.Collections.ObjectModel;
using System.Windows.Input;

namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Элемент дорожки для отображения на таймлайне.
/// </summary>
public sealed class TimelineTrackItemViewModel
{
    public required string TrackName { get; init; }

    public required string Header { get; init; }

    public required string Title { get; init; }

    public required bool IsEnabled { get; init; }

    public required bool IsVideoTrack { get; init; }

    public required ICommand ToggleTrackEnabledCommand { get; init; }

    public string ToggleTrackEnabledLabel => IsVideoTrack
        ? IsEnabled ? "Hide" : "Show"
        : IsEnabled ? "Mute" : "Sound";

    public string TrackStateLabel => IsVideoTrack
        ? IsEnabled ? "Visible" : "Hidden"
        : IsEnabled ? "Audible" : "Muted";

    public double TrackOpacity => IsEnabled ? 1.0 : 0.38;

    public ObservableCollection<TimelineClipItemViewModel> Clips { get; } = new();
}
