using Microsoft.EntityFrameworkCore;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// El registro de descartes sobre la tabla <c>DescarteEmision</c> (FEAT-008, FR-10, ADR-026). Solo se agrega: la base
/// rechaza modificar o borrar un descarte (<c>TR_DescarteEmision_SinModificar</c>, <c>TR_DescarteEmision_SinBorrar</c>).
/// <see cref="Agregar"/> solo registra el alta en el contexto compartido, que confirma <see cref="UnidadDeTrabajoEf"/>.
/// Sin datos del cliente; no registra nada en el log.
/// </summary>
public sealed class DescarteEmisionRepository(OpticaDbContext contexto) : IDescarteEmisionRepository
{
    private const string MensajeFilaInvalida = "Un descarte de emisión guardado no es válido.";

    /// <exception cref="ArgumentNullException"><paramref name="descarte"/> es null.</exception>
    public void Agregar(DescarteEmision descarte)
    {
        ArgumentNullException.ThrowIfNull(descarte);
        contexto.DescartesEmision.Add(new DescarteEmisionFila
        {
            Momento = descarte.Momento.UtcDateTime,
            PresupuestoNumero = descarte.PresupuestoNumero,
            Tipo = TextosFacturacion.TextoDe(descarte.Tipo),
            PuntoVenta = descarte.PuntoVenta,
            Numero = descarte.Numero,
            Total = descarte.TotalRegistrado,
            TotalArca = descarte.TotalArca,
            Motivo = TextosFacturacion.TextoDe(descarte.Motivo),
            Entorno = TextosFacturacion.TextoDe(descarte.Entorno),
        });
    }

    /// <exception cref="InvalidOperationException">Una fila no es válida. El mensaje es fijo y no lleva datos.</exception>
    public async Task<IReadOnlyList<DescarteEmision>> ListarDescartesAsync(
        long presupuestoNumero, CancellationToken cancelacion = default)
    {
        var filas = await contexto.DescartesEmision.AsNoTracking()
            .Where(d => d.PresupuestoNumero == presupuestoNumero)
            .OrderBy(d => d.Id)
            .ToListAsync(cancelacion);
        return filas.Select(Reconstruir).ToList();
    }

    private static DescarteEmision Reconstruir(DescarteEmisionFila fila)
    {
        var tipo = TextosFacturacion.TipoDe(fila.Tipo);
        var motivo = TextosFacturacion.MotivoDe(fila.Motivo);
        var entorno = TextosFacturacion.EntornoDe(fila.Entorno);
        if (tipo is not null && motivo is not null && entorno is not null && fila.PuntoVenta is >= 1 and <= 99999)
        {
            try
            {
                return new DescarteEmision(
                    new DateTimeOffset(DateTime.SpecifyKind(fila.Momento, DateTimeKind.Utc)), fila.PresupuestoNumero,
                    tipo.Value, (int)fila.PuntoVenta, fila.Numero, fila.Total, fila.TotalArca, motivo.Value, entorno.Value);
            }
            catch (ArgumentException)
            {
                // Sin la excepción adjunta, como EmisionPendienteRepository.
            }
        }

        throw new InvalidOperationException(MensajeFilaInvalida);
    }
}
