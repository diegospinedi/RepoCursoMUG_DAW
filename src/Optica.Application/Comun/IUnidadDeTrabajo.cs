namespace Optica.Application.Comun;

/// <summary>
/// Confirma en una sola transacción todo lo que registraron los repositorios: se guarda todo o nada.
/// </summary>
public interface IUnidadDeTrabajo
{
    /// <exception cref="ConflictoAlConfirmarException">
    /// Con <see cref="MotivoConflicto.Unicidad"/>, lo guardado choca con datos que ya existen: clave primaria o única
    /// duplicada (por ejemplo, el código en el proveedor de otro artículo) o la restricción de credencial única
    /// (<c>CK_CredencialAcceso_Id</c>: otra credencial grabada en paralelo o una segunda credencial). Con
    /// <see cref="MotivoConflicto.Concurrencia"/>, lo que se reemplaza cambió o se borró después de leerlo: la credencial
    /// por otro cambio de contraseña o un restablecimiento, o un artículo cuya versión o estado ya no son los leídos
    /// (D-08, D-22). Cualquier otra falla, incluida otra restricción CHECK, se propaga como error y no se guarda nada.
    /// </exception>
    Task ConfirmarAsync(CancellationToken cancelacion = default);
}

/// <summary>Por qué no se pudo confirmar (D-08, D-18).</summary>
public enum MotivoConflicto
{
    /// <summary>Lo que se reemplaza cambió o se borró después de leerlo (token de concurrencia).</summary>
    Concurrencia,

    /// <summary>Lo guardado repite una clave primaria o única, o la credencial única.</summary>
    Unicidad,
}

/// <summary>
/// La confirmación chocó con datos que ya existen (clave duplicada o credencial de más) o que cambiaron después de
/// leerlos; no se guardó nada. <see cref="Motivo"/> dice cuál de los dos (D-18).
/// </summary>
/// <remarks>
/// El tipo no admite causa: su único constructor recibe el mensaje y el motivo, así que con él el compilador impide
/// adjuntarle la excepción de EF; lo que impide que vuelva un constructor con causa (público o no) es la prueba de
/// regresión <c>ConflictoAlConfirmar_NoAdmiteCausa_NingunConstructorRecibeExcepcion</c>. Eso importa porque ni la
/// <c>DbUpdateConcurrencyException</c> de un conflicto de concurrencia (sus <c>Entries</c> conservan el hash anterior
/// y el nuevo de la credencial, o los datos del artículo) ni la <c>DbUpdateException</c> de una unicidad o del CHECK de
/// credencial única (sus <c>Entries</c> llevan el hash de la credencial duplicada) pueden llegar al log, y una excepción
/// interna termina en el log con todo lo que lleva. El mensaje es genérico y <see cref="Exception.InnerException"/> es
/// siempre <c>null</c>. No agregues un constructor con causa.
/// </remarks>
public sealed class ConflictoAlConfirmarException : Exception
{
    /// <exception cref="ArgumentOutOfRangeException">El motivo no es un valor de <see cref="MotivoConflicto"/>.</exception>
    public ConflictoAlConfirmarException(string mensaje, MotivoConflicto motivo)
        : base(mensaje)
    {
        if (!Enum.IsDefined(motivo))
        {
            throw new ArgumentOutOfRangeException(nameof(motivo), "El motivo del conflicto no es válido.");
        }

        Motivo = motivo;
    }

    /// <summary>Concurrencia (lo leído cambió) o unicidad (lo guardado choca con datos existentes).</summary>
    public MotivoConflicto Motivo { get; }
}
