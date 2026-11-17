using Optica.Application.Comun;
using Optica.Domain.Backup;

namespace Optica.Application.Backup;

/// <summary>Por qué se rechaza una copia antes de tocar la base (FR-13, D-28).</summary>
public enum RechazoCopia
{
    /// <summary>El archivo no se llama <c>optica-AAAA-MM-DD.db</c> (con una fecha que existe).</summary>
    NombreInvalido,

    /// <summary>No hay un archivo con esa ruta.</summary>
    Inexistente,

    /// <summary>No se abre como base o no pasa <c>PRAGMA integrity_check</c>.</summary>
    Danada,

    /// <summary>
    /// No tiene el historial de migraciones, lo tiene vacío o no es un prefijo de las migraciones de esta versión: no es
    /// una base de la óptica, o está alterada.
    /// </summary>
    Ajena,

    /// <summary>Tiene migraciones que esta versión no conoce: la hizo una versión más nueva de la aplicación.</summary>
    VersionMasNueva,
}

/// <summary>
/// El resultado de verificar una copia: el rechazo (null si sirve), el nombre del archivo y su fecha (null si el nombre no
/// tiene el patrón: un nombre cualquiera puede llevar el de una persona, D-12), si la copia es de una versión anterior
/// (la Api la actualiza al arrancar, D-28) y cuántas facturas con CAE de la base actual son posteriores a la última de la
/// copia (null si no se pudieron contar o si la copia se rechazó).
/// </summary>
public sealed record VerificacionCopia(
    RechazoCopia? Rechazo, string? NombreArchivo, DateOnly? Fecha, bool RequiereActualizar, int? FacturasPosteriores)
{
    /// <summary>Verdadero si la copia se puede restaurar.</summary>
    public bool Valida => Rechazo is null;
}

/// <summary>En qué paso falló la restauración.</summary>
public enum FallaRestauracion
{
    /// <summary>La verificación de la copia lanzó una excepción: no se tocó nada.</summary>
    Verificacion,

    /// <summary>La copia no pasó la verificación (<see cref="VerificacionCopia.Rechazo"/>): no se tocó nada.</summary>
    CopiaRechazada,

    /// <summary>La restauración al temporal falló o el temporal no pasó la verificación: la base actual no se tocó.</summary>
    Temporal,

    /// <summary>Falló el intercambio: la base actual volvió a su lugar y el temporal se descartó.</summary>
    Intercambio,

    /// <summary>
    /// Falló el intercambio y tampoco se pudo devolver la base actual: quedó en
    /// <see cref="ResultadoRestauracion.CarpetaAnterior"/> y la restaurada, como temporal en la carpeta de datos.
    /// </summary>
    VueltaAtras,
}

/// <summary>
/// El resultado de una restauración: la verificación de la copia, la carpeta (relativa a la carpeta de datos) donde quedó
/// la base anterior, el paso que falló (null si se restauró) y el tipo de la excepción, nunca su texto (D-12).
/// </summary>
public sealed record ResultadoRestauracion(
    VerificacionCopia Copia,
    string? CarpetaAnterior,
    FallaRestauracion? Falla,
    string? TipoError,
    string? TipoErrorClaves = null)
{
    /// <summary>Verdadero si la base quedó restaurada.</summary>
    public bool Hecha => Falla is null;
}

