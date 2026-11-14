using Optica.Domain.Configuracion;
using Optica.Domain.Presupuestos;

namespace Optica.Domain.Facturacion;

/// <summary>
/// Una factura autorizada por ARCA, vinculada al presupuesto que le dio origen (FR-18): tipo, punto de venta, número,
/// fecha, concepto, receptor, importes, CAE, vencimiento del CAE, entorno y al menos una línea. No expone nada que la
/// cambie (FR-25); la base lo refuerza con triggers. Lleva el DNI del receptor: no sobrescribe
/// <see cref="object.ToString"/> (Ley 25.326).
/// </summary>
public sealed class Factura
{
    /// <summary>Largo del CAE: 14 dígitos.</summary>
    public const int LargoCae = 14;

    /// <summary>Reconstruye una factura guardada; la recién autorizada se arma con <see cref="Emitida"/>.</summary>
    /// <remarks>
    /// Al leer una factura guardada no se controla que el vencimiento del CAE sea igual o posterior a la fecha ni que el
    /// DNI del receptor sea el del cliente del presupuesto: esos controles se hacen al emitir, en <see cref="Emitida"/>.
    /// Repetirlos acá podría dejar ilegible un registro que es inmutable (FR-25).
    /// </remarks>
    /// <exception cref="ArgumentNullException">El receptor, los importes, el CAE o la lista de líneas son null.</exception>
    /// <exception cref="ArgumentException">
    /// El número de presupuesto es menor que 1; el de comprobante no está entre 1 y
    /// <see cref="NumeroComprobante.NumeroMaximo"/>; el punto de venta no está entre 1 y 99999; el
    /// tipo, el concepto o el entorno no están definidos; alguna fecha es la por defecto; el CAE no son 14 dígitos; los
    /// importes no corresponden al tipo; no hay líneas, alguna es null, su orden no es 1, 2, 3… o no suman el total. El
    /// mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public Factura(
        long presupuestoNumero, TipoComprobante tipo, int puntoVenta, long numero, DateOnly fecha,
        ConceptoFactura concepto, ReceptorFactura receptor, ImportesFactura importes, string cae,
        DateOnly vencimientoCae, EntornoArca entorno, IReadOnlyList<LineaFactura> lineas)
    {
        if (presupuestoNumero < 1)
        {
            throw new ArgumentException("El número de presupuesto no es válido.", nameof(presupuestoNumero));
        }

        var comprobante = new NumeroComprobante(tipo, puntoVenta, numero);
        ReglasFactura.ValidarFecha(fecha, nameof(fecha), "La fecha del comprobante no es válida.");
        ReglasFactura.ValidarConcepto(concepto);
        ArgumentNullException.ThrowIfNull(receptor);
        ArgumentNullException.ThrowIfNull(importes);
        ReglasFactura.ValidarImportesDelTipo(tipo, importes);
        ArgumentNullException.ThrowIfNull(cae);
        if (cae.Length != LargoCae || !cae.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("El CAE no es válido.", nameof(cae));
        }

        ReglasFactura.ValidarFecha(vencimientoCae, nameof(vencimientoCae), "El vencimiento del CAE no es válido.");
        ReglasFactura.ValidarEntorno(entorno);
        ValidarLineas(lineas, importes.Total);

        PresupuestoNumero = presupuestoNumero;
        Comprobante = comprobante;
        Fecha = fecha;
        Concepto = concepto;
        Receptor = receptor;
        Importes = importes;
        Cae = cae;
        VencimientoCae = vencimientoCae;
        Entorno = entorno;
        Lineas = Array.AsReadOnly([.. lineas]);
    }

    /// <summary>El número del presupuesto que le dio origen; una sola factura por presupuesto (FR-03).</summary>
    public long PresupuestoNumero { get; }

    /// <summary>Tipo, punto de venta y número: "Factura B 0003-00000125".</summary>
    public NumeroComprobante Comprobante { get; }

    public TipoComprobante Tipo => Comprobante.Tipo;

    public int PuntoVenta => Comprobante.PuntoVenta;

    public long Numero => Comprobante.Numero;

    /// <summary>La fecha local del día de emisión (FR-07).</summary>
    public DateOnly Fecha { get; }

    public ConceptoFactura Concepto { get; }

    public ReceptorFactura Receptor { get; }

    public ImportesFactura Importes { get; }

    /// <summary>14 dígitos.</summary>
    public string Cae { get; }

    public DateOnly VencimientoCae { get; }

    /// <summary>El entorno de ARCA en que se emitió (FR-18).</summary>
    public EntornoArca Entorno { get; }

    /// <summary>Al menos una línea, con orden 1, 2, 3…, de solo lectura.</summary>
    public IReadOnlyList<LineaFactura> Lineas { get; }

    /// <summary>
    /// La factura que ARCA autorizó para <paramref name="solicitud"/>: copia las líneas de <paramref name="presupuesto"/>
    /// en su orden (FR-01) y toma de la solicitud tipo, punto de venta, fecha, concepto, receptor e importes, con el
    /// número, el CAE y el entorno de la respuesta (FR-18).
    /// </summary>
    /// <exception cref="ArgumentNullException">El presupuesto o la solicitud son null.</exception>
    /// <exception cref="ArgumentException">
    /// El presupuesto no está en Final; el total de la solicitud no es el del presupuesto; el receptor está identificado
    /// con un DNI que no es el del cliente del presupuesto; el vencimiento del CAE es anterior a la fecha del
    /// comprobante; o algún dato de la respuesta es inválido (igual que el constructor). El mensaje no incluye valores.
    /// </exception>
    public static Factura Emitida(
        Presupuesto presupuesto, SolicitudComprobante solicitud, long numero, string cae, DateOnly vencimientoCae,
        EntornoArca entorno)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        ArgumentNullException.ThrowIfNull(solicitud);
        if (presupuesto.Estado != EstadoPresupuesto.Final)
        {
            throw new ArgumentException("Solo se factura un presupuesto en Final.", nameof(presupuesto));
        }

        if (solicitud.Importes.Total != presupuesto.Total)
        {
            throw new ArgumentException("La solicitud no es del total del presupuesto.", nameof(solicitud));
        }

        // Un receptor identificado es el cliente del presupuesto (FR-11); los mensajes no llevan DNI (Ley 25.326).
        if (solicitud.Receptor.Dni is { } dni && !string.Equals(dni, presupuesto.Cliente.Dni, StringComparison.Ordinal))
        {
            throw new ArgumentException("El receptor de la solicitud no es el cliente del presupuesto.", nameof(solicitud));
        }

        if (vencimientoCae < solicitud.Fecha)
        {
            throw new ArgumentException(
                "El vencimiento del CAE es anterior a la fecha del comprobante.", nameof(vencimientoCae));
        }

        var lineas = presupuesto.Lineas.Select((linea, indice) => LineaFactura.DesdePresupuesto(indice + 1, linea)).ToList();
        return new Factura(
            presupuesto.Numero, solicitud.Tipo, solicitud.PuntoVenta, numero, solicitud.Fecha, solicitud.Concepto,
            solicitud.Receptor, solicitud.Importes, cae, vencimientoCae, entorno, lineas);
    }

    private static void ValidarLineas(IReadOnlyList<LineaFactura> lineas, decimal total)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        if (lineas.Count == 0)
        {
            throw new ArgumentException("La factura necesita al menos una línea.", nameof(lineas));
        }

        var suma = 0m;
        for (var indice = 0; indice < lineas.Count; indice++)
        {
            if (lineas[indice] is not { } linea || linea.Orden != indice + 1)
            {
                throw new ArgumentException("Las líneas de la factura no son válidas.", nameof(lineas));
            }

            // Cada Precio Final es como máximo ImporteMaximo y se corta al pasar el total: la suma no desborda.
            suma += linea.PrecioFinal;
            if (suma > total)
            {
                break;
            }
        }

        if (suma != total)
        {
            throw new ArgumentException("Las líneas de la factura no suman el total.", nameof(lineas));
        }
    }
}

