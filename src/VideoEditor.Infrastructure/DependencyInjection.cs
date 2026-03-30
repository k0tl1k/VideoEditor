using VideoEditor.Application.Abstractions;
using VideoEditor.Application.DI;
using VideoEditor.Infrastructure.Services;

namespace VideoEditor.Infrastructure;

/// <summary>
/// 	Р РµРіРёСЃС‚СЂР°С†РёСЏ СЃРµСЂРІРёСЃРѕРІ РёРЅС„СЂР°СЃС‚СЂСѓРєС‚СѓСЂС‹.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 	Р”РѕР±Р°РІР»СЏРµС‚ РёРЅС„СЂР°СЃС‚СЂСѓРєС‚СѓСЂРЅС‹Рµ СЂРµР°Р»РёР·Р°С†РёРё.
    /// </summary>
    public static SimpleServiceCollection AddInfrastructureServices(this SimpleServiceCollection services)
    {
        services.AddSingleton<IProjectPathService, FileSystemProjectPathService>();
        return services;
    }
}

