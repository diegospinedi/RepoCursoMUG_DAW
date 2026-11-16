using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Marca;

namespace Optica.Application.Facturacion;

/// <summary>
/// Una línea del comprobante impreso: descripción, cantidad e importe (el Precio Final de la línea) en centavos (FR-09,
/// NFR-02). Clase y no <c>record</c>: el <c>ToString</c> generado mostraría los valores.
/// </summary>
public sealed class LineaDocumentoFactura
{
    /// <exception cref="ArgumentException">
    /// La descripción está vacía, la cantidad es menor que 1 o el importe es negativo. El mensaje nombra el campo y no
    /// incluye el valor recibido.
    /// </exception>
    public LineaDocumentoFactura(string descripcion, int cantidad, long importeCentavos)
    {
        DocumentoFactura.ValidarTexto(descripcion, nameof(descripcion), "La descripción de la línea no es válida.");
        if (cantidad < 1)
        {
            throw new ArgumentException("La cantidad de la línea no es válida.", nameof(cantidad));
        }

        if (importeCentavos < 0)
        {
            throw new ArgumentException("El importe de la línea no es válido.", nameof(importeCentavos));
        }

        Descripcion = descripcion;
        Cantidad = cantidad;
        ImporteCentavos = importeCentavos;
    }

    public string Descripcion { get; }

    public int Cantidad { get; }

    /// <summary>El Precio Final de la línea, en centavos.</summary>
    public long ImporteCentavos { get; }
}

