namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Текущий инструмент таймлайна.
/// </summary>
public enum TimelineToolMode
{
    /// <summary>
    /// 	Инструмент рамочного выделения.
    /// </summary>
    Select = 0,

    /// <summary>
    /// 	Инструмент перемещения клипов.
    /// </summary>
    Move = 1,

    /// <summary>
    /// 	Инструмент разрезания клипов.
    /// </summary>
    Razor = 2
}
