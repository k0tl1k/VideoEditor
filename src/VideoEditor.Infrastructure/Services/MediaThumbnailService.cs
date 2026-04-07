using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using VideoEditor.Application.Abstractions;
using VideoEditor.Domain.Entities;
using VideoEditor.Domain.Enums;

namespace VideoEditor.Infrastructure.Services;

/// <summary>
/// 	Строит миниатюры для изображений и видео.
/// </summary>
public sealed class MediaThumbnailService : IMediaThumbnailService
{
    /// <inheritdoc />
    public string? GetThumbnailPath(MediaAsset asset)
    {
        if (!File.Exists(asset.FilePath))
            return null;

        if (asset.Type == MediaType.Image)
            return asset.FilePath;

        if (asset.Type != MediaType.Video)
            return null;

        return BuildVideoThumbnail(asset.FilePath);
    }

    private static string? BuildVideoThumbnail(string videoPath)
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoEditor",
            "Thumbnails");

        Directory.CreateDirectory(cacheDir);

        var stamp = File.GetLastWriteTimeUtc(videoPath).Ticks;
        var targetPath = Path.Combine(cacheDir, $"{ComputeHash($"{videoPath}|{stamp}")}.jpg");

        if (File.Exists(targetPath))
            return targetPath;

        if (TryBuildShellThumbnail(videoPath, targetPath))
            return targetPath;

        var ffmpegPath = ResolveFfmpegExecutable();
        if (!string.IsNullOrWhiteSpace(ffmpegPath) && TryBuildFfmpegThumbnail(ffmpegPath, videoPath, targetPath))
            return targetPath;

        return null;
    }

    private static bool TryBuildShellThumbnail(string videoPath, string targetPath)
    {
        try
        {
            var shellItemGuid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(videoPath, IntPtr.Zero, ref shellItemGuid, out var factory);

            factory.GetImage(new SizeStruct(320, 180),
                SIIGBF.ThumbnailOnly | SIIGBF.BiggerSizeOk | SIIGBF.ResizeToFit,
                out var hBitmap);

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                source.Freeze();

                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));

                using var stream = File.Create(targetPath);
                encoder.Save(stream);

                return File.Exists(targetPath);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBuildFfmpegThumbnail(string ffmpegPath, string videoPath, string targetPath)
    {
        var seekPoints = new[] { "00:00:00", "00:00:00.200", "00:00:01" };

        foreach (var seekPoint in seekPoints)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -hide_banner -loglevel error -ss {seekPoint} -i \"{videoPath}\" -frames:v 1 -vf \"scale=320:-1\" \"{targetPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            try
            {
                using var process = Process.Start(startInfo);
                process?.WaitForExit(5000);

                if (File.Exists(targetPath))
                    return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
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
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory imageFactory);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x0,
        BiggerSizeOk = 0x1,
        MemoryOnly = 0x2,
        IconOnly = 0x4,
        ThumbnailOnly = 0x8,
        InCacheOnly = 0x10
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SizeStruct
    {
        public SizeStruct(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        void GetImage(SizeStruct size, SIIGBF flags, out IntPtr imageHandle);
    }
}

