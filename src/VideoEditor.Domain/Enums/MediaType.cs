namespace VideoEditor.Domain.Enums;

/// <summary>
/// 	Тип импортированного медиафайла.
/// </summary>
public enum MediaType
{
    /// <summary>
    /// 	Видеофайл (основной контент на таймлайне).
    /// </summary>
    Video = 0,

    /// <summary>
    /// 	Аудиофайл (музыка, озвучка, звуковые эффекты).
    /// </summary>
    Audio = 1,

    /// <summary>
    /// 	Статичное изображение.
    /// </summary>
    Image = 2
}
