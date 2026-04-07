using VideoEditor.Application.Abstractions;
using VideoEditor.Application.DI;
using VideoEditor.Infrastructure.Services;

namespace VideoEditor.Infrastructure;

/// <summary>
/// 	Регистрирует сервисы инфраструктуры.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 	Добавляет инфраструктурные реализации.
    /// </summary>
    /// <param name="services"> Коллекция сервисов. </param>
    /// <returns> Обновлённая коллекция сервисов. </returns>
    public static SimpleServiceCollection AddInfrastructureServices(this SimpleServiceCollection services)
    {
        services.AddSingleton<IProjectPathService, FileSystemProjectPathService>();
        services.AddSingleton<IMediaDurationService, MediaDurationService>();
        services.AddSingleton<IMediaThumbnailService, MediaThumbnailService>();
        return services;
    }
}
