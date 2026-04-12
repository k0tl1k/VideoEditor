using System.Windows;

namespace VideoEditor.UI.Windows;

public partial class DeleteMediaConfirmationWindow : Window
{
    public DeleteMediaConfirmationWindow(string headerText, string subHeaderText, string messageText, string detailsText)
    {
        InitializeComponent();
        HeaderText = headerText;
        SubHeaderText = subHeaderText;
        MessageText = messageText;
        DetailsText = detailsText;
        DataContext = this;
    }

    public string HeaderText { get; }

    public string SubHeaderText { get; }

    public string MessageText { get; }

    public string DetailsText { get; }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
