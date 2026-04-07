using VideoEditor.Domain.Entities;

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

    public string? ThumbnailPath { get; }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);
}
