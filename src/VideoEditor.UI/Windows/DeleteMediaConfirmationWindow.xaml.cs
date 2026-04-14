using System.Windows;

namespace VideoEditor.UI.Windows;

/// <summary>
/// 	Окно подтверждения удаления медиа.
/// </summary>
public partial class DeleteMediaConfirmationWindow : Window
{
    /// <summary>
    /// 	Создает окно подтверждения удаления медиа.
    /// </summary>
    /// <param name="headerText"> Заголовок окна. </param>
    /// <param name="subHeaderText"> Подзаголовок окна. </param>
    /// <param name="messageText"> Основной текст предупреждения. </param>
    /// <param name="detailsText"> Дополнительные детали. </param>
    public DeleteMediaConfirmationWindow(string headerText, string subHeaderText, string messageText, string detailsText)
    {
        InitializeComponent();
        HeaderText = headerText;
        SubHeaderText = subHeaderText;
        MessageText = messageText;
        DetailsText = detailsText;
        DataContext = this;
    }

    /// <summary>
    /// 	Заголовок окна.
    /// </summary>
    public string HeaderText { get; }

    /// <summary>
    /// 	Подзаголовок окна.
    /// </summary>
    public string SubHeaderText { get; }

    /// <summary>
    /// 	Основной текст предупреждения.
    /// </summary>
    public string MessageText { get; }

    /// <summary>
    /// 	Дополнительные детали.
    /// </summary>
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
