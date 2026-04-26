using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using VideoEditor.Application.Abstractions;

namespace VideoEditor.UI.Windows;

public partial class StartupWindow : Window
{
    public StartupWindow(IRecentProjectService recentProjectService)
    {
        RecentProjects = new ObservableCollection<RecentProjectItem>(recentProjectService.GetRecentProjects());
        InitializeComponent();
        DataContext = this;
        NoRecentProjectsTextBlock.Visibility = RecentProjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public ObservableCollection<RecentProjectItem> RecentProjects { get; }

    public bool ShouldCreateNewProject { get; private set; } = true;

    public string ProjectName { get; private set; } = "Diploma Project";

    public string? ProjectFilePath { get; private set; }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldCreateNewProject = true;
        ProjectName = string.IsNullOrWhiteSpace(ProjectNameTextBox.Text)
            ? "New Project"
            : ProjectNameTextBox.Text.Trim();
        DialogResult = true;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "VideoEditor project|*.vedproj;*.json|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        ShouldCreateNewProject = false;
        ProjectFilePath = dialog.FileName;
        SelectedProjectTextBlock.Text = Path.GetFileName(dialog.FileName);
        DialogResult = true;
    }

    private void OpenRecentButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedRecentProject();
    }

    private void RecentProjectsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenSelectedRecentProject();
    }

    private void OpenSelectedRecentProject()
    {
        if (RecentProjectsListBox.SelectedItem is not RecentProjectItem recentProject)
            return;

        ShouldCreateNewProject = false;
        ProjectFilePath = recentProject.FilePath;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
