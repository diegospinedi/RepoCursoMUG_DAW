using Optica.Domain.Backup;

namespace Optica.Application.Backup;

/// <summary>
/// La ubicación del backup vigente, en la forma del contrato de la Api (FEAT-010): la ruta absoluta, o null mientras
/// nunca se eligió.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra la ruta (<c>UbicacionBackupVigente { }</c>): puede llevar el nombre de una
/// persona (D-12, R-05) y no llega al log por esa vía.
/// </remarks>
public sealed record UbicacionBackupVigente(string? Ruta)
{
    /// <summary>Sin ubicación elegida.</summary>
    public static UbicacionBackupVigente SinElegir { get; } = new((string?)null);

    /// <exception cref="ArgumentNullException"><paramref name="ubicacion"/> es null.</exception>
    public static UbicacionBackupVigente Desde(UbicacionBackup ubicacion)
    {
        ArgumentNullException.ThrowIfNull(ubicacion);
        return new(ubicacion.Ruta);
    }

    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Consulta la ubicación del backup leyendo el repositorio en cada llamada, sin caché. Sin ubicación devuelve
/// <see cref="UbicacionBackupVigente.SinElegir"/>; una fila corrupta propaga la <see cref="InvalidOperationException"/>
/// del repositorio (texto fijo, sin la ruta), que la Api responde con su 500 genérico.
/// </summary>
public sealed class ObtenerUbicacionBackup(IUbicacionBackupRepository ubicaciones)
{
    public async Task<UbicacionBackupVigente> EjecutarAsync(CancellationToken cancelacion = default) =>
        await ubicaciones.ObtenerAsync(cancelacion) is { } ubicacion
            ? UbicacionBackupVigente.Desde(ubicacion)
            : UbicacionBackupVigente.SinElegir;
}
