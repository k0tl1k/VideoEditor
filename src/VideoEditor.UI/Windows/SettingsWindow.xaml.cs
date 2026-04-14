using System.Windows;

namespace VideoEditor.UI.Windows;

/// <summary>
/// 	Окно настроек приложения.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// 	Создает окно настроек.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 	Список текущих горячих клавиш.
    /// </summary>
    public IReadOnlyList<HotkeyRow> HotkeyRows { get; } =
    [
        new("Tools", "V", "Move tool"),
        new(null, "C", "Razor tool"),
        new("Timeline", "Ctrl + C", "Copy selected clips"),
        new(null, "Ctrl + V", "Paste copied clips at Playhead"),
        new(null, "Ctrl + A", "Select all timeline clips"),
        new(null, "Delete", "Delete selected clips"),
        new(null, "Ctrl + Wheel", "Zoom timeline around Playhead"),
        new(null, "Alt + Wheel", "Scroll timeline horizontally"),
        new(null, "Alt + Left/Right", "Step timeline horizontally")
    ];

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RootBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>
    /// 	Строка списка горячих клавиш.
    /// </summary>
    public sealed record HotkeyRow(string? Section, string Gesture, string Description)
    {
        /// <summary>
        /// 	Показывает, нужно ли вывести заголовок секции.
        /// </summary>
        public bool HasSection => !string.IsNullOrWhiteSpace(Section);
    }
}
