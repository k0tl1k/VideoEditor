namespace VideoEditor.Application.DI;

/// <summary>
/// 	Коллекция регистраций singleton-сервисов.
/// </summary>
public sealed class SimpleServiceCollection
{
    private readonly Dictionary<Type, Func<SimpleServiceProvider, object>> _registrations = new();

    /// <summary>
    /// 	Регистрирует singleton через фабрику.
    /// </summary>
    /// <param name="factory"> Фабрика создания сервиса. </param>
    /// <returns> Текущая коллекция регистраций. </returns>
    public SimpleServiceCollection AddSingleton<TService>(Func<SimpleServiceProvider, TService> factory)
        where TService : class
    {
        _registrations[typeof(TService)] = provider => factory(provider);
        return this;
    }

    /// <summary>
    /// 	Регистрирует singleton по типам сервиса и реализации.
    /// </summary>
    /// <returns> Текущая коллекция регистраций. </returns>
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
    /// <returns> Текущая коллекция регистраций. </returns>
    public SimpleServiceCollection AddSingleton<TService>()
        where TService : class
    {
        _registrations[typeof(TService)] = provider => provider.CreateInstance(typeof(TService));
        return this;
    }

    /// <summary>
    /// 	Создает провайдер сервисов.
    /// </summary>
    /// <returns> Провайдер сервисов. </returns>
    public SimpleServiceProvider BuildServiceProvider()
    {
        return new SimpleServiceProvider(_registrations);
    }
}
