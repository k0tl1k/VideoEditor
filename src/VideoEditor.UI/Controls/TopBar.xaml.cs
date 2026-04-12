using System.Windows;
using System.Windows.Controls;
using VideoEditor.UI.Windows;

namespace VideoEditor.UI.Controls;

public partial class TopBar : UserControl
{
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
}
