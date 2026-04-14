using System.ComponentModel;
using System.Runtime.CompilerServices;
using VideoEditor.Domain.Enums;

namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Визуальный слой предпросмотра для композиции таймлайна.
/// </summary>
public sealed class PreviewVisualLayerViewModel : INotifyPropertyChanged
{
    private double _scale;
    private TimeSpan _sourceDuration;
    private TimeSpan _sourceStart;
    private double _x;
    private double _y;
    private int _zIndex;

    /// <summary>
    /// 	Идентификатор клипа, которому принадлежит слой.
    /// </summary>
    public required Guid ClipId { get; init; }

    /// <summary>
    /// 	Источник медиафайла для предпросмотра.
    /// </summary>
    public required Uri Source { get; init; }

    /// <summary>
    /// 	Тип медиафайла.
    /// </summary>
    public required MediaType MediaType { get; init; }

    /// <summary>
    /// 	Начало фрагмента внутри исходника.
    /// </summary>
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

    /// <summary>
    /// 	Длительность фрагмента внутри исходника.
    /// </summary>
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

    /// <summary>
    /// 	Позиция слоя по X в кадре предпросмотра.
    /// </summary>
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

    /// <summary>
    /// 	Позиция слоя по Y в кадре предпросмотра.
    /// </summary>
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

    /// <summary>
    /// 	Масштаб слоя в кадре предпросмотра.
    /// </summary>
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

    /// <summary>
    /// 	Порядок слоя в композиции.
    /// </summary>
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

    /// <summary>
    /// 	Показывает, что слой является изображением.
    /// </summary>
    public bool IsImage => MediaType == MediaType.Image;

    /// <summary>
    /// 	Показывает, что слой является видео.
    /// </summary>
    public bool IsVideo => MediaType == MediaType.Video;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