/// <summary>
/// Lo que se dibuja en el PDF de una factura autorizada (FR-06 a FR-15 de FEAT-009): los datos del emisor con su
/// condición frente al IVA (de <see cref="ReglasComprobanteImpreso"/>), el comprobante, la fecha, el receptor, las
/// líneas, el total, el IVA contenido (solo en la B), el CAE con su vencimiento, la URL del QR de ARCA, si lleva la
/// leyenda de comprobante simulado y la identidad visual. Los importes van en centavos: el generador los formatea con 2
/// decimales sin redondear nada (NFR-02).
/// </summary>
/// <remarks>
/// Lleva datos personales (DNI, Apellido y Nombre del receptor, y la URL del QR, que puede llevar el DNI): es una clase
/// sin <c>ToString</c> con datos, y sus mensajes de error nombran el campo sin el valor (Ley 25.326).
/// </remarks>
public sealed class DocumentoFactura
{
    /// <summary>
    /// Arma el documento y valida lo que el PDF necesita. Es <c>internal</c> a propósito (D-23, R-11): la URL del QR no
    /// puede venir de afuera de Application, así ningún código arma un PDF de Simulado u Homologacion con un QR que lleve
    /// el DNI. La única entrada pública es <see cref="Desde"/>, que la arma con <see cref="QrArca.Url"/> desde la
    /// factura. Las pruebas de Infrastructure lo usan por <c>InternalsVisibleTo</c>.
    /// </summary>
    /// <param name="cuitEmisor">El CUIT del emisor en forma canónica: 11 dígitos sin guiones.</param>
    /// <param name="dniReceptor">
    /// El DNI de <see cref="Factura.Receptor"/>; null si la factura se emitió sin identificar al receptor. En ese caso
    /// <paramref name="apellidoReceptor"/> y <paramref name="nombreReceptor"/> no se guardan: el PDF dice "Consumidor
    /// Final" (FR-08).
    /// </param>
    /// <param name="ivaContenidoCentavos">El IVA contenido de la Factura B; null en la C (FR-11, FR-12).</param>
    /// <param name="entorno">El entorno de ARCA guardado en la factura: decide la leyenda (FR-14, FR-15).</param>
    /// <exception cref="ArgumentException">
    /// Un texto del emisor está vacío, el CUIT no es válido, el tipo no es B ni C, el punto de venta o el número no son
    /// válidos, una fecha es la por defecto, el DNI no son 7 u 8 dígitos o falta el Apellido o el Nombre del receptor
    /// identificado, no hay líneas, el total es negativo, el IVA contenido no corresponde al tipo, el CAE no son 14
    /// dígitos, la URL del QR no es la de ARCA, o el entorno no está definido. El mensaje nombra el campo y no incluye el
    /// valor recibido. Un argumento null obligatorio lanza <see cref="ArgumentNullException"/>.
    /// </exception>
    internal DocumentoFactura(
        string razonSocial, string domicilioComercial, string cuitEmisor, string ingresosBrutos,
        DateOnly inicioActividades, TipoComprobante tipo, int puntoVenta, long numero, DateOnly fecha,
        string? dniReceptor, string? apellidoReceptor, string? nombreReceptor,
        IReadOnlyList<LineaDocumentoFactura> lineas, long totalCentavos, long? ivaContenidoCentavos, string cae,
        DateOnly vencimientoCae, string urlQr, EntornoArca entorno, IdentidadVisual marca)
    {
        ValidarTexto(razonSocial, nameof(razonSocial), "La razón social del emisor no es válida.");
        ValidarTexto(domicilioComercial, nameof(domicilioComercial), "El domicilio comercial del emisor no es válido.");
        if (!Cuit.EsValidoCanonico(cuitEmisor))
        {
            throw new ArgumentException("El CUIT del emisor no es válido.", nameof(cuitEmisor));
        }

        ValidarTexto(ingresosBrutos, nameof(ingresosBrutos), "El número de Ingresos Brutos del emisor no es válido.");
        ValidarFecha(inicioActividades, nameof(inicioActividades), "La fecha de inicio de actividades no es válida.");
        if (tipo is not (TipoComprobante.FacturaB or TipoComprobante.FacturaC))
        {
            throw new ArgumentException("El tipo de comprobante no es válido.", nameof(tipo));
        }

        var comprobante = new NumeroComprobante(tipo, puntoVenta, numero);
        ValidarFecha(fecha, nameof(fecha), "La fecha del comprobante no es válida.");
        if (dniReceptor is not null)
        {
            if (dniReceptor.Length is < 7 or > 8 || !dniReceptor.All(char.IsAsciiDigit))
            {
                throw new ArgumentException("El DNI del receptor no es válido.", nameof(dniReceptor));
            }

            ValidarTexto(apellidoReceptor, nameof(apellidoReceptor), "El apellido del receptor no es válido.");
            ValidarTexto(nombreReceptor, nameof(nombreReceptor), "El nombre del receptor no es válido.");
        }

        ArgumentNullException.ThrowIfNull(lineas);
        if (lineas.Count == 0 || lineas.Any(linea => linea is null))
        {
            throw new ArgumentException("Las líneas del comprobante no son válidas.", nameof(lineas));
        }

        if (totalCentavos < 0)
        {
            throw new ArgumentException("El total del comprobante no es válido.", nameof(totalCentavos));
        }

        // La B lleva el IVA contenido (FR-11) y la C no (FR-12).
        if ((tipo == TipoComprobante.FacturaB) != ivaContenidoCentavos.HasValue
            || ivaContenidoCentavos is < 0
            || ivaContenidoCentavos > totalCentavos)
        {
            throw new ArgumentException("El IVA contenido no corresponde al comprobante.", nameof(ivaContenidoCentavos));
        }

        ArgumentNullException.ThrowIfNull(cae);
        if (cae.Length != Factura.LargoCae || !cae.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("El CAE no es válido.", nameof(cae));
        }

        ValidarFecha(vencimientoCae, nameof(vencimientoCae), "El vencimiento del CAE no es válido.");

        // Solo la URL de verificación de ARCA (R-04): el QR no puede llevar a otro sitio.
        ArgumentNullException.ThrowIfNull(urlQr);
        if (urlQr.Length <= QrArca.UrlBase.Length || !urlQr.StartsWith(QrArca.UrlBase, StringComparison.Ordinal))
        {
            throw new ArgumentException("La URL del QR no es la de ARCA.", nameof(urlQr));
        }

        if (!Enum.IsDefined(entorno))
        {
            throw new ArgumentException("El entorno de ARCA no es válido.", nameof(entorno));
        }

        ArgumentNullException.ThrowIfNull(marca);

        RazonSocial = razonSocial;
        DomicilioComercial = domicilioComercial;
        CuitEmisor = cuitEmisor;
        IngresosBrutos = ingresosBrutos;
        InicioActividades = inicioActividades;
        CondicionIva = ReglasComprobanteImpreso.CondicionIvaEmisor(tipo);
        Comprobante = comprobante;
        Fecha = fecha;
        DniReceptor = dniReceptor;
        ApellidoReceptor = dniReceptor is null ? null : apellidoReceptor;
        NombreReceptor = dniReceptor is null ? null : nombreReceptor;
        Lineas = Array.AsReadOnly([.. lineas]);
        TotalCentavos = totalCentavos;
        IvaContenidoCentavos = ivaContenidoCentavos;
        Cae = cae;
        VencimientoCae = vencimientoCae;
        UrlQr = urlQr;
        LlevaLeyendaSimulado = !ReglasComprobanteImpreso.TieneValidezFiscal(entorno);
        Marca = marca;
    }

    public string RazonSocial { get; }

    public string DomicilioComercial { get; }

    /// <summary>11 dígitos sin guiones.</summary>
    public string CuitEmisor { get; }

