namespace VideoEditor.Domain.Entities;

/// <summary>
/// 	Базовая сущность доменной модели.
/// </summary>
public abstract class EntityBase
{
    /// <summary>
    /// 	Уникальный идентификатор сущности.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
}
