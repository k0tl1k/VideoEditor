namespace VideoEditor.Application.Abstractions;

public sealed class TimelineExportOptions
{
    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    public int FrameRate { get; init; } = 30;

    public TimeSpan RangeStart { get; init; } = TimeSpan.Zero;

    public TimeSpan? RangeEnd { get; init; }

    public int ConstantRateFactor { get; init; } = 23;

    public string VideoCodec { get; init; } = "H.264";

    public string Preset { get; init; } = "medium";

    public int AudioBitrateKbps { get; init; } = 192;
}
