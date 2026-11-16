using Microsoft.EntityFrameworkCore;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Observaciones de verificación sobre la tabla <c>ObservacionEmision</c> (FEAT-008, D-12, ADR-026). Se agregan y se
/// quitan, no se modifican: la base rechaza el UPDATE (<c>TR_ObservacionEmision_Inmutable</c>), así que reemplazar la del
/// mismo presupuesto exige confirmar la baja en el medio, y <see cref="Agregar"/> lo controla. <see cref="Agregar"/> y
/// <see cref="QuitarObservacion"/> solo registran el cambio en el contexto compartido, que confirma
/// <see cref="UnidadDeTrabajoEf"/>. Sin datos del cliente; no registra nada en el log.
/// </summary>
public sealed class ObservacionEmisionRepository(OpticaDbContext contexto) : IObservacionEmisionRepository
{
    private const string MensajeQuitadaSinConfirmar =
        "La observación de este presupuesto se quitó y la baja no se confirmó.";

    private const string MensajeFilaInvalida = "Una observación de emisión guardada no es válida.";

    /// <exception cref="ArgumentNullException"><paramref name="observacion"/> es null.</exception>
    /// <exception cref="InvalidOperationException">
    /// La observación del mismo presupuesto está quitada y sin confirmar: EF Core grabaría la baja y el alta como un
    /// UPDATE, que el trigger rechaza. No registra nada. El mensaje es fijo.
    /// </exception>
    public void Agregar(ObservacionEmision observacion)
    {
        ArgumentNullException.ThrowIfNull(observacion);
        if (contexto.ObservacionesEmision.Local.FindEntry(observacion.PresupuestoNumero) is { State: EntityState.Deleted })
        {
            throw new InvalidOperationException(MensajeQuitadaSinConfirmar);
        }

        contexto.ObservacionesEmision.Add(new ObservacionEmisionFila
        {
            PresupuestoNumero = observacion.PresupuestoNumero,
            Motivo = TextosFacturacion.TextoDe(observacion.Motivo),
            Numero = observacion.Numero,
            Total = observacion.TotalRegistrado,
            TotalArca = observacion.TotalArca,
            Entorno = TextosFacturacion.TextoDe(observacion.Entorno),
            Momento = observacion.Momento.UtcDateTime,
        });
    }

    /// <summary>
    /// Registra la baja de la observación del presupuesto, si existe; si no, no registra nada. La busca con <c>Find</c>,
    /// como <see cref="EmisionPendienteRepository.Quitar"/>.
    /// </summary>
    public void QuitarObservacion(long presupuestoNumero)
    {
        var fila = contexto.ObservacionesEmision.Find(presupuestoNumero);
        if (fila is not null)
        {
            contexto.ObservacionesEmision.Remove(fila);
        }
    }

    /// <exception cref="InvalidOperationException">La fila no es válida. El mensaje es fijo y no lleva datos.</exception>
    public async Task<ObservacionEmision?> ObtenerObservacionAsync(
        long presupuestoNumero, CancellationToken cancelacion = default)
    {
        var fila = await contexto.ObservacionesEmision.AsNoTracking()
            .SingleOrDefaultAsync(o => o.PresupuestoNumero == presupuestoNumero, cancelacion);
        if (fila is null)
        {
            return null;
        }

        var motivo = TextosFacturacion.MotivoDe(fila.Motivo);
        var entorno = TextosFacturacion.EntornoDe(fila.Entorno);
        if (motivo is not null && entorno is not null)
        {
            try
            {
                return new ObservacionEmision(
                    fila.PresupuestoNumero, motivo.Value, fila.Numero, fila.Total, fila.TotalArca, entorno.Value,
                    new DateTimeOffset(DateTime.SpecifyKind(fila.Momento, DateTimeKind.Utc)));
            }
            catch (ArgumentException)
            {
                // Sin la excepción adjunta, como EmisionPendienteRepository.
            }
        }

        throw new InvalidOperationException(MensajeFilaInvalida);
    }
}