/// <summary>
/// Restaura la base desde una copia de backup (FR-13 de FEAT-010, D-27 a D-29, ADR-030). Lo usa solo el comando de
/// consola <c>restaurar-backup</c>, con la Api detenida y el candado de instancia tomado: no hay ruta de la Api ni
/// pantalla que restaure (FR-14, AC-15).
/// </summary>
/// <remarks>
/// <para>
/// La verificación (<see cref="VerificarAsync"/>) exige, en orden, el nombre exacto de una copia
/// (<see cref="CopiaBackup.TryLeerFecha"/>), que el archivo exista, que pase la comprobación de integridad y que su
/// historial de migraciones sea un prefijo, no vacío, de las migraciones de esta versión: una copia con migraciones
/// desconocidas es de una versión más nueva, y una sin historial o con huecos no es de la óptica (D-28). Recién con la copia
/// válida cuenta las facturas con CAE de la base actual posteriores a la última de la copia (R-10), que el comando informa
/// antes de pedir la confirmación.
/// </para>
/// <para>
/// La restauración (<see cref="RestaurarAsync"/>) vuelve a verificar la copia (pudo cambiar mientras se esperaba la
/// confirmación), la restaura a un temporal verificado y recién entonces aparta la base actual y pone la restaurada
/// (D-29). Si el temporal falla, lo descarta sin tocar la base; si falla el intercambio, el restaurador ya devolvió la base
/// actual y el temporal se descarta; si tampoco se pudo devolverla, el temporal no se borra (puede ser la única base
/// completa que queda en la carpeta de datos). El temporal se juzga con la misma regla que la copia (también los triggers
/// de protección). Con la base restaurada, renueva las claves de sesión (<see cref="IDatosLocales.RenovarClaves"/>), como
/// el restablecimiento de la contraseña: si eso falla, la restauración no se deshace y se informa el tipo de la falla. No
/// migra (la Api migra al arrancar) y no escribe datos en la base.
/// </para>
/// <para>
/// Ningún resultado lleva rutas completas ni el texto de una excepción: solo el nombre de la copia (que tiene el patrón
/// fijo), su fecha, la carpeta anterior relativa a la carpeta de datos y el tipo de la excepción (D-12).
/// </para>
/// </remarks>
public sealed class RestaurarBackup(IRestauradorBase restaurador, IDatosLocales datos)
{
    private readonly IRestauradorBase _restaurador = restaurador ?? throw new ArgumentNullException(nameof(restaurador));
    private readonly IDatosLocales _datos = datos ?? throw new ArgumentNullException(nameof(datos));

    /// <summary>Verifica la copia de <paramref name="rutaCopia"/> sin tocar la base actual.</summary>
    public async Task<VerificacionCopia> VerificarAsync(string rutaCopia, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(rutaCopia);
        var nombre = Path.GetFileName(rutaCopia);
        if (!CopiaBackup.TryLeerFecha(nombre, out var fecha))
        {
            return new VerificacionCopia(RechazoCopia.NombreInvalido, null, null, false, null);
        }

        var inspeccion = await _restaurador.InspeccionarCopiaAsync(rutaCopia, cancelacion);
        var rechazo = Rechazo(inspeccion, _restaurador.MigracionesConocidas());
        if (rechazo is not null)
        {
            return new VerificacionCopia(rechazo, nombre, fecha, false, null);
        }

        var requiereActualizar = inspeccion.Migraciones!.Count < _restaurador.MigracionesConocidas().Count;
        var posteriores = await _restaurador.ContarFacturasPosterioresAsync(inspeccion.UltimaFactura, cancelacion);
        return new VerificacionCopia(null, nombre, fecha, requiereActualizar, posteriores);
    }

