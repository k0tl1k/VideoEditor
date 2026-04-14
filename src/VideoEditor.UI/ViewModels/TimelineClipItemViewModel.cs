using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Элемент клипа для отображения на таймлайне.
/// </summary>
public sealed class TimelineClipItemViewModel : INotifyPropertyChanged
{
    private bool _isDragging;

    /// <summary>
    /// 	Идентификатор клипа на таймлайне.
    /// </summary>
    public required Guid ClipId { get; init; }

    /// <summary>
    /// 	Идентификатор исходного медиафайла.
    /// </summary>
    public required Guid MediaAssetId { get; init; }

    /// <summary>
    /// 	Имя дорожки, на которой лежит клип.
    /// </summary>
    public required string TrackName { get; init; }

    /// <summary>
    /// 	Название клипа для UI.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 	Цвет фона клипа.
    /// </summary>
    public required string BackgroundColor { get; init; }

    /// <summary>
    /// 	Ширина клипа в пикселях.
    /// </summary>
    public required double Width { get; init; }

    /// <summary>
    /// 	Позиция клипа слева в пикселях.
    /// </summary>
    public required double Left { get; init; }

    /// <summary>
    /// 	Акцентный цвет клипа.
    /// </summary>
    public required string AccentColor { get; init; }

    /// <summary>
    /// 	Показывает, выбран ли клип.
    /// </summary>
    public required bool IsSelected { get; init; }

    /// <summary>
    /// 	Показывает, связан ли клип с парным аудио или видео.
    /// </summary>
    public required bool IsLinkedClip { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 	Показывает, находится ли клип в процессе перетаскивания.
    /// </summary>
    public bool IsDragging
    {
        get => _isDragging;
        set
        {
            if (_isDragging == value)
                return;

            _isDragging = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClipOpacity));
        }
    }

    /// <summary>
    /// 	Цвет рамки с учетом выделения.
    /// </summary>
    public string BorderColor => IsSelected ? "#F6D365" : AccentColor;

    /// <summary>
    /// 	Толщина рамки с учетом выделения.
    /// </summary>
    public string BorderThickness => IsSelected ? "3" : "1";

    /// <summary>
    /// 	Прозрачность клипа во время перетаскивания.
    /// </summary>
    public double ClipOpacity => IsDragging ? 0.55 : 1.0;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
