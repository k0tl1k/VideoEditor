using System.Windows;

namespace VideoEditor.UI.Windows;

public partial class ExportSettingsWindow : Window
{
    public ExportSettingsWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
