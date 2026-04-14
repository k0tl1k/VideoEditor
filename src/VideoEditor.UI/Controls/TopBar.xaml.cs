using System.Windows;
using System.Windows.Controls;
using VideoEditor.UI.Windows;

namespace VideoEditor.UI.Controls;

/// <summary>
/// 	Верхняя панель с основными действиями редактора.
/// </summary>
public partial class TopBar : UserControl
{
    /// <summary>
    /// 	Создает верхнюю панель.
    /// </summary>
    public TopBar()
    {
        InitializeComponent();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ExportSettingsWindow
        {
            Owner = Window.GetWindow(this),
            DataContext = DataContext
        };

        window.ShowDialog();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow
        {
            Owner = Window.GetWindow(this)
        };

        window.ShowDialog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow
        {
            Owner = Window.GetWindow(this),
            DataContext = DataContext
        };

        window.ShowDialog();
    }
}
