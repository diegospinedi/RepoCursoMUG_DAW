namespace Optica.Application.Comun;

/// <summary>
/// Confirma en una sola transacción todo lo que registraron los repositorios: se guarda todo o nada.
/// </summary>
public interface IUnidadDeTrabajo
{
    /// <exception cref="ConflictoAlConfirmarException">
    /// Lo guardado choca con datos que ya existen: clave primaria o única duplicada, o la restricción de
    /// credencial única (<c>CK_CredencialAcceso_Id</c>: otra credencial grabada en paralelo o una segunda
    /// credencial). Cualquier otra falla, incluida otra restricción CHECK, se propaga como error y no se guarda
    /// nada.
    /// </exception>
    Task ConfirmarAsync(CancellationToken cancelacion = default);
}

/// <summary>
/// La confirmación chocó con datos que ya existen (clave duplicada o credencial de más); no se guardó nada.
/// </summary>
public sealed class ConflictoAlConfirmarException : Exception
{
    public ConflictoAlConfirmarException(string mensaje, Exception? causa = null)
        : base(mensaje, causa)
    {
    }
}
