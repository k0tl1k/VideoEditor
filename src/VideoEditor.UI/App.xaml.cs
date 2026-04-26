using System.Windows;
using VideoEditor.Application;
using VideoEditor.Application.Abstractions;
using VideoEditor.Application.DI;
using VideoEditor.Infrastructure;
using VideoEditor.UI.ViewModels;
using VideoEditor.UI.Windows;

namespace VideoEditor.UI;

/// <summary>
/// 	Точка входа WPF-приложения.
/// </summary>
public partial class App : System.Windows.Application
{
    private SimpleServiceProvider? _serviceProvider;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new SimpleServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();

        var startupWindow = new StartupWindow(_serviceProvider.GetRequiredService<IRecentProjectService>());
        if (startupWindow.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        if (mainWindow.DataContext is MainWindowViewModel viewModel)
        {
            if (startupWindow.ShouldCreateNewProject)
            {
                viewModel.CreateNewProject(startupWindow.ProjectName);
            }
            else if (!string.IsNullOrWhiteSpace(startupWindow.ProjectFilePath) &&
                     !viewModel.LoadProjectFromFile(startupWindow.ProjectFilePath))
            {
                Shutdown();
                return;
            }
        }

        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(SimpleServiceCollection services)
    {
        services.AddApplicationServices();
        services.AddInfrastructureServices();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
}

