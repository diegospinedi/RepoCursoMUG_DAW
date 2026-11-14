using Optica.Domain.Comun;

namespace Optica.Domain.Facturacion;

/// <summary>
/// Un intento de emisión registrado antes de llamar a ARCA (D-09, ADR-021): el presupuesto, el momento en UTC, el tipo,
/// el punto de venta, la fecha, el total y el entorno. Desde FEAT-008 (FR-01, ADR-025) también el número de comprobante
/// pedido, el concepto, los importes y si el receptor se identificó: lo que hace falta para consultarla y recuperarla.
/// Mientras exista no se envía otra solicitud para ese presupuesto (FR-24); se agrega y se quita, no se modifica.
/// </summary>
/// <remarks>
/// La pendiente nueva siempre tiene número, concepto, importes y receptor identificado (todo o nada). Una grabada por
/// FEAT-007 no tiene ninguno de los cuatro: se reconstruye con <see cref="SinNumero"/> (FR-14). No guarda el DNI del
/// receptor, solo si se identificó (Ley 25.326).
/// </remarks>
public sealed class EmisionPendiente
{
    /// <summary>Registra la pendiente de una solicitud con el número de comprobante que se va a pedir (FR-01).</summary>
    /// <param name="momento">Cuándo se registró; se guarda en UTC.</param>
    /// <param name="numero">El último autorizado del punto de venta para el tipo + 1; de 1 a 99.999.999.</param>
    /// <exception cref="ArgumentNullException">La solicitud es null.</exception>
    /// <exception cref="ArgumentException">
    /// El número de presupuesto es menor que 1, el momento es el por defecto, el número de comprobante no está entre 1 y
    /// <see cref="NumeroComprobante.NumeroMaximo"/> o el entorno no está definido. El mensaje nombra el campo y no incluye
    /// el valor recibido.
    /// </exception>
    public EmisionPendiente(
        long presupuestoNumero, DateTimeOffset momento, SolicitudComprobante solicitud, long numero, EntornoArca entorno)
        : this(
            presupuestoNumero, momento, (solicitud ?? throw new ArgumentNullException(nameof(solicitud))).Tipo,
            solicitud.PuntoVenta, solicitud.Fecha, solicitud.Importes.Total, entorno)
    {
        ValidarNumero(numero);

        Numero = numero;
        Concepto = solicitud.Concepto;
        Importes = solicitud.Importes;
        ReceptorIdentificado = solicitud.Receptor.EstaIdentificado;
    }

    private EmisionPendiente(
        long presupuestoNumero, DateTimeOffset momento, TipoComprobante tipo, int puntoVenta, DateOnly fecha,
        decimal total, EntornoArca entorno)
    {
        if (presupuestoNumero < 1)
        {
            throw new ArgumentException("El número de presupuesto no es válido.", nameof(presupuestoNumero));
        }

        if (momento == default)
        {
            throw new ArgumentException("El momento de la emisión no es válido.", nameof(momento));
        }

        ReglasFactura.ValidarTipo(tipo);
        ReglasFactura.ValidarPuntoVenta(puntoVenta);
        ReglasFactura.ValidarFecha(fecha, nameof(fecha), "La fecha del comprobante no es válida.");
        if (total < 0m || total > ReglasImporte.ImporteMaximo || ReglasImporte.TieneMasDeDosDecimales(total))
        {
            throw new ArgumentException("El total de la emisión no es válido.", nameof(total));
        }

        ReglasFactura.ValidarEntorno(entorno);

        PresupuestoNumero = presupuestoNumero;
        Momento = momento.ToUniversalTime();
        Tipo = tipo;
        PuntoVenta = puntoVenta;
        Fecha = fecha;
        Total = total;
        Entorno = entorno;
    }

    public long PresupuestoNumero { get; }

    /// <summary>En UTC (desplazamiento 0).</summary>
    public DateTimeOffset Momento { get; }

    public TipoComprobante Tipo { get; }

    /// <summary>De 1 a 99999.</summary>
    public int PuntoVenta { get; }

    /// <summary>La fecha del comprobante pedida.</summary>
    public DateOnly Fecha { get; }

    /// <summary>El total de la solicitud: el que se compara con el de ARCA (FR-05, FR-07).</summary>
    public decimal Total { get; }

    /// <summary>El entorno de ARCA al que se envió (o se va a enviar) la solicitud.</summary>
    public EntornoArca Entorno { get; }

    /// <summary>El número de comprobante pedido (FR-01); null en una pendiente de FEAT-007 (FR-14).</summary>
    public long? Numero { get; private init; }

    /// <summary>Null en una pendiente de FEAT-007.</summary>
    public ConceptoFactura? Concepto { get; private init; }

    /// <summary>Total, neto, IVA y alícuota de la solicitud; null en una pendiente de FEAT-007.</summary>
    public ImportesFactura? Importes { get; private init; }

    /// <summary>Si la solicitud identificó al receptor con su DNI (FR-11 de FEAT-007); null en una de FEAT-007.</summary>
    public bool? ReceptorIdentificado { get; private init; }

    /// <summary>Verdadero si tiene número registrado (y, con él, concepto, importes y receptor).</summary>
    public bool TieneNumero => Numero is not null;

    /// <summary>
    /// Reconstruye una pendiente grabada por FEAT-007: sin número, concepto, importes ni receptor (FR-14, AC-15).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Algún dato no es válido (los mismos controles que el constructor; el total, entre 0 y
    /// <see cref="ReglasImporte.ImporteMaximo"/> con 2 decimales). El mensaje nombra el campo y no incluye el valor.
    /// </exception>
    public static EmisionPendiente SinNumero(
        long presupuestoNumero, DateTimeOffset momento, TipoComprobante tipo, int puntoVenta, DateOnly fecha,
        decimal total, EntornoArca entorno) =>
        new(presupuestoNumero, momento, tipo, puntoVenta, fecha, total, entorno);

    /// <summary>
    /// Reconstruye una pendiente guardada con número, sin la solicitud original (que llevaba el DNI): el número, el
    /// concepto, los importes y si el receptor se identificó.
    /// </summary>
    /// <exception cref="ArgumentNullException">Los importes son null.</exception>
    /// <exception cref="ArgumentException">
    /// Algún dato no es válido, o los importes no corresponden al tipo (la B discrimina IVA y la C no). El mensaje nombra
    /// el campo y no incluye el valor recibido.
    /// </exception>
    public static EmisionPendiente Registrada(
        long presupuestoNumero, DateTimeOffset momento, TipoComprobante tipo, int puntoVenta, DateOnly fecha, long numero,
        ConceptoFactura concepto, ImportesFactura importes, bool receptorIdentificado, EntornoArca entorno)
    {
        ArgumentNullException.ThrowIfNull(importes);
        ValidarNumero(numero);
        ReglasFactura.ValidarConcepto(concepto);
        ReglasFactura.ValidarImportesDelTipo(tipo, importes);

        return new EmisionPendiente(presupuestoNumero, momento, tipo, puntoVenta, fecha, importes.Total, entorno)
        {
            Numero = numero,
            Concepto = concepto,
            Importes = importes,
            ReceptorIdentificado = receptorIdentificado,
        };
    }

    private static void ValidarNumero(long numero)
    {
        if (numero is < NumeroComprobante.NumeroMinimo or > NumeroComprobante.NumeroMaximo)
        {
            throw new ArgumentException("El número de comprobante no es válido.", nameof(numero));
        }
    }
}
