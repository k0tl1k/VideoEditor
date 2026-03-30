using System.Windows;
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
    }
}