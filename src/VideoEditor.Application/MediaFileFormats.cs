using VideoEditor.Domain.Enums;

namespace VideoEditor.Application;

/// <summary>
/// 	Хранит поддерживаемые форматы медиафайлов.
/// </summary>
public static class MediaFileFormats
{
    public static readonly IReadOnlyList<string> VideoExtensions =
    [
        ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v"
    ];

    public static readonly IReadOnlyList<string> AudioExtensions =
    [
        ".mp3", ".wav", ".aac", ".flac", ".ogg", ".m4a"
    ];

    public static readonly IReadOnlyList<string> ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    ];

    public static string OpenFileDialogFilter =>
        $"Media files|{BuildExtensionsFilter()}|All files|*.*";

    /// <summary>
    /// 	Определяет тип медиа по расширению файла.
    /// </summary>
    /// <param name="extension"> Расширение файла. </param>
    /// <returns> Тип медиафайла. </returns>
    public static MediaType ResolveMediaType(string extension)
    {
        if (Contains(VideoExtensions, extension))
            return MediaType.Video;

        if (Contains(AudioExtensions, extension))
            return MediaType.Audio;

        if (Contains(ImageExtensions, extension))
            return MediaType.Image;

        return MediaType.Video;
    }

    /// <summary>
    ///     Checks whether the file extension is supported by the editor import pipeline.
    /// </summary>
    public static bool IsSupportedExtension(string extension)
    {
        return Contains(VideoExtensions, extension) ||
               Contains(AudioExtensions, extension) ||
               Contains(ImageExtensions, extension);
    }

    private static bool Contains(IEnumerable<string> extensions, string extension)
    {
        return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildExtensionsFilter()
    {
        return string.Join(";",
            VideoExtensions.Concat(AudioExtensions).Concat(ImageExtensions).Select(x => $"*{x}"));
    }
}
