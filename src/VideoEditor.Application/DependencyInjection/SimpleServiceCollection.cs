namespace VideoEditor.Application.DI;

/// <summary>
/// 	Коллекция регистраций сервисов (singleton).
/// </summary>
public sealed class SimpleServiceCollection
{
    private readonly Dictionary<Type, Func<SimpleServiceProvider, object>> _registrations = new();

    /// <summary>
    /// 	Регистрирует singleton по фабрике.
    /// </summary>
    public SimpleServiceCollection AddSingleton<TService>(Func<SimpleServiceProvider, TService> factory)
        where TService : class
    {
        _registrations[typeof(TService)] = provider => factory(provider);
        return this;
    }

    /// <summary>
    /// 	Регистрирует singleton по типам сервиса и реализации.
    /// </summary>
    public SimpleServiceCollection AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        _registrations[typeof(TService)] = provider => provider.CreateInstance(typeof(TImplementation));
        return this;
    }

    /// <summary>
    /// 	Регистрирует singleton по конкретному типу.
    /// </summary>
    public SimpleServiceCollection AddSingleton<TService>()
        where TService : class
    {
        _registrations[typeof(TService)] = provider => provider.CreateInstance(typeof(TService));
        return this;
    }

    /// <summary>
    /// 	Строит провайдер сервисов.
    /// </summary>
    public SimpleServiceProvider BuildServiceProvider()
    {
        return new SimpleServiceProvider(_registrations);
    }
}
