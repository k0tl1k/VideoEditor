using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Abstractions;

/// <summary>
///     Builds and caches downsampled waveform data for media assets with audio.
/// </summary>
public interface IAudioWaveformService
{
    /// <summary>
    ///     Returns waveform data for a media asset, or an empty waveform when it cannot be generated.
    /// </summary>
    /// <param name="asset">Source media asset.</param>
    /// <param name="projectDirectory">Directory used for project-local cache files.</param>
    /// <param name="targetSamples">Desired number of downsampled peak samples.</param>
    /// <returns>Downsampled waveform data.</returns>
    AudioWaveformData GetWaveform(MediaAsset asset, string projectDirectory, int targetSamples = 1600);
}
