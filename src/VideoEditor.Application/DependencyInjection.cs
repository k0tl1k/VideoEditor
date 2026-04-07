using VideoEditor.Application.Abstractions;
using VideoEditor.Application.DI;
using VideoEditor.Application.Services;

namespace VideoEditor.Application;

/// <summary>
/// 	Регистрирует сервисы слоя приложения.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 	Добавляет сервисы слоя приложения.
    /// </summary>
    /// <param name="services"> Коллекция сервисов. </param>
    /// <returns> Обновлённая коллекция сервисов. </returns>
    public static SimpleServiceCollection AddApplicationServices(this SimpleServiceCollection services)
    {
        services.AddSingleton<IProjectBootstrapService, ProjectBootstrapService>();
        services.AddSingleton<IMediaImportService, MediaImportService>();
        return services;
    }
}
