using Optica.Domain.Backup;

namespace Optica.Application.Backup;

/// <summary>
/// El estado del backup para el aviso al ingresar (FR-09 de FEAT-010), en la forma del contrato de
/// <c>GET /api/backup/estado</c>: si falta el backup de ayer, la fecha del último backup verificado (null si no hay
/// ninguno), si hay una ubicación elegida (<see cref="UbicacionElegida"/> o <see cref="UbicacionSinElegir"/>) y si esa
/// ubicación existe y se puede leer. Nunca lleva la ruta (D-12): puede tener el nombre de una persona.
/// </summary>
public sealed record EstadoBackupVigente(bool FaltaAyer, DateOnly? Ultimo, string Ubicacion, bool Accesible)
{
    /// <summary>Hay una ubicación de backup elegida.</summary>
    public const string UbicacionElegida = "elegida";

    /// <summary>Nunca se eligió una ubicación de backup.</summary>
    public const string UbicacionSinElegir = "sin_elegir";
}

/// <summary>
/// Consulta el estado del backup (FR-09, AC-09, AC-10, AC-18 de FEAT-010), leyendo el historial y la ubicación en cada
/// llamada, sin caché.
/// </summary>
/// <remarks>
/// <para>
/// "Falta el de ayer" es la regla compartida con el intento al arrancar (<see cref="ReglaBackupDiario"/>, D-20): una
/// copia de hoy cubre la de ayer (D-11). El último backup es la mayor fecha del historial que no pasa de hoy (D-21), en
/// la zona local del <see cref="TimeProvider"/>; sale del historial y no de la carpeta, así que se conoce aunque la
/// ubicación no esté accesible (AC-18).
/// </para>
/// <para>
/// La ubicación es accesible si su carpeta existe y se puede leer (<see cref="IAlmacenCopias.SePuedeLeer"/>, que nunca
/// lanza): una carpeta que no existe (pendrive desconectado) o que no se puede leer responde falso, nunca una falla.
/// Sin ubicación elegida, falso y no se consulta el almacén. Una fila corrupta del historial o de la ubicación propaga la <see cref="InvalidOperationException"/> del
/// repositorio (texto fijo, sin la ruta), que la Api responde con su 500 genérico.
/// </para>
/// </remarks>
public sealed class ObtenerEstadoBackup(
    IHistorialBackupRepository historial,
    IUbicacionBackupRepository ubicaciones,
    IAlmacenCopias almacen,
    TimeProvider reloj)
{
    public async Task<EstadoBackupVigente> EjecutarAsync(CancellationToken cancelacion = default)
    {
        var hoy = ReglaBackupDiario.Hoy(reloj);
        var ultimo = await historial.UltimaFechaHastaAsync(hoy, cancelacion);
        var ubicacion = await ubicaciones.ObtenerAsync(cancelacion);
        return new EstadoBackupVigente(
            ReglaBackupDiario.FaltaLaDeAyer(ultimo, hoy),
            ultimo,
            ubicacion is null ? EstadoBackupVigente.UbicacionSinElegir : EstadoBackupVigente.UbicacionElegida,
            ubicacion is not null && almacen.SePuedeLeer(ubicacion.Ruta));
    }
}
