using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Optica.Application.Comun;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Confirma lo que registraron los repositorios con un único <c>SaveChanges</c> del contexto compartido,
/// dentro de una transacción explícita: si algo falla antes del commit, no queda nada guardado. Las violaciones de
/// unicidad (motivo <see cref="MotivoConflicto.Unicidad"/>) y los tokens de concurrencia que no coinciden
/// (<see cref="DbUpdateConcurrencyException"/>, motivo <see cref="MotivoConflicto.Concurrencia"/>) se informan como
/// <see cref="ConflictoAlConfirmarException"/>, nunca con la excepción de EF como causa (D-18); también el rechazo de un
/// trigger de cierre (motivo <see cref="MotivoConflicto.RegistroCerrado"/>, ADR-017).
/// </summary>
public sealed class UnidadDeTrabajoEf(OpticaDbContext contexto) : IUnidadDeTrabajo
{
    // Códigos extendidos de SQLite: clave primaria y unicidad violadas, y CHECK.
    private const int SqliteRestriccionCheck = 275;
    private const int SqliteRestriccionClavePrimaria = 1555;
    private const int SqliteRestriccionUnica = 2067;

    // RAISE(ABORT, ...) dentro de un trigger: SQLITE_CONSTRAINT_TRIGGER.
    private const int SqliteRestriccionTrigger = 1811;

    /// <summary>El texto con el que aborta un trigger que protege un registro cerrado (convención de ADR-017).</summary>
    private const string TextoRegistroCerrado = "registro_cerrado";

    /// <summary>
    /// El único CHECK que expresa unicidad (a lo sumo una credencial, <c>Id = 1</c>). Cualquier otro CHECK es
    /// un dato inválido, no un conflicto, y se propaga como error.
    /// </summary>
    private const string RestriccionCredencialUnica = "CK_CredencialAcceso_Id";

    private const string MensajeAnidada = "La operación en transacción no se anida.";

    private const string MensajeCambiosSinConfirmar =
        "La operación en transacción terminó con cambios registrados sin confirmar.";

    /// <summary>
    /// Abre la transacción con <c>BeginTransactionAsync</c>, que en Microsoft.Data.Sqlite emite <c>BEGIN IMMEDIATE</c>:
    /// toma la escritura antes de la primera lectura de la operación (ADR-013). Si la operación termina sin cambios
    /// pendientes en el contexto, confirma. Si lanza, si deja cambios registrados sin confirmar o si falla el commit,
    /// revierte explícitamente (sin el token de la operación), descarta lo pendiente del contexto y propaga la excepción
    /// original sin envolver, aunque la reversión falle.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// El contexto ya tiene una transacción en curso (la operación no se anida, D-19), o la operación terminó con cambios
    /// registrados sin confirmar. Los mensajes son fijos y no llevan valores.
    /// </exception>
    public async Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        if (contexto.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(MensajeAnidada);
        }

        await using var transaccion = await contexto.Database.BeginTransactionAsync(cancelacion);
        try
        {
            var resultado = await operacion(cancelacion);

            // Lo registrado y no confirmado se perdería en silencio al confirmar: es un error de programación.
            if (contexto.ChangeTracker.HasChanges())
            {
                throw new InvalidOperationException(MensajeCambiosSinConfirmar);
            }

            await transaccion.CommitAsync(cancelacion);
            return resultado;
        }
        catch
        {
            await RevertirSinReemplazarLaFallaAsync(transaccion);
            contexto.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>
    /// Revierte explícitamente, sin el token de la operación: un pedido cancelado no debe impedir la reversión. Si la
    /// reversión falla, la falla se descarta para no reemplazar la excepción original; la transacción igual se revierte al
    /// descartarse sin confirmar, y EF Core deja constancia del error en su log de transacciones.
    /// </summary>
    private static async Task RevertirSinReemplazarLaFallaAsync(IDbContextTransaction transaccion)
    {
        try
        {
            await transaccion.RollbackAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Intencional: la excepción que importa es la de la operación, que el llamador relanza.
        }
    }

    /// <summary>
    /// Fuera de una operación, graba en su propia transacción y la confirma. Dentro de
    /// <see cref="EjecutarEnTransaccionAsync"/> reutiliza <c>Database.CurrentTransaction</c> y solo llama a
    /// <c>SaveChanges</c>, que graba bajo un savepoint automático de EF Core: si falla, vuelve a ese savepoint y la
    /// transacción de la operación sigue abierta (D-18 de FEAT-002c).
    /// </summary>
    public async Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        try
        {
            if (contexto.Database.CurrentTransaction is not null)
            {
                await contexto.SaveChangesAsync(cancelacion);
                return;
            }

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
        catch (DbUpdateException error) when (EsRegistroCerrado(error))
        {
            // Un trigger de cierre rechazó la escritura de un registro cerrado (un presupuesto en Final o sus líneas,
            // ADR-017). Lo pendiente se descarta y la causa no se adjunta: sus Entries llevan los datos del cliente.
            contexto.ChangeTracker.Clear();
            throw new ConflictoAlConfirmarException(
                "Los datos a guardar pertenecen a un registro cerrado.", MotivoConflicto.RegistroCerrado);
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

    /// <summary>
    /// <c>RAISE(ABORT, 'registro_cerrado')</c> de un trigger: código extendido <c>SQLITE_CONSTRAINT_TRIGGER</c> y el texto
    /// de la convención. Otro trigger con otro texto (la fecha y el número del presupuesto) no es un conflicto.
    /// </summary>
    private static bool EsRegistroCerrado(DbUpdateException error) =>
        error.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteRestriccionTrigger } sqlite
        && sqlite.Message.Contains("'" + TextoRegistroCerrado + "'", StringComparison.Ordinal);

    private static bool EsConflicto(DbUpdateException error) =>
        error.InnerException is SqliteException sqlite
        && (sqlite.SqliteExtendedErrorCode is SqliteRestriccionClavePrimaria or SqliteRestriccionUnica
            || (sqlite.SqliteExtendedErrorCode == SqliteRestriccionCheck
                && sqlite.Message.Contains("CHECK constraint failed: " + RestriccionCredencialUnica, StringComparison.Ordinal)));
}
