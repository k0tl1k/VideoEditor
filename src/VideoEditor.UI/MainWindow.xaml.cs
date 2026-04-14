using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VideoEditor.UI.Converters;
using VideoEditor.UI.ViewModels;

namespace VideoEditor.UI;

/// <summary>
/// 	Главное окно приложения.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 	Создает главное окно.
    /// </summary>
    /// <param name="viewModel"> Модель представления главного окна. </param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Icon = LoadWindowIcon();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseWheel += MainWindow_PreviewMouseWheel;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsTextInputFocused())
            return;

        if (TimelinePanelControl.HandleTimelineKeyDown(e.Key))
            e.Handled = true;
    }

    private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TimelinePanelControl.HandleTimelineMouseWheel(e.Delta))
            e.Handled = true;
    }

    private static ImageSource? LoadWindowIcon()
    {
        var converter = new IconFileToImageSourceConverter();
        return converter.Convert("app-logo.svg", typeof(ImageSource), string.Empty, CultureInfo.InvariantCulture) as ImageSource;
    }

    private static bool IsTextInputFocused()
    {
        return Keyboard.FocusedElement is TextBoxBase;
    }
}
