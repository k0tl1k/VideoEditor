using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace VideoEditor.UI.Converters;

public sealed class IconFileToImageSourceConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fileName = value as string ?? parameter as string;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return Cache.GetOrAdd(fileName, LoadIcon);
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static ImageSource? LoadIcon(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return LoadSvgIcon(path);
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource? LoadSvgIcon(string path)
    {
        try
        {
            var reader = new FileSvgReader(new WpfDrawingSettings());
            var drawing = reader.Read(path);
            if (drawing is null)
            {
                return null;
            }

            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
