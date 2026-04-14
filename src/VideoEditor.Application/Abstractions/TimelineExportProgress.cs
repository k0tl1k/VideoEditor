namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	Состояние прогресса экспорта.
/// </summary>
public sealed class TimelineExportProgress
{
    /// <summary>
    /// 	Процент завершения экспорта.
    /// </summary>
    public double Percent { get; init; }

    /// <summary>
    /// 	Уже отрендеренная длительность.
    /// </summary>
    public TimeSpan RenderedDuration { get; init; }
}
