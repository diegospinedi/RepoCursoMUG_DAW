namespace Optica.Application.Comun;

/// <summary>
/// Confirma en una sola transacción todo lo que registraron los repositorios: se guarda todo o nada.
/// </summary>
public interface IUnidadDeTrabajo
{
    /// <summary>
    /// Ejecuta <paramref name="operacion"/> dentro de una transacción de escritura que abarca sus lecturas y su
    /// grabación (ADR-013, D-05): en SQLite, <c>BEGIN IMMEDIATE</c>, así ninguna otra escritura confirma entre lo que la
    /// operación lee y lo que graba. Si la operación termina, la transacción se confirma y se devuelve su resultado; si
    /// lanza, se revierte todo lo que grabó, se descarta lo pendiente y la excepción se propaga tal cual.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dentro de la operación, <see cref="ConfirmarAsync"/> solo graba lo registrado: no abre ni confirma otra
    /// transacción, y lo grabado queda visible para otros recién cuando la operación termina. Un conflicto se informa
    /// con <see cref="ConflictoAlConfirmarException"/> y los mismos motivos que fuera de una operación; lo que intentó
    /// grabar esa confirmación no queda, y si la operación atiende la excepción y termina normalmente, la transacción se
    /// confirma sin esos cambios (D-18).
    /// </para>
    /// <para>
    /// La operación no se anida: llamar a este método desde dentro de una operación lanza
    /// <see cref="InvalidOperationException"/>. Nada dentro de la operación debe abrir otra conexión que escriba en la
    /// base: esperaría la transacción de la propia operación hasta agotar el tiempo de espera (D-19).
    /// </para>
    /// <para>
    /// Todo lo que la operación registra lo confirma ella con <see cref="ConfirmarAsync"/> antes de terminar. Si termina
    /// con cambios registrados sin confirmar, no se confirma nada: se revierte todo y se lanza
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// La reversión no depende de <paramref name="cancelacion"/>: un pedido cancelado durante la operación igual revierte,
    /// y si la reversión misma falla, la excepción que se propaga es la original de la operación.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="operacion"/> es null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Ya hay una operación en curso en esta unidad de trabajo, o la operación terminó con cambios registrados sin
    /// confirmar. Los mensajes no llevan valores.
    /// </exception>
    Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default);

    /// <summary>
    /// Fuera de <see cref="EjecutarEnTransaccionAsync"/>, graba lo registrado en su propia transacción y la confirma.
    /// Dentro de una operación solo graba: confirma o revierte la operación (ADR-013).
    /// </summary>
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
