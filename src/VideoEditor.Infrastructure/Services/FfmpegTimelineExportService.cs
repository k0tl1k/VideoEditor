using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;
using VideoEditor.Domain.Enums;

namespace VideoEditor.Infrastructure.Services;

public sealed class FfmpegTimelineExportService : ITimelineExportService
{
    private readonly record struct ExportClipSegment(
        TimeSpan SourceStart,
        TimeSpan OutputStart,
        TimeSpan Duration);

    public async Task ExportAsync(
        VideoProject project,
        string outputFilePath,
        TimelineExportOptions options,
        IProgress<TimelineExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var timelineDuration = ResolveExportDuration(project);
        if (timelineDuration <= TimeSpan.Zero)
            throw new InvalidOperationException("Timeline is empty. Add at least one clip before export.");

        var rangeStart = options.RangeStart < TimeSpan.Zero ? TimeSpan.Zero : options.RangeStart;
        var rangeEnd = options.RangeEnd is { } requestedEnd && requestedEnd > TimeSpan.Zero
            ? requestedEnd
            : timelineDuration;
        if (rangeEnd > timelineDuration)
            rangeEnd = timelineDuration;

        if (rangeEnd <= rangeStart)
            throw new InvalidOperationException("Export range is empty. End time must be greater than start time.");

        var exportDuration = rangeEnd - rangeStart;
        var visualClips = ResolveVisualClips(project, rangeStart, rangeEnd).ToList();
        var audioClips = ResolveAudioClips(project, rangeStart, rangeEnd).ToList();
        var inputIndex = 0;
        var filterBuilder = new StringBuilder();
        var process = new ProcessStartInfo
        {
            FileName = ResolveFfmpegExecutablePath(),
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        process.ArgumentList.Add("-y");

        filterBuilder.Append(CultureInfo.InvariantCulture,
            $"color=c=black:s={options.Width}x{options.Height}:r={options.FrameRate}:d={FormatSeconds(exportDuration)}[vbase0];");

        var currentVideoLabel = "vbase0";
        var visualLayerIndex = 0;
        foreach (var clipInfo in visualClips)
        {
            var clip = clipInfo.Clip;
            var asset = clipInfo.Asset;
            var inputLabel = inputIndex++;
            var segment = ResolveExportSegment(clip, rangeStart, rangeEnd);
            AddInput(process, asset, segment.Duration);
            AppendVisualClipFilter(filterBuilder, inputLabel, visualLayerIndex, clip, asset, segment, options);

            var nextVideoLabel = $"vbase{visualLayerIndex + 1}";
            filterBuilder.Append(CultureInfo.InvariantCulture,
                $"[{currentVideoLabel}][v{visualLayerIndex}]overlay=x={FormatNumber(ResolveOverlayX(clip, options))}:y={FormatNumber(ResolveOverlayY(clip, options))}:eof_action=pass:shortest=0[{nextVideoLabel}];");

            currentVideoLabel = nextVideoLabel;
            visualLayerIndex++;
        }

        var audioLabels = new List<string>();
        for (var audioIndex = 0; audioIndex < audioClips.Count; audioIndex++)
        {
            var clipInfo = audioClips[audioIndex];
            var clip = clipInfo.Clip;
            var asset = clipInfo.Asset;
            var inputLabel = inputIndex++;
            var segment = ResolveExportSegment(clip, rangeStart, rangeEnd);
            AddInput(process, asset, segment.Duration);
            AppendAudioClipFilter(filterBuilder, inputLabel, audioIndex, clip, segment);
            audioLabels.Add($"[a{audioIndex}]");
        }

        var outputAudioLabel = "aout";
        if (audioLabels.Count == 0)
        {
            filterBuilder.Append(CultureInfo.InvariantCulture,
                $"anullsrc=channel_layout=stereo:sample_rate=48000:d={FormatSeconds(exportDuration)}[{outputAudioLabel}]");
        }
        else if (audioLabels.Count == 1)
        {
            filterBuilder.Append(CultureInfo.InvariantCulture,
                $"{audioLabels[0]}atrim=duration={FormatSeconds(exportDuration)}[{outputAudioLabel}]");
        }
        else
        {
            filterBuilder.Append(CultureInfo.InvariantCulture,
                $"{string.Concat(audioLabels)}amix=inputs={audioLabels.Count}:duration=longest:dropout_transition=0,atrim=duration={FormatSeconds(exportDuration)}[{outputAudioLabel}]");
        }

        process.ArgumentList.Add("-filter_complex");
        process.ArgumentList.Add(filterBuilder.ToString());
        process.ArgumentList.Add("-map");
        process.ArgumentList.Add($"[{currentVideoLabel}]");
        process.ArgumentList.Add("-map");
        process.ArgumentList.Add($"[{outputAudioLabel}]");
        process.ArgumentList.Add("-t");
        process.ArgumentList.Add(FormatSeconds(exportDuration));
        process.ArgumentList.Add("-r");
        process.ArgumentList.Add(options.FrameRate.ToString(CultureInfo.InvariantCulture));
        AddVideoCodecArguments(process, options);
        process.ArgumentList.Add("-pix_fmt");
        process.ArgumentList.Add("yuv420p");
        process.ArgumentList.Add("-c:a");
        process.ArgumentList.Add("aac");
        process.ArgumentList.Add("-b:a");
        process.ArgumentList.Add($"{options.AudioBitrateKbps}k");
        process.ArgumentList.Add("-progress");
        process.ArgumentList.Add("pipe:1");
        process.ArgumentList.Add("-nostats");
        process.ArgumentList.Add("-shortest");
        process.ArgumentList.Add(outputFilePath);

        await RunFfmpegAsync(process, exportDuration, progress, cancellationToken);
    }

    private static void AddVideoCodecArguments(ProcessStartInfo process, TimelineExportOptions options)
    {
        var normalizedCodec = options.VideoCodec.Trim().ToUpperInvariant();
        if (normalizedCodec is "H.265" or "HEVC" or "H265")
        {
            process.ArgumentList.Add("-c:v");
            process.ArgumentList.Add("libx265");
            process.ArgumentList.Add("-preset");
            process.ArgumentList.Add(options.Preset);
            process.ArgumentList.Add("-crf");
            process.ArgumentList.Add(options.ConstantRateFactor.ToString(CultureInfo.InvariantCulture));
            process.ArgumentList.Add("-tag:v");
            process.ArgumentList.Add("hvc1");
            return;
        }

        if (normalizedCodec is "MPEG-4" or "MPEG4")
        {
            process.ArgumentList.Add("-c:v");
            process.ArgumentList.Add("mpeg4");
            process.ArgumentList.Add("-q:v");
            process.ArgumentList.Add("4");
            return;
        }

        process.ArgumentList.Add("-c:v");
        process.ArgumentList.Add("libx264");
        process.ArgumentList.Add("-preset");
        process.ArgumentList.Add(options.Preset);
        process.ArgumentList.Add("-crf");
        process.ArgumentList.Add(options.ConstantRateFactor.ToString(CultureInfo.InvariantCulture));
    }

    private static void AddInput(ProcessStartInfo process, MediaAsset asset, TimeSpan duration)
    {
        if (asset.Type == MediaType.Image)
        {
            process.ArgumentList.Add("-loop");
            process.ArgumentList.Add("1");
            process.ArgumentList.Add("-t");
            process.ArgumentList.Add(FormatSeconds(duration));
        }

        process.ArgumentList.Add("-i");
        process.ArgumentList.Add(asset.FilePath);
    }

    private static void AppendVisualClipFilter(
        StringBuilder filterBuilder,
        int inputLabel,
        int visualLayerIndex,
        TimelineClip clip,
        MediaAsset asset,
        ExportClipSegment segment,
        TimelineExportOptions options)
    {
        var start = FormatSeconds(segment.OutputStart);
        var sourceStart = FormatSeconds(segment.SourceStart);
        var duration = FormatSeconds(segment.Duration);
        var scaleWidth = Math.Max(1, (int)Math.Round(options.Width * clip.FrameScale));
        var scaleHeight = Math.Max(1, (int)Math.Round(options.Height * clip.FrameScale));

        if (asset.Type == MediaType.Image)
        {
            filterBuilder.Append(CultureInfo.InvariantCulture,
                $"[{inputLabel}:v]format=rgba,scale={options.Width}:{options.Height}:force_original_aspect_ratio=decrease,pad={options.Width}:{options.Height}:(ow-iw)/2:(oh-ih)/2:color=black@0,fps={options.FrameRate},trim=duration={duration},setpts=PTS-STARTPTS+{start}/TB,scale={scaleWidth}:{scaleHeight}[v{visualLayerIndex}];");
            return;
        }

        filterBuilder.Append(CultureInfo.InvariantCulture,
            $"[{inputLabel}:v]trim=start={sourceStart}:duration={duration},setpts=PTS-STARTPTS+{start}/TB,format=rgba,scale={options.Width}:{options.Height}:force_original_aspect_ratio=decrease,pad={options.Width}:{options.Height}:(ow-iw)/2:(oh-ih)/2:color=black@0,fps={options.FrameRate},scale={scaleWidth}:{scaleHeight}[v{visualLayerIndex}];");
    }

    private static void AppendAudioClipFilter(
        StringBuilder filterBuilder,
        int inputLabel,
        int audioIndex,
        TimelineClip clip,
        ExportClipSegment segment)
    {
        var sourceStart = FormatSeconds(segment.SourceStart);
        var duration = FormatSeconds(segment.Duration);
        var delayMs = Math.Max(0, (int)Math.Round(segment.OutputStart.TotalMilliseconds));
        var volume = FormatNumber(Math.Clamp(clip.AudioVolume, 0, 2.0));

        filterBuilder.Append(CultureInfo.InvariantCulture,
            $"[{inputLabel}:a]atrim=start={sourceStart}:duration={duration},asetpts=PTS-STARTPTS,volume={volume},adelay={delayMs}:all=1[a{audioIndex}];");
    }

    private static async Task RunFfmpegAsync(
        ProcessStartInfo processStartInfo,
        TimeSpan exportDuration,
        IProgress<TimelineExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };

        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                stderr.AppendLine(args.Data);
        };
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                ReportProgress(args.Data, exportDuration, progress);
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Could not start FFmpeg process.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException("Could not start FFmpeg. Make sure ffmpeg is installed and available in PATH.", ex);
        }

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg export failed.{Environment.NewLine}{stderr}");

        progress?.Report(new TimelineExportProgress
        {
            Percent = 100,
            RenderedDuration = exportDuration
        });
    }

    private static void ReportProgress(
        string outputLine,
        TimeSpan exportDuration,
        IProgress<TimelineExportProgress>? progress)
    {
        if (progress is null || exportDuration <= TimeSpan.Zero)
            return;

        var separatorIndex = outputLine.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex <= 0)
            return;

        var key = outputLine[..separatorIndex];
        var value = outputLine[(separatorIndex + 1)..];
        TimeSpan renderedDuration;
        if (key.Equals("out_time_ms", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
            renderedDuration = TimeSpan.FromTicks(microseconds * 10);
        }
        else if (key.Equals("out_time", StringComparison.OrdinalIgnoreCase) &&
            TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsedDuration))
        {
            renderedDuration = parsedDuration;
        }
        else
        {
            return;
        }

        progress.Report(new TimelineExportProgress
        {
            Percent = Math.Clamp(renderedDuration.TotalSeconds / exportDuration.TotalSeconds * 100, 0, 100),
            RenderedDuration = renderedDuration
        });
    }

    private static string ResolveFfmpegExecutablePath()
    {
        var pathExecutable = FindExecutableInPath("ffmpeg.exe");
        if (!string.IsNullOrWhiteSpace(pathExecutable))
            return pathExecutable;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetPackagesPath = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(wingetPackagesPath))
            return "ffmpeg";

        return Directory
            .EnumerateFiles(wingetPackagesPath, "ffmpeg.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains("Gyan.FFmpeg", StringComparison.OrdinalIgnoreCase))
            ?? "ffmpeg";
    }

    private static string? FindExecutableInPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var executablePath = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(executablePath))
                return executablePath;
        }

        return null;
    }

    private static TimeSpan ResolveExportDuration(VideoProject project)
    {
        return project.Tracks
            .Where(x => x.IsEnabled)
            .SelectMany(x => x.Clips)
            .Select(x => x.TimelineEnd)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();
    }

    private static IEnumerable<(TimelineTrack Track, TimelineClip Clip, MediaAsset Asset)> ResolveVisualClips(
        VideoProject project,
        TimeSpan rangeStart,
        TimeSpan rangeEnd)
    {
        return project.Tracks
            .Where(x => x.IsEnabled && IsTrackOfKind(x, "Video"))
            .OrderBy(x => ExtractTrackIndex(x.Name))
            .SelectMany(track => track.Clips
                .OrderBy(clip => clip.TimelineStart)
                .Select(clip => new
                {
                    Track = track,
                    Clip = clip,
                    Asset = project.MediaAssets.FirstOrDefault(asset => asset.Id == clip.MediaAssetId)
                }))
            .Where(x => x.Asset is not null && x.Asset.Type != MediaType.Audio && IntersectsRange(x.Clip, rangeStart, rangeEnd))
            .Select(x => (x.Track, x.Clip, x.Asset!));
    }

    private static IEnumerable<(TimelineTrack Track, TimelineClip Clip, MediaAsset Asset)> ResolveAudioClips(
        VideoProject project,
        TimeSpan rangeStart,
        TimeSpan rangeEnd)
    {
        return project.Tracks
            .Where(x => x.IsEnabled && IsTrackOfKind(x, "Audio"))
            .SelectMany(track => track.Clips
                .OrderBy(clip => clip.TimelineStart)
                .Select(clip => new
                {
                    Track = track,
                    Clip = clip,
                    Asset = project.MediaAssets.FirstOrDefault(asset => asset.Id == clip.MediaAssetId)
                }))
            .Where(x => x.Asset is not null && x.Asset.Type != MediaType.Image && IntersectsRange(x.Clip, rangeStart, rangeEnd))
            .Select(x => (x.Track, x.Clip, x.Asset!));
    }

    private static bool IntersectsRange(TimelineClip clip, TimeSpan rangeStart, TimeSpan rangeEnd)
    {
        return clip.TimelineStart < rangeEnd && clip.TimelineEnd > rangeStart;
    }

    private static ExportClipSegment ResolveExportSegment(
        TimelineClip clip,
        TimeSpan rangeStart,
        TimeSpan rangeEnd)
    {
        var segmentTimelineStart = clip.TimelineStart > rangeStart ? clip.TimelineStart : rangeStart;
        var segmentTimelineEnd = clip.TimelineEnd < rangeEnd ? clip.TimelineEnd : rangeEnd;
        var sourceOffset = segmentTimelineStart - clip.TimelineStart;

        if (sourceOffset < TimeSpan.Zero)
            sourceOffset = TimeSpan.Zero;

        return new ExportClipSegment(
            clip.SourceStart + sourceOffset,
            segmentTimelineStart - rangeStart,
            segmentTimelineEnd - segmentTimelineStart);
    }

    private static bool IsTrackOfKind(TimelineTrack track, string kind)
    {
        return track.Name.StartsWith(kind, StringComparison.OrdinalIgnoreCase);
    }

    private static int ExtractTrackIndex(string trackName)
    {
        var lastPart = trackName.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return int.TryParse(lastPart, out var index) ? index : 1;
    }

    private static double ResolveOverlayX(TimelineClip clip, TimelineExportOptions options)
    {
        return clip.FrameX - (options.Width * clip.FrameScale - options.Width) / 2;
    }

    private static double ResolveOverlayY(TimelineClip clip, TimelineExportOptions options)
    {
        return clip.FrameY - (options.Height * clip.FrameScale - options.Height) / 2;
    }

    private static string FormatSeconds(TimeSpan value)
    {
        return FormatNumber(Math.Max(0, value.TotalSeconds));
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
