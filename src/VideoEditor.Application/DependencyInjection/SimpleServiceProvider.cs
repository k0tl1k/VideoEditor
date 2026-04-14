using System.Collections.Concurrent;
using System.Reflection;

namespace VideoEditor.Application.DI;

/// <summary>
/// 	Простой DI-контейнер с singleton и constructor injection.
/// </summary>
public sealed class SimpleServiceProvider : IServiceProvider, IDisposable
{
    private readonly IReadOnlyDictionary<Type, Func<SimpleServiceProvider, object>> _registrations;
    private readonly ConcurrentDictionary<Type, object> _singletons = new();

    /// <summary>
    /// 	Создает провайдер сервисов из регистраций.
    /// </summary>
    /// <param name="registrations"> Регистрации сервисов. </param>
    public SimpleServiceProvider(IReadOnlyDictionary<Type, Func<SimpleServiceProvider, object>> registrations)
    {
        _registrations = new Dictionary<Type, Func<SimpleServiceProvider, object>>(registrations);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        if (_singletons.TryGetValue(serviceType, out var existing))
            return existing;

        if (_registrations.TryGetValue(serviceType, out var factory))
        {
            var created = factory(this);
            _singletons[serviceType] = created;
            return created;
        }

        if (!serviceType.IsAbstract && !serviceType.IsInterface)
        {
            var created = CreateInstance(serviceType);
            _singletons[serviceType] = created;
            return created;
        }

        return null;
    }

    /// <summary>
    /// 	Возвращает сервис или бросает исключение.
    /// </summary>
    /// <returns> Зарегистрированный сервис. </returns>
    public T GetRequiredService<T>() where T : class
    {
        return GetService(typeof(T)) as T
               ?? throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered.");
    }

    /// <summary>
    /// 	Создает экземпляр типа через самый полный публичный конструктор.
    /// </summary>
    /// <param name="implementationType"> Тип реализации. </param>
    /// <returns> Созданный экземпляр. </returns>
    public object CreateInstance(Type implementationType)
    {
        var constructor = implementationType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Type '{implementationType.FullName}' has no public constructor.");

        var parameters = constructor.GetParameters();
        if (parameters.Length == 0)
            return Activator.CreateInstance(implementationType)
                   ?? throw new InvalidOperationException($"Cannot instantiate '{implementationType.FullName}'.");

        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var resolved = GetService(parameters[i].ParameterType);
            args[i] = resolved ?? throw new InvalidOperationException(
                $"Cannot resolve '{parameters[i].ParameterType.FullName}' for '{implementationType.FullName}'.");
        }

        return constructor.Invoke(args);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var singleton in _singletons.Values)
        {
            if (singleton is IDisposable disposable)
                disposable.Dispose();
        }

        _singletons.Clear();
    }
}
