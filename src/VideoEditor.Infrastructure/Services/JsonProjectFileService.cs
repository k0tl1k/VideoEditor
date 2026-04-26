using System.IO;
using System.Text.Json;
using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;
using VideoEditor.Domain.Enums;

namespace VideoEditor.Infrastructure.Services;

/// <summary>
///     Persists projects as a small versioned JSON document.
/// </summary>
public sealed class JsonProjectFileService : IProjectFileService
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public void Save(VideoProject project, string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var document = ProjectFileDocument.FromProject(project);
        File.WriteAllText(filePath, JsonSerializer.Serialize(document, JsonOptions));
    }

    public VideoProject Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Project file was not found.", filePath);

        var document = JsonSerializer.Deserialize<ProjectFileDocument>(File.ReadAllText(filePath), JsonOptions)
            ?? throw new InvalidOperationException("Project file is empty or corrupted.");

        if (document.FormatVersion <= 0 || document.FormatVersion > CurrentFormatVersion)
            throw new InvalidOperationException($"Unsupported project file version: {document.FormatVersion}.");

        return document.ToProject();
    }

    private sealed record ProjectFileDocument(
        int FormatVersion,
        Guid Id,
        string Name,
        DateTime CreatedAtUtc,
        IReadOnlyList<MediaAssetDto> MediaAssets,
        IReadOnlyList<TimelineTrackDto> Tracks)
    {
        public static ProjectFileDocument FromProject(VideoProject project)
        {
            return new ProjectFileDocument(
                CurrentFormatVersion,
                project.Id,
                project.Name,
                project.CreatedAtUtc,
                project.MediaAssets
                    .Select(asset => new MediaAssetDto(
                        asset.Id,
                        asset.FilePath,
                        asset.DisplayName,
                        asset.Type,
                        asset.Duration))
                    .ToList(),
                project.Tracks
                    .Select(track => new TimelineTrackDto(
                        track.Id,
                        track.Name,
                        track.IsEnabled,
                        track.Clips.Select(TimelineClipDto.FromClip).ToList()))
                    .ToList());
        }

        public VideoProject ToProject()
        {
            var project = new VideoProject
            {
                Id = Id,
                Name = string.IsNullOrWhiteSpace(Name) ? "Loaded Project" : Name,
                CreatedAtUtc = CreatedAtUtc == default ? DateTime.UtcNow : CreatedAtUtc
            };

            foreach (var asset in MediaAssets ?? Array.Empty<MediaAssetDto>())
            {
                project.MediaAssets.Add(new MediaAsset
                {
                    Id = asset.Id == Guid.Empty ? Guid.NewGuid() : asset.Id,
                    FilePath = asset.FilePath ?? string.Empty,
                    DisplayName = string.IsNullOrWhiteSpace(asset.DisplayName)
                        ? Path.GetFileName(asset.FilePath ?? string.Empty)
                        : asset.DisplayName,
                    Type = asset.Type,
                    Duration = asset.Duration < TimeSpan.Zero ? TimeSpan.Zero : asset.Duration
                });
            }

            foreach (var trackDto in Tracks ?? Array.Empty<TimelineTrackDto>())
            {
                var track = new TimelineTrack
                {
                    Id = trackDto.Id == Guid.Empty ? Guid.NewGuid() : trackDto.Id,
                    Name = string.IsNullOrWhiteSpace(trackDto.Name) ? "Video Track 1" : trackDto.Name,
                    IsEnabled = trackDto.IsEnabled
                };

                foreach (var clipDto in trackDto.Clips ?? Array.Empty<TimelineClipDto>())
                    track.Clips.Add(clipDto.ToClip());

                project.Tracks.Add(track);
            }

            return project;
        }
    }

    private sealed record MediaAssetDto(
        Guid Id,
        string? FilePath,
        string? DisplayName,
        MediaType Type,
        TimeSpan Duration);

    private sealed record TimelineTrackDto(
        Guid Id,
        string? Name,
        bool IsEnabled,
        IReadOnlyList<TimelineClipDto>? Clips);

    private sealed record TimelineClipDto(
        Guid Id,
        Guid MediaAssetId,
        Guid? LinkedGroupId,
        TimeSpan SourceStart,
        TimeSpan SourceDuration,
        TimeSpan TrimBaselineSourceStart,
        TimeSpan TrimBaselineSourceDuration,
        TimeSpan TimelineStart,
        double FrameX,
        double FrameY,
        double FrameScale,
        double AudioVolume,
        bool MuteEmbeddedAudio)
    {
        public static TimelineClipDto FromClip(TimelineClip clip)
        {
            return new TimelineClipDto(
                clip.Id,
                clip.MediaAssetId,
                clip.LinkedGroupId,
                clip.SourceStart,
                clip.SourceDuration,
                clip.TrimBaselineSourceStart,
                clip.TrimBaselineSourceDuration,
                clip.TimelineStart,
                clip.FrameX,
                clip.FrameY,
                clip.FrameScale,
                clip.AudioVolume,
                clip.MuteEmbeddedAudio);
        }

        public TimelineClip ToClip()
        {
            return new TimelineClip
            {
                Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
                MediaAssetId = MediaAssetId,
                LinkedGroupId = LinkedGroupId,
                SourceStart = SourceStart < TimeSpan.Zero ? TimeSpan.Zero : SourceStart,
                SourceDuration = SourceDuration < TimeSpan.Zero ? TimeSpan.Zero : SourceDuration,
                TrimBaselineSourceStart = TrimBaselineSourceStart < TimeSpan.Zero ? TimeSpan.Zero : TrimBaselineSourceStart,
                TrimBaselineSourceDuration = TrimBaselineSourceDuration < TimeSpan.Zero ? TimeSpan.Zero : TrimBaselineSourceDuration,
                TimelineStart = TimelineStart < TimeSpan.Zero ? TimeSpan.Zero : TimelineStart,
                FrameX = FrameX,
                FrameY = FrameY,
                FrameScale = FrameScale <= 0 ? 1.0 : FrameScale,
                AudioVolume = Math.Clamp(AudioVolume <= 0 ? 1.0 : AudioVolume, 0, 2.0),
                MuteEmbeddedAudio = MuteEmbeddedAudio
            };
        }
    }
}
