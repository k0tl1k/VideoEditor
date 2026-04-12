using System.Windows;
using System.Windows.Input;

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

    private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore drag failures when a control is handling the mouse interaction.
        }
    }
}
