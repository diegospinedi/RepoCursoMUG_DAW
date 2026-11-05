using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Optica.Application.Comun;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Confirma lo que registraron los repositorios con un único <c>SaveChanges</c> del contexto compartido,
/// dentro de una transacción explícita: si algo falla antes del commit, no queda nada guardado. Las violaciones de
/// unicidad (motivo <see cref="MotivoConflicto.Unicidad"/>) y los tokens de concurrencia que no coinciden
/// (<see cref="DbUpdateConcurrencyException"/>, motivo <see cref="MotivoConflicto.Concurrencia"/>) se informan como
/// <see cref="ConflictoAlConfirmarException"/>, nunca con la excepción de EF como causa (D-18).
/// </summary>
public sealed class UnidadDeTrabajoEf(OpticaDbContext contexto) : IUnidadDeTrabajo
{
    // Códigos extendidos de SQLite: clave primaria y unicidad violadas, y CHECK.
    private const int SqliteRestriccionCheck = 275;
    private const int SqliteRestriccionClavePrimaria = 1555;
    private const int SqliteRestriccionUnica = 2067;

    /// <summary>
    /// El único CHECK que expresa unicidad (a lo sumo una credencial, <c>Id = 1</c>). Cualquier otro CHECK es
    /// un dato inválido, no un conflicto, y se propaga como error.
    /// </summary>
    private const string RestriccionCredencialUnica = "CK_CredencialAcceso_Id";

    public async Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        try
        {
            await using var transaccion = await contexto.Database.BeginTransactionAsync(cancelacion);
            await contexto.SaveChangesAsync(cancelacion);
            await transaccion.CommitAsync(cancelacion);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Un token de concurrencia no coincidió (la credencial, o la versión o el estado de un artículo, cambiaron o
            // se borraron después de leerlos): el UPDATE o DELETE afectó 0 filas y la transacción se revirtió. Lo
            // pendiente se descarta, como en cualquier conflicto. La causa NO se adjunta: sus Entries conservan el hash
            // anterior y el nuevo de la credencial, o los datos del artículo.
            contexto.ChangeTracker.Clear();
            throw new ConflictoAlConfirmarException(
                "Los datos a guardar cambiaron después de leerlos.", MotivoConflicto.Concurrencia);
        }
        catch (DbUpdateException error) when (EsConflicto(error))
        {
            // Lo pendiente no se guardó: se descarta para que el contexto no lo reintente. La causa tampoco se adjunta
            // acá: sus Entries llevan el hash de la credencial duplicada. Índices UNIQUE, clave primaria y el CHECK de
            // credencial única son todos unicidad (D-18).
            contexto.ChangeTracker.Clear();
            throw new ConflictoAlConfirmarException(
                "Los datos a guardar chocan con datos existentes.", MotivoConflicto.Unicidad);
        }
        catch
        {
            contexto.ChangeTracker.Clear();
            throw;
        }
    }

    private static bool EsConflicto(DbUpdateException error) =>
        error.InnerException is SqliteException sqlite
        && (sqlite.SqliteExtendedErrorCode is SqliteRestriccionClavePrimaria or SqliteRestriccionUnica
            || (sqlite.SqliteExtendedErrorCode == SqliteRestriccionCheck
                && sqlite.Message.Contains("CHECK constraint failed: " + RestriccionCredencialUnica, StringComparison.Ordinal)));
}
