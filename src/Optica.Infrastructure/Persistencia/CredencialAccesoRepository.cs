using Microsoft.EntityFrameworkCore;
using Optica.Domain.Acceso;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Credencial de acceso sobre la tabla <c>CredencialAcceso</c> (fila única con <c>Id = 1</c>). Mapea la
/// entidad de dominio a <see cref="CredencialAccesoFila"/>; <see cref="Agregar"/> solo deja el cambio en el
/// contexto compartido, que confirma <see cref="UnidadDeTrabajoEf"/>.
/// </summary>
public sealed class CredencialAccesoRepository(OpticaDbContext contexto) : ICredencialAccesoRepository
{
    /// <summary>Clave de la única fila permitida por <c>CHECK (Id = 1)</c>.</summary>
    public const int IdUnico = 1;

    public Task<bool> ExisteAsync(CancellationToken cancelacion = default) =>
        contexto.CredencialesAcceso.AsNoTracking().AnyAsync(c => c.Id == IdUnico, cancelacion);

    public async Task<CredencialAcceso?> ObtenerAsync(CancellationToken cancelacion = default)
    {
        var fila = await contexto.CredencialesAcceso.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == IdUnico, cancelacion);
        return fila is null ? null : new CredencialAcceso(fila.Hash, fila.DefinidaEn);
    }

    public void Agregar(CredencialAcceso credencial)
    {
        ArgumentNullException.ThrowIfNull(credencial);
        contexto.CredencialesAcceso.Add(new CredencialAccesoFila
        {
            Id = IdUnico,
            Hash = credencial.Hash,
            DefinidaEn = credencial.DefinidaEn,
        });
    }

    /// <summary>
    /// Deja la fila marcada como modificada en el contexto, con el hash de <paramref name="anterior"/> como valor
    /// original: como <see cref="CredencialAccesoFila.Hash"/> es token de concurrencia, el <c>UPDATE</c> que emite
    /// <see cref="UnidadDeTrabajoEf"/> filtra por <c>Id = 1 AND Hash = @anterior</c>. Si otra operación cambió o borró la
    /// credencial, afecta 0 filas, EF lanza <see cref="DbUpdateConcurrencyException"/> y la unidad de trabajo lo
    /// informa como conflicto. Sin <c>ExecuteUpdate</c>, que se ejecutaría fuera de esa transacción.
    /// </summary>
    public void Reemplazar(CredencialAcceso anterior, CredencialAcceso nueva)
    {
        ArgumentNullException.ThrowIfNull(anterior);
        ArgumentNullException.ThrowIfNull(nueva);
        if (ReferenceEquals(anterior, nueva))
        {
            // Ya modificada, el WHERE compararía contra el hash nuevo y siempre daría conflicto.
            throw new ArgumentException(
                "La credencial nueva tiene que ser otra instancia que la leída.", nameof(nueva));
        }

        // ObtenerAsync lee sin seguimiento; si la fila ya está en el contexto (por ejemplo, recién agregada o
        // reemplazada y confirmada), se reutiliza esa entrada en lugar de adjuntar otra con la misma clave.
        var entrada = contexto.CredencialesAcceso.Local.FindEntry(IdUnico)
            ?? contexto.CredencialesAcceso.Attach(new CredencialAccesoFila
            {
                Id = IdUnico,
                Hash = anterior.Hash,
                DefinidaEn = anterior.DefinidaEn,
            });

        // El valor original es el hash de anterior, no el que tenga la entrada: si el contexto seguía una versión
        // vieja de la fila y anterior se releyó después, el WHERE tiene que comparar contra lo releído.
        var hash = entrada.Property(c => c.Hash);
        hash.OriginalValue = anterior.Hash;
        hash.CurrentValue = nueva.Hash;
        hash.IsModified = true;
        var definidaEn = entrada.Property(c => c.DefinidaEn);
        definidaEn.CurrentValue = nueva.DefinidaEn;
        definidaEn.IsModified = true;
    }
}
