using System.Globalization;
using System.Windows;
using System.Windows.Media;
using VideoEditor.UI.Converters;
using VideoEditor.UI.ViewModels;

namespace VideoEditor.UI;

/// <summary>
/// 	Логика взаимодействия для MainWindow.xaml.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 	Создает экземпляр главного окна.
    /// </summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Icon = LoadWindowIcon();
    }

    private static ImageSource? LoadWindowIcon()
    {
        var converter = new IconFileToImageSourceConverter();
        return converter.Convert("app-logo.svg", typeof(ImageSource), string.Empty, CultureInfo.InvariantCulture) as ImageSource;
    }
}
