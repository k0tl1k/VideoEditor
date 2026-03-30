using VideoEditor.Application.Abstractions;
using VideoEditor.Application.DI;
using VideoEditor.Application.Services;

namespace VideoEditor.Application;

/// <summary>
/// 	Р РµРіРёСЃС‚СЂР°С†РёСЏ СЃРµСЂРІРёСЃРѕРІ СЃР»РѕСЏ РїСЂРёР»РѕР¶РµРЅРёСЏ.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 	Р”РѕР±Р°РІР»СЏРµС‚ СЃРµСЂРІРёСЃС‹ use-case СЃР»РѕСЏ.
    /// </summary>
    public static SimpleServiceCollection AddApplicationServices(this SimpleServiceCollection services)
    {
        services.AddSingleton<IProjectBootstrapService, ProjectBootstrapService>();
        return services;
    }
}

