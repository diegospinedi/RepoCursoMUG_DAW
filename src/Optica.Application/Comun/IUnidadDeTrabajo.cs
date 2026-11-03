namespace Optica.Application.Comun;

/// <summary>
/// Confirma en una sola transacción todo lo que registraron los repositorios: se guarda todo o nada.
/// </summary>
public interface IUnidadDeTrabajo
{
    /// <exception cref="ConflictoAlConfirmarException">
    /// Lo guardado choca con datos que ya existen: clave primaria o única duplicada, la restricción de credencial
    /// única (<c>CK_CredencialAcceso_Id</c>: otra credencial grabada en paralelo o una segunda credencial), o un
    /// conflicto de concurrencia (lo que se reemplaza cambió o se borró después de leerlo, por ejemplo la credencial
    /// por otro cambio de contraseña o un restablecimiento). Cualquier otra falla, incluida otra restricción CHECK, se
    /// propaga como error y no se guarda nada.
    /// </exception>
    Task ConfirmarAsync(CancellationToken cancelacion = default);
}

/// <summary>
/// La confirmación chocó con datos que ya existen (clave duplicada o credencial de más) o que cambiaron después de
/// leerlos; no se guardó nada.
/// </summary>
/// <remarks>
/// El tipo no admite causa: su único constructor recibe solo el mensaje, así que con él el compilador impide
/// adjuntarle la excepción de EF; lo que impide que vuelva un constructor con causa (público o no) es la prueba de
/// regresión <c>ConflictoAlConfirmar_NoAdmiteCausa_NingunConstructorRecibeExcepcion</c>. Eso importa porque ni la
/// <c>DbUpdateConcurrencyException</c> de un conflicto de concurrencia (sus <c>Entries</c> conservan el hash anterior
/// y el nuevo de la credencial) ni la <c>DbUpdateException</c> de una unicidad o del CHECK de credencial única (sus
/// <c>Entries</c> llevan el hash de la credencial duplicada) pueden llegar al log, y una excepción interna termina en
/// el log con todo lo que lleva. El mensaje es genérico y <see cref="Exception.InnerException"/> es siempre
/// <c>null</c>. No agregues un constructor con causa.
/// </remarks>
public sealed class ConflictoAlConfirmarException : Exception
{
    public ConflictoAlConfirmarException(string mensaje)
        : base(mensaje)
    {
    }
}
