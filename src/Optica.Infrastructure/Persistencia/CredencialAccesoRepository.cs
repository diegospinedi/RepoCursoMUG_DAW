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
}
