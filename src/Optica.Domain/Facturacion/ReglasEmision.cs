using System.Diagnostics.CodeAnalysis;
using Optica.Domain.Configuracion;
using Optica.Domain.Presupuestos;

namespace Optica.Domain.Facturacion;

/// <summary>Por qué no se arma la solicitud de una factura. No se llama a ARCA.</summary>
public enum MotivoEmisionRechazada
{
    /// <summary>El presupuesto está en Borrador (FR-02).</summary>
    Borrador,

    /// <summary>El tope de identificación no tiene valor (FR-13).</summary>
    SinTope,

    /// <summary>El punto de venta no tiene valor (FR-29).</summary>
    SinPuntoVenta,

    /// <summary>Con Responsable Inscripto, la alícuota guardada no es una de ARCA (FR-27).</summary>
    AlicuotaNoAdmitida,
}

/// <summary>
/// El resultado de <see cref="ReglasEmision.Armar"/>: <see cref="EsArmada"/> con la solicitud, o rechazada con su
/// <see cref="Motivo"/> y sin solicitud.
/// </summary>
public sealed class ResultadoArmadoEmision
{
    private ResultadoArmadoEmision(SolicitudComprobante? solicitud, MotivoEmisionRechazada? motivo)
    {
        Solicitud = solicitud;
        Motivo = motivo;
    }

    /// <summary>La solicitud; null si la emisión se rechazó.</summary>
    public SolicitudComprobante? Solicitud { get; }

    /// <summary>El motivo del rechazo; null si la solicitud se armó.</summary>
    public MotivoEmisionRechazada? Motivo { get; }

    /// <summary>Verdadero si la solicitud se armó: <see cref="Solicitud"/> la tiene.</summary>
    [MemberNotNullWhen(true, nameof(Solicitud))]
    [MemberNotNullWhen(false, nameof(Motivo))]
    public bool EsArmada => Solicitud is not null;

    internal static ResultadoArmadoEmision Armada(SolicitudComprobante solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return new ResultadoArmadoEmision(solicitud, null);
    }

    internal static ResultadoArmadoEmision Rechazada(MotivoEmisionRechazada motivo) => new(null, motivo);
}

/// <summary>
/// Las reglas que arman la solicitud de una factura a consumidor final desde un presupuesto en Final y la configuración
/// vigente (FR-01, FR-04 a FR-16, FR-27, FR-29). Reglas puras: no leen la base ni el reloj.
/// </summary>
public static class ReglasEmision
{
    /// <summary>
    /// Rechaza, en este orden, un presupuesto en Borrador, una configuración sin tope o sin punto de venta, y, con
    /// Responsable Inscripto, una alícuota que no es de ARCA. Si no, arma la solicitud: Factura B con Responsable
    /// Inscripto y C con Monotributo (FR-05, FR-06); el total del presupuesto (FR-04); el receptor sin identificar si el
    /// total no supera el tope de <paramref name="configuracion"/> y con el DNI del cliente si lo supera (FR-10 a
    /// FR-12); el neto y el IVA en la B (FR-14, FR-15) y solo el total en la C (FR-16); la fecha recibida (FR-07); y el
    /// concepto elegido, con las fechas de servicio iguales a la del comprobante en Productos y servicios (FR-08, FR-09).
    /// </summary>
    /// <param name="fecha">La fecha local del día de emisión (D-21); la pone quien llama.</param>
    /// <exception cref="ArgumentNullException">El presupuesto o la configuración son null.</exception>
    /// <exception cref="ArgumentException">
    /// El concepto no está definido o la fecha es la por defecto: la capa de aplicación los valida antes. El mensaje no
    /// incluye el valor recibido.
    /// </exception>
    public static ResultadoArmadoEmision Armar(
        Presupuesto presupuesto, ConfiguracionNegocio configuracion, ConceptoFactura concepto, DateOnly fecha)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        ArgumentNullException.ThrowIfNull(configuracion);
        ReglasFactura.ValidarConcepto(concepto);
        ReglasFactura.ValidarFecha(fecha, nameof(fecha), "La fecha del comprobante no es válida.");

        if (presupuesto.Estado != EstadoPresupuesto.Final)
        {
            return ResultadoArmadoEmision.Rechazada(MotivoEmisionRechazada.Borrador);
        }

        if (configuracion.TopeIdentificacion is not { } tope)
        {
            return ResultadoArmadoEmision.Rechazada(MotivoEmisionRechazada.SinTope);
        }

        if (configuracion.PuntoVenta is not { } puntoVenta)
        {
            return ResultadoArmadoEmision.Rechazada(MotivoEmisionRechazada.SinPuntoVenta);
        }

        var esFacturaB = configuracion.CondicionFiscal == CondicionFiscal.ResponsableInscripto;
        if (esFacturaB && !configuracion.AlicuotaAdmitidaPorArca)
        {
            return ResultadoArmadoEmision.Rechazada(MotivoEmisionRechazada.AlicuotaNoAdmitida);
        }

        var total = presupuesto.Total;
        var receptor = total > tope ? ReceptorFactura.ConDni(presupuesto.Cliente.Dni) : ReceptorFactura.SinIdentificar;
        var solicitud = esFacturaB
            ? new SolicitudComprobante(
                TipoComprobante.FacturaB, puntoVenta, fecha, concepto, receptor,
                ImportesFactura.ParaFacturaB(total, configuracion.AlicuotaIva))
            : new SolicitudComprobante(
                TipoComprobante.FacturaC, puntoVenta, fecha, concepto, receptor, ImportesFactura.ParaFacturaC(total));

        return ResultadoArmadoEmision.Armada(solicitud);
    }
}
