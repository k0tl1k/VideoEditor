using System.Windows;
using VideoEditor.Application;
using VideoEditor.Application.DI;
using VideoEditor.Infrastructure;
using VideoEditor.UI.ViewModels;

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

        var services = new SimpleServiceCollection();
        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
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

