namespace VideoEditor.Application.Abstractions;

/// <summary>
///     Downsampled audio waveform peaks normalized to the 0..1 range.
/// </summary>
public sealed record AudioWaveformData(IReadOnlyList<double> Peaks);
