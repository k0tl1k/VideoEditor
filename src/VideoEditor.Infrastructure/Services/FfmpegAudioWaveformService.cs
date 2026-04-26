using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;
using VideoEditor.Domain.Enums;

namespace VideoEditor.Infrastructure.Services;

/// <summary>
///     Builds timeline waveform data by decoding audio through FFmpeg and caching peak samples.
/// </summary>
public sealed class FfmpegAudioWaveformService : IAudioWaveformService
{
    private const int DecodeSampleRate = 8000;
    private static readonly AudioWaveformData EmptyWaveform = new(Array.Empty<double>());
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly Dictionary<string, AudioWaveformData> _memoryCache = new();
    private readonly object _syncRoot = new();
    private bool _ffmpegResolved;
    private string? _resolvedFfmpegPath;

    public AudioWaveformData GetWaveform(MediaAsset asset, string projectDirectory, int targetSamples = 1600)
    {
        if (asset.Type == MediaType.Image ||
            asset.Duration <= TimeSpan.Zero ||
            targetSamples <= 0 ||
            !File.Exists(asset.FilePath))
            return EmptyWaveform;

        try
        {
            var cachePath = BuildCachePath(asset, projectDirectory, targetSamples);
            lock (_syncRoot)
            {
                if (_memoryCache.TryGetValue(cachePath, out var memoryCached))
                    return memoryCached;
            }

            var cached = ReadCache(cachePath);
            if (cached is not null)
            {
                Remember(cachePath, cached);
                return cached;
            }

            var ffmpegPath = ResolveCachedFfmpegExecutable();
            if (string.IsNullOrWhiteSpace(ffmpegPath))
                return EmptyWaveform;

            var peaks = DecodePeaks(ffmpegPath, asset, targetSamples);
            if (peaks.Count == 0)
                return EmptyWaveform;

            var data = new AudioWaveformData(peaks);
            WriteCache(cachePath, data);
            Remember(cachePath, data);
            return data;
        }
        catch
        {
            return EmptyWaveform;
        }
    }

    private void Remember(string cachePath, AudioWaveformData data)
    {
        lock (_syncRoot)
            _memoryCache[cachePath] = data;
    }

    private static IReadOnlyList<double> DecodePeaks(string ffmpegPath, MediaAsset asset, int targetSamples)
    {
        var totalInputSamples = Math.Max(1, (long)Math.Ceiling(asset.Duration.TotalSeconds * DecodeSampleRate));
        var samplesPerPeak = Math.Max(1, (int)Math.Ceiling(totalInputSamples / (double)targetSamples));
        var peaks = new List<double>(targetSamples);
        var currentPeak = 0d;
        var currentCount = 0;
        var buffer = new byte[8192];
        var pending = new byte[4];
        var pendingCount = 0;

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-hide_banner -loglevel error -i \"{asset.FilePath}\" -vn -ac 1 -ar {DecodeSampleRate.ToString(CultureInfo.InvariantCulture)} -f f32le -",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process is null)
            return Array.Empty<double>();

        var stream = process.StandardOutput.BaseStream;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                pending[pendingCount++] = buffer[i];
                if (pendingCount < 4)
                    continue;

                var sample = BitConverter.ToSingle(pending, 0);
                if (!float.IsNaN(sample) && !float.IsInfinity(sample))
                    currentPeak = Math.Max(currentPeak, Math.Min(1d, Math.Abs(sample)));

                currentCount++;
                pendingCount = 0;

                if (currentCount < samplesPerPeak)
                    continue;

                peaks.Add(currentPeak);
                currentPeak = 0;
                currentCount = 0;
            }
        }

        process.WaitForExit(1000);
        if (currentCount > 0)
            peaks.Add(currentPeak);

        return peaks.Count > targetSamples
            ? peaks.Take(targetSamples).ToArray()
            : peaks;
    }

    private static string BuildCachePath(MediaAsset asset, string projectDirectory, int targetSamples)
    {
        var cacheDirectory = Path.Combine(projectDirectory, "Waveforms");
        Directory.CreateDirectory(cacheDirectory);

        var stamp = File.GetLastWriteTimeUtc(asset.FilePath).Ticks;
        var key = $"{asset.FilePath}|{stamp}|{asset.Duration.Ticks}|{targetSamples}";
        return Path.Combine(cacheDirectory, $"{ComputeHash(key)}.waveform.json");
    }

    private static AudioWaveformData? ReadCache(string cachePath)
    {
        if (!File.Exists(cachePath))
            return null;

        var dto = JsonSerializer.Deserialize<WaveformCacheDto>(File.ReadAllText(cachePath), JsonOptions);
        return dto?.Peaks is { Count: > 0 }
            ? new AudioWaveformData(dto.Peaks)
            : null;
    }

    private static void WriteCache(string cachePath, AudioWaveformData data)
    {
        var dto = new WaveformCacheDto(data.Peaks.ToArray());
        File.WriteAllText(cachePath, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private string? ResolveCachedFfmpegExecutable()
    {
        lock (_syncRoot)
        {
            if (_ffmpegResolved)
                return _resolvedFfmpegPath;

            _resolvedFfmpegPath = ResolveFfmpegExecutable();
            _ffmpegResolved = true;
            return _resolvedFfmpegPath;
        }
    }

    private static string? ResolveFfmpegExecutable()
    {
        var envPath = Environment.GetEnvironmentVariable("VIDEOEDITOR_FFMPEG");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        var candidates = new[]
        {
            "ffmpeg",
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "FFmpeg", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "VideoEditor.Infrastructure", "FFmpeg", "ffmpeg.exe")
        };

        foreach (var candidate in candidates)
        {
            if (candidate.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                if (CanRunFfmpeg(candidate))
                    return candidate;

                continue;
            }

            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private static bool CanRunFfmpeg(string fileName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "-version",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });

            process?.WaitForExit(1500);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var builder = new StringBuilder(bytes.Length * 2);

        foreach (var b in bytes)
            builder.Append(b.ToString("x2"));

        return builder.ToString();
    }

    private sealed record WaveformCacheDto(IReadOnlyList<double> Peaks);
}
