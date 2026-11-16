namespace Optica.Domain.Facturacion;

/// <summary>
/// Un descarte confirmado de una emisión pendiente (FR-09, FR-10, ADR-026): el momento en UTC, el presupuesto, el tipo,
/// el punto de venta, el número registrado (si había), el total registrado, el de ARCA (si se conoce), el motivo y el
/// entorno. Es el registro con que se concilia con el contador: solo se agrega. No lleva datos del cliente.
/// </summary>
public sealed class DescarteEmision
{
    /// <exception cref="ArgumentException">
    /// El momento es el por defecto; el número de presupuesto es menor que 1; el tipo, el motivo o el entorno no están
    /// definidos; el punto de venta no está entre 1 y 99999; el número no está entre 1 y
    /// <see cref="NumeroComprobante.NumeroMaximo"/>; un total no es un importe válido; o, con
    /// <see cref="MotivoObservacionEmision.ImporteDistinto"/>, falta el número o el total de ARCA. El mensaje nombra el
    /// campo y no incluye el valor recibido.
    /// </exception>
    public DescarteEmision(
        DateTimeOffset momento, long presupuestoNumero, TipoComprobante tipo, int puntoVenta, long? numero,
        decimal totalRegistrado, decimal? totalArca, MotivoObservacionEmision motivo, EntornoArca entorno)
    {
        ReglasObservacion.ValidarMomento(momento);
        ReglasObservacion.ValidarPresupuesto(presupuestoNumero);
        ReglasFactura.ValidarTipo(tipo);
        ReglasFactura.ValidarPuntoVenta(puntoVenta);
        ReglasObservacion.Validar(motivo, numero, totalRegistrado, totalArca);
        ReglasFactura.ValidarEntorno(entorno);

        Momento = momento.ToUniversalTime();
        PresupuestoNumero = presupuestoNumero;
        Tipo = tipo;
        PuntoVenta = puntoVenta;
        Numero = numero;
        TotalRegistrado = totalRegistrado;
        TotalArca = totalArca;
        Motivo = motivo;
        Entorno = entorno;
    }

    /// <summary>Cuándo se descartó, en UTC (FR-10).</summary>
    public DateTimeOffset Momento { get; }

    public long PresupuestoNumero { get; }

    public TipoComprobante Tipo { get; }

    /// <summary>De 1 a 99999.</summary>
    public int PuntoVenta { get; }

    /// <summary>El número registrado en la pendiente; null si no tenía (FR-14).</summary>
    public long? Numero { get; }

    public decimal TotalRegistrado { get; }

    /// <summary>El total de ARCA, cuando se conoce (FR-10).</summary>
    public decimal? TotalArca { get; }

    public MotivoObservacionEmision Motivo { get; }

    /// <summary>El entorno de la pendiente descartada.</summary>
    public EntornoArca Entorno { get; }
}
