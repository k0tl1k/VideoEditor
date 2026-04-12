using VideoEditor.Domain.Entities;
using VideoEditor.Domain.Enums;

namespace VideoEditor.UI.ViewModels;

/// <summary>
/// 	Элемент списка импортированных медиафайлов.
/// </summary>
public sealed class ImportedMediaItemViewModel
{
    public ImportedMediaItemViewModel(MediaAsset asset, string? thumbnailPath)
    {
        Asset = asset;
        ThumbnailPath = thumbnailPath;
    }

    public MediaAsset Asset { get; }

    public string DisplayName => Asset.DisplayName;

    public string TypeLabel => Asset.Type.ToString();

    public bool IsAudio => Asset.Type == MediaType.Audio;

    public string? TypeIconFileName => IsAudio ? "sound.svg" : null;

    public string? ThumbnailPath { get; }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);
}