/// <summary>Validaciones que comparten las entidades de facturación; los mensajes no incluyen el valor recibido.</summary>
internal static class ReglasFactura
{
    internal static void ValidarTipo(TipoComprobante tipo)
    {
        if (!Enum.IsDefined(tipo))
        {
            throw new ArgumentException("El tipo de comprobante no es válido.", nameof(tipo));
        }
    }

    internal static void ValidarPuntoVenta(int puntoVenta)
    {
        if (puntoVenta is < PoliticaConfiguracionNegocio.PuntoVentaMinimo or > PoliticaConfiguracionNegocio.PuntoVentaMaximo)
        {
            throw new ArgumentException("El punto de venta no es válido.", nameof(puntoVenta));
        }
    }

    internal static void ValidarConcepto(ConceptoFactura concepto)
    {
        if (!Enum.IsDefined(concepto))
        {
            throw new ArgumentException("El concepto de la factura no es válido.", nameof(concepto));
        }
    }

    internal static void ValidarEntorno(EntornoArca entorno)
    {
        if (!Enum.IsDefined(entorno))
        {
            throw new ArgumentException("El entorno de ARCA no es válido.", nameof(entorno));
        }
    }

    internal static void ValidarFecha(DateOnly fecha, string campo, string mensaje)
    {
        if (fecha == default)
        {
            throw new ArgumentException(mensaje, campo);
        }
    }

    /// <summary>La Factura B discrimina IVA y la C no (FR-14, FR-16).</summary>
    internal static void ValidarImportesDelTipo(TipoComprobante tipo, ImportesFactura importes)
    {
        if (importes.DiscriminaIva != (tipo == TipoComprobante.FacturaB))
        {
            throw new ArgumentException("Los importes no corresponden al tipo de comprobante.", nameof(importes));
        }
    }
}
