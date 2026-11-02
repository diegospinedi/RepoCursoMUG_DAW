using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Optica.Application.Comun;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Confirma lo que registraron los repositorios con un único <c>SaveChanges</c> del contexto compartido,
/// dentro de una transacción explícita: si algo falla antes del commit, no queda nada guardado.
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
        catch (DbUpdateException error) when (EsConflicto(error))
        {
            // Lo pendiente no se guardó: se descarta para que el contexto no lo reintente.
            contexto.ChangeTracker.Clear();
            throw new ConflictoAlConfirmarException("Los datos a guardar chocan con datos existentes.", error);
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