    /// <summary>
    /// Restaura la base desde la copia de <paramref name="rutaCopia"/>. Solo con la Api detenida y el candado de instancia
    /// tomado por quien llama.
    /// </summary>
    public async Task<ResultadoRestauracion> RestaurarAsync(string rutaCopia, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(rutaCopia);
        VerificacionCopia copia;
        try
        {
            copia = await VerificarAsync(rutaCopia, cancelacion);
        }
        catch (Exception error)
        {
            // Antes de tocar nada (ni el temporal): la base actual no se modificó. La verificación queda sin rechazo, con
            // el nombre y la fecha solo si el nombre tiene el patrón.
            var nombre = Path.GetFileName(rutaCopia);
            var valido = CopiaBackup.TryLeerFecha(nombre, out var fecha);
            return new ResultadoRestauracion(
                new VerificacionCopia(null, valido ? nombre : null, valido ? fecha : null, false, null),
                null,
                FallaRestauracion.Verificacion,
                error.GetType().Name);
        }

        if (!copia.Valida)
        {
            return new ResultadoRestauracion(copia, null, FallaRestauracion.CopiaRechazada, null);
        }

        try
        {
            // El temporal se juzga con la misma regla que la copia (D-28, triggers incluidos): la copia pudo cambiar
            // entre la verificación y la restauración.
            var temporal = await _restaurador.RestaurarATemporalAsync(rutaCopia, cancelacion);
            if (Rechazo(temporal, _restaurador.MigracionesConocidas()) is { } rechazo)
            {
                Descartar();
                return new ResultadoRestauracion(copia with { Rechazo = rechazo }, null, FallaRestauracion.CopiaRechazada, null);
            }
        }
        catch (Exception error)
        {
            // También la cancelación: el temporal a medias se descarta y la base actual no se tocó.
            Descartar();
            return new ResultadoRestauracion(copia, null, FallaRestauracion.Temporal, error.GetType().Name);
        }

        // Desde acá no se cancela: el intercambio son unos renombres, y cortarlo a la mitad es lo que hay que evitar.
        string anterior;
        try
        {
            anterior = _restaurador.ApartarYPonerRestaurada();
        }
        catch (VueltaAtrasFallidaException error)
        {
            return new ResultadoRestauracion(
                copia, error.CarpetaAnterior, FallaRestauracion.VueltaAtras, (error.InnerException ?? error).GetType().Name);
        }
        catch (Exception error)
        {
            Descartar();
            return new ResultadoRestauracion(copia, null, FallaRestauracion.Intercambio, error.GetType().Name);
        }

        // Como el restablecimiento: las cookies emitidas con las claves de antes dejan de valer, porque la base restaurada
        // trae la contraseña vigente a la fecha de la copia. Una falla acá no deshace la restauración: se informa su tipo.
        try
        {
            _datos.RenovarClaves();
        }
        catch (Exception error)
        {
            return new ResultadoRestauracion(copia, anterior, null, null, error.GetType().Name);
        }

        return new ResultadoRestauracion(copia, anterior, null, null);
    }

    /// <summary>El rechazo de una copia ya inspeccionada, o null si se puede restaurar (D-28).</summary>
    private static RechazoCopia? Rechazo(InspeccionCopia inspeccion, IReadOnlyList<string> conocidas)
    {
        if (!inspeccion.Existe)
        {
            return RechazoCopia.Inexistente;
        }

        if (!inspeccion.Integra)
        {
            return RechazoCopia.Danada;
        }

        if (inspeccion.Migraciones is not { Count: > 0 } migraciones)
        {
            return RechazoCopia.Ajena;
        }

        if (migraciones.Any(migracion => !conocidas.Contains(migracion, StringComparer.Ordinal)))
        {
            return RechazoCopia.VersionMasNueva;
        }

        var ordenadas = migraciones.Order(StringComparer.Ordinal).ToList();
        if (!ordenadas.SequenceEqual(conocidas.Take(ordenadas.Count), StringComparer.Ordinal))
        {
            return RechazoCopia.Ajena;
        }

        // Sin un trigger de registros cerrados o de solo agregar que crean las migraciones de su historial, la base
        // restaurada dejaría modificar un presupuesto en Final o una factura con CAE (AGENTS.md): no es una copia intacta.
        return inspeccion.TriggersCompletos ? null : RechazoCopia.Ajena;
    }

    /// <summary>Descarta el temporal; si tampoco se puede, queda para la próxima restauración, que lo borra antes de empezar.</summary>
    private void Descartar()
    {
        try
        {
            _restaurador.DescartarTemporal();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Intencional: el temporal no es la base; RestaurarATemporalAsync lo pisa en el próximo intento.
        }
    }
}
