using System.ComponentModel;
using System.Runtime.CompilerServices;
using VideoEditor.Domain.Enums;

namespace VideoEditor.UI.ViewModels;

public sealed class PreviewVisualLayerViewModel : INotifyPropertyChanged
{
    private double _scale;
    private TimeSpan _sourceDuration;
    private TimeSpan _sourceStart;
    private double _x;
    private double _y;
    private int _zIndex;

    public required Guid ClipId { get; init; }

    public required Uri Source { get; init; }

    public required MediaType MediaType { get; init; }

    public required TimeSpan SourceStart
    {
        get => _sourceStart;
        set
        {
            if (_sourceStart == value)
                return;

            _sourceStart = value;
            OnPropertyChanged();
        }
    }

    public required TimeSpan SourceDuration
    {
        get => _sourceDuration;
        set
        {
            if (_sourceDuration == value)
                return;

            _sourceDuration = value;
            OnPropertyChanged();
        }
    }

    public required double X
    {
        get => _x;
        set
        {
            if (Math.Abs(_x - value) < 0.001)
                return;

            _x = value;
            OnPropertyChanged();
        }
    }

    public required double Y
    {
        get => _y;
        set
        {
            if (Math.Abs(_y - value) < 0.001)
                return;

            _y = value;
            OnPropertyChanged();
        }
    }

    public required double Scale
    {
        get => _scale;
        set
        {
            if (Math.Abs(_scale - value) < 0.001)
                return;

            _scale = value;
            OnPropertyChanged();
        }
    }

    public required int ZIndex
    {
        get => _zIndex;
        set
        {
            if (_zIndex == value)
                return;

            _zIndex = value;
            OnPropertyChanged();
        }
    }

    public bool IsImage => MediaType == MediaType.Image;

    public bool IsVideo => MediaType == MediaType.Video;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
