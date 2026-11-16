using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Emisiones pendientes sobre la tabla <c>EmisionPendiente</c> (FEAT-007, D-09, ADR-021). Se agregan y se quitan, no se
/// modifican. <see cref="Agregar"/> y <see cref="Quitar"/> solo registran el cambio en el contexto compartido, que
/// confirma <see cref="UnidadDeTrabajoEf"/>; una segunda pendiente para el mismo presupuesto choca con la clave primaria y
/// se informa ahí como conflicto de unicidad (FR-24).
/// </summary>
/// <remarks>
/// Guarda los datos de la solicitud que verifica el PRD 07 (tipo, punto de venta, fecha, total), el momento en UTC y el
/// entorno, y desde FEAT-008 el número pedido, el concepto, el neto, el IVA, la alícuota y si el receptor se identificó
/// (D-15); no guarda el DNI del receptor (Ley 25.326). Una fila de FEAT-007 (las columnas nuevas nulas) se lee como
/// <see cref="EmisionPendiente.SinNumero"/>; una con algunas nulas y otras no es inválida. No registra nada en el log. La base rechaza modificar una
/// pendiente (<c>TR_EmisionPendiente_Inmutable</c>); por eso, quitar y volver a agregar la del mismo presupuesto exige
/// confirmar la baja en el medio, y <see cref="Agregar"/> lo controla.
/// </remarks>
public sealed class EmisionPendienteRepository(OpticaDbContext contexto) : IEmisionPendienteRepository
{
    private const string MensajeQuitadaSinConfirmar =
        "La emisión pendiente de este presupuesto se quitó y la baja no se confirmó.";

    private const string MensajeFilaInvalida = "Una emisión pendiente guardada no es válida.";

    /// <exception cref="ArgumentNullException"><paramref name="pendiente"/> es null.</exception>
    /// <exception cref="InvalidOperationException">
    /// El contexto tiene la pendiente del mismo presupuesto quitada y sin confirmar: EF Core grabaría la baja y el alta
    /// como un UPDATE, que <c>TR_EmisionPendiente_Inmutable</c> rechaza. No registra nada. El mensaje es fijo.
    /// </exception>
    public void Agregar(EmisionPendiente pendiente)
    {
        ArgumentNullException.ThrowIfNull(pendiente);
        if (contexto.EmisionesPendientes.Local.FindEntry(pendiente.PresupuestoNumero) is { State: EntityState.Deleted })
        {
            throw new InvalidOperationException(MensajeQuitadaSinConfirmar);
        }

        contexto.EmisionesPendientes.Add(new EmisionPendienteFila
        {
            PresupuestoNumero = pendiente.PresupuestoNumero,
            Momento = pendiente.Momento.UtcDateTime,
            Tipo = TextosFacturacion.TextoDe(pendiente.Tipo),
            PuntoVenta = pendiente.PuntoVenta,
            Fecha = pendiente.Fecha,
            Total = pendiente.Total,
            Entorno = TextosFacturacion.TextoDe(pendiente.Entorno),
            Numero = pendiente.Numero,
            Concepto = pendiente.Concepto is { } concepto ? TextosFacturacion.TextoDe(concepto) : null,
            Neto = pendiente.Importes?.Neto,
            Iva = pendiente.Importes?.Iva,
            Alicuota = pendiente.Importes?.Alicuota?.ToString(CultureInfo.InvariantCulture),
            ReceptorIdentificado = pendiente.ReceptorIdentificado is { } identificado ? (identificado ? 1L : 0L) : null,
        });
    }

    /// <summary>
    /// Registra la baja de la pendiente del presupuesto, si existe; si no, no registra nada (D-44). La busca con
    /// <c>Find</c>: primero entre las que sigue el contexto (una agregada y sin confirmar se descarta) y si no, en la base.
    /// Marcarla como borrada sin leerla haría fallar la confirmación por concurrencia cuando no hay fila.
    /// </summary>
    public void Quitar(long presupuestoNumero)
    {
        var fila = contexto.EmisionesPendientes.Find(presupuestoNumero);
        if (fila is not null)
        {
            contexto.EmisionesPendientes.Remove(fila);
        }
    }

    public Task<bool> ExisteAsync(long presupuestoNumero, CancellationToken cancelacion = default) =>
        contexto.EmisionesPendientes.AsNoTracking().AnyAsync(p => p.PresupuestoNumero == presupuestoNumero, cancelacion);

    /// <exception cref="InvalidOperationException">La fila no es válida. El mensaje es fijo y no lleva datos.</exception>
    public async Task<EmisionPendiente?> ObtenerPendienteAsync(
        long presupuestoNumero, CancellationToken cancelacion = default)
    {
        var fila = await contexto.EmisionesPendientes.AsNoTracking()
            .SingleOrDefaultAsync(p => p.PresupuestoNumero == presupuestoNumero, cancelacion);
        return fila is null ? null : Reconstruir(fila);
    }

    public async Task<IReadOnlyList<long>> ListarNumerosAsync(CancellationToken cancelacion = default) =>
        await contexto.EmisionesPendientes.AsNoTracking()
            .OrderBy(p => p.PresupuestoNumero)
            .Select(p => p.PresupuestoNumero)
            .ToListAsync(cancelacion);

    /// <summary>
    /// La pendiente de una fila: sin número si las seis columnas de FEAT-008 son nulas (FR-14); con número si número,
    /// concepto, neto, IVA y receptor tienen valor (la alícuota la controlan los importes según el tipo). Cualquier otra
    /// combinación, o un valor que el dominio rechaza, es una fila inválida.
    /// </summary>
    private static EmisionPendiente Reconstruir(EmisionPendienteFila fila)
    {
        var tipo = TextosFacturacion.TipoDe(fila.Tipo);
        var entorno = TextosFacturacion.EntornoDe(fila.Entorno);
        if (tipo is null || entorno is null || fila.PuntoVenta is < 1 or > 99999)
        {
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        var momento = new DateTimeOffset(DateTime.SpecifyKind(fila.Momento, DateTimeKind.Utc));
        var puntoVenta = (int)fila.PuntoVenta;
        try
        {
            if (fila is { Numero: null, Concepto: null, Neto: null, Iva: null, Alicuota: null, ReceptorIdentificado: null })
            {
                return EmisionPendiente.SinNumero(
                    fila.PresupuestoNumero, momento, tipo.Value, puntoVenta, fila.Fecha, fila.Total, entorno.Value);
            }

            if (fila is
                {
                    Numero: { } numero, Concepto: { } textoConcepto, Neto: { } neto, Iva: { } iva,
                    ReceptorIdentificado: 0 or 1
                }
                && TextosFacturacion.ConceptoDe(textoConcepto) is { } concepto
                && LeerAlicuota(fila.Alicuota, out var alicuota))
            {
                return EmisionPendiente.Registrada(
                    fila.PresupuestoNumero, momento, tipo.Value, puntoVenta, fila.Fecha, numero, concepto,
                    new ImportesFactura(fila.Total, neto, iva, alicuota), fila.ReceptorIdentificado == 1, entorno.Value);
            }
        }
        catch (ArgumentException)
        {
            // Sin la excepción adjunta: su mensaje es del dominio y la fila puede venir de cualquier lado.
        }

        throw new InvalidOperationException(MensajeFilaInvalida);
    }

    private static bool LeerAlicuota(string? texto, out decimal? alicuota)
    {
        alicuota = null;
        if (texto is null)
        {
            return true;
        }

        if (decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor))
        {
            alicuota = valor;
            return true;
        }

        return false;
    }
}
