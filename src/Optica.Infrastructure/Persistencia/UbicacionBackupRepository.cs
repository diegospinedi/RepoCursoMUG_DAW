using Microsoft.EntityFrameworkCore;
using Optica.Domain.Backup;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// La ubicación del backup sobre la tabla <c>ConfiguracionBackup</c> (fila única con <c>Id = 1</c>, FEAT-010). La
/// migración no inserta fila: hasta la primera grabación no hay ubicación. <see cref="Reemplazar"/> solo deja el cambio
/// en el contexto compartido, que confirma <see cref="UnidadDeTrabajoEf"/>. La ruta nunca va a un mensaje (D-12).
/// </summary>
public sealed class UbicacionBackupRepository(OpticaDbContext contexto) : IUbicacionBackupRepository
{
    /// <summary>Clave de la única fila permitida por <c>CHECK (Id = 1)</c>.</summary>
    public const int IdUnico = 1;

    private const string MensajeFilaInvalida = "La ubicación del backup guardada no es válida.";

    /// <summary>Lee la fila sin seguimiento en cada llamada, sin caché; null si no hay fila.</summary>
    /// <exception cref="InvalidOperationException">
    /// La fila no cumple las reglas de lectura (editada a mano: una ruta relativa pasa el CHECK): estado corrupto, con
    /// texto fijo y sin la ruta.
    /// </exception>
    public async Task<UbicacionBackup?> ObtenerAsync(CancellationToken cancelacion = default)
    {
        var fila = await contexto.ConfiguracionesBackup.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == IdUnico, cancelacion);
        if (fila is null)
        {
            return null;
        }

        try
        {
            return UbicacionBackup.Leer(fila.Ruta);
        }
        catch (ArgumentException error)
        {
            // La causa nombra el campo y no el valor (invariante del dominio), así que se puede adjuntar.
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }
    }

    /// <summary>
    /// Inserta la fila si no existe o le asigna la ruta, sobre la entrada que ya esté en el contexto o leída con
    /// seguimiento. Sobre una fila existente la ruta queda marcada siempre como modificada (como en
    /// <see cref="DatosEmisorRepository"/>): el <c>UPDATE</c> la lleva aunque la entrada reutilizada ya tuviera ese valor
    /// y otro alcance haya grabado otro en el medio. Sin token de concurrencia: la última grabación gana.
    /// </summary>
    public void Reemplazar(UbicacionBackup ubicacion)
    {
        ArgumentNullException.ThrowIfNull(ubicacion);

        var fila = contexto.ConfiguracionesBackup.Local.FindEntry(IdUnico)?.Entity
            ?? contexto.ConfiguracionesBackup.SingleOrDefault(c => c.Id == IdUnico);
        if (fila is null)
        {
            contexto.ConfiguracionesBackup.Add(new ConfiguracionBackupFila { Id = IdUnico, Ruta = ubicacion.Ruta });
            return;
        }

        var ruta = contexto.Entry(fila).Property(c => c.Ruta);
        ruta.CurrentValue = ubicacion.Ruta;
        ruta.IsModified = true;
    }
}