    public string IngresosBrutos { get; }

    public DateOnly InicioActividades { get; }

    /// <summary>"Responsable Inscripto" en la B, "Responsable Monotributo" en la C (D-07).</summary>
    public string CondicionIva { get; }

    /// <summary>Tipo, punto de venta y número: "Factura B 0003-00000125".</summary>
    public NumeroComprobante Comprobante { get; }

    public TipoComprobante Tipo => Comprobante.Tipo;

    public DateOnly Fecha { get; }

    /// <summary>El DNI de <see cref="Factura.Receptor"/>; null si se emitió a Consumidor Final sin identificar.</summary>
    public string? DniReceptor { get; }

    /// <summary>El Apellido del cliente del presupuesto, solo con receptor identificado.</summary>
    public string? ApellidoReceptor { get; }

    /// <summary>El Nombre del cliente del presupuesto, solo con receptor identificado.</summary>
    public string? NombreReceptor { get; }

    /// <summary>Verdadero si el PDF identifica al receptor como "Consumidor Final", sin DNI (FR-08).</summary>
    public bool EsConsumidorFinalSinIdentificar => DniReceptor is null;

    public IReadOnlyList<LineaDocumentoFactura> Lineas { get; }

    public long TotalCentavos { get; }

    /// <summary>El IVA contenido calculado al emitir la Factura B; null en la C.</summary>
    public long? IvaContenidoCentavos { get; }

    /// <summary>14 dígitos.</summary>
    public string Cae { get; }

    public DateOnly VencimientoCae { get; }

    /// <summary>La URL de <see cref="QrArca"/>: empieza siempre con <see cref="QrArca.UrlBase"/>.</summary>
    public string UrlQr { get; }

    /// <summary>
    /// Verdadero si el PDF lleva "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL" en cada página: la factura no se emitió en
    /// Produccion (FR-14, FR-15).
    /// </summary>
    public bool LlevaLeyendaSimulado { get; }

    public IdentidadVisual Marca { get; }

    /// <summary>
    /// El documento de una factura guardada: los datos del emisor vigentes, el Apellido y el Nombre del cliente del
    /// presupuesto (solo se usan si la factura se emitió con DNI), los importes en centavos, la URL del QR de
    /// <see cref="QrArca"/> y la marca. Es la única forma pública de armar el documento: la URL del QR siempre sale de
    /// la factura y de su entorno, así fuera de Produccion nunca lleva el DNI (D-23).
    /// </summary>
    /// <exception cref="ArgumentNullException">La factura, el emisor o la marca son null.</exception>
    /// <exception cref="ArgumentException">Igual que el constructor.</exception>
    public static DocumentoFactura Desde(
        Factura factura, DatosEmisor emisor, string? apellidoCliente, string? nombreCliente, IdentidadVisual marca)
    {
        ArgumentNullException.ThrowIfNull(factura);
        ArgumentNullException.ThrowIfNull(emisor);

        var lineas = factura.Lineas
            .Select(linea => new LineaDocumentoFactura(linea.Descripcion, linea.Cantidad, Centavos(linea.PrecioFinal)))
            .ToList();
        long? ivaContenido = factura.Tipo == TipoComprobante.FacturaB ? Centavos(factura.Importes.Iva) : null;

        return new DocumentoFactura(
            emisor.RazonSocial, emisor.DomicilioComercial, emisor.Cuit.Valor, emisor.IngresosBrutos,
            emisor.InicioActividades, factura.Tipo, factura.PuntoVenta, factura.Numero, factura.Fecha,
            factura.Receptor.Dni, apellidoCliente, nombreCliente, lineas, Centavos(factura.Importes.Total), ivaContenido,
            factura.Cae, factura.VencimientoCae, QrArca.Url(factura, emisor.Cuit), factura.Entorno, marca);
    }

    /// <summary>Un importe con hasta 2 decimales (lo garantiza la factura) en centavos, sin redondear.</summary>
    private static long Centavos(decimal importe)
    {
        var centavos = importe * 100m;
        if (centavos != decimal.Truncate(centavos))
        {
            throw new ArgumentException("Un importe de la factura tiene más de 2 decimales.", nameof(importe));
        }

        return decimal.ToInt64(centavos);
    }

    internal static void ValidarTexto(string? texto, string campo, string mensaje)
    {
        ArgumentNullException.ThrowIfNull(texto, campo);
        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new ArgumentException(mensaje, campo);
        }
    }

    private static void ValidarFecha(DateOnly fecha, string campo, string mensaje)
    {
        if (fecha == default)
        {
            throw new ArgumentException(mensaje, campo);
        }
    }
}
