using System.Globalization;
using System.Windows.Input;
using System.Windows.Data;

namespace VideoEditor.UI.Converters;

public sealed class BoolToTimelineCursorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isRazor = value is bool boolValue && boolValue;
        return isRazor ? Cursors.Cross : Cursors.SizeAll;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
