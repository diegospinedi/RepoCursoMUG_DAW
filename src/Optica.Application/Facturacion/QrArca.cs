using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>
/// El contenido del código QR de ARCA del comprobante impreso (FR-10 de FEAT-009, ADR-027): la URL de verificación
/// constante <see cref="UrlBase"/> seguida del JSON del comprobante en base64, armado con <c>System.Text.Json</c> y
/// nunca concatenando texto (threat model, Tampering). Solo el valor del parámetro <c>p</c> sale de los datos (R-04).
/// </summary>
/// <remarks>
/// <para>
/// Los códigos de ARCA (tipo de comprobante, tipo de documento, moneda y cotización) repiten los de
/// <c>Infrastructure/Arca/CodigosWsfe.cs</c>: el QR es un dato del comprobante impreso y no una llamada a ARCA. Una
/// prueba de Infrastructure compara ambas fuentes (D-24).
/// </para>
/// <para>
/// El JSON lleva el DNI del receptor cuando la factura se emitió con DNI en Produccion, como exige la especificación de
/// ARCA. Fuera de Produccion lleva <see cref="DocTipoSinIdentificar"/> y <see cref="DocNroSinIdentificar"/> aunque la
/// factura tenga DNI: el comprobante no existe en ARCA y escanearlo no debe mandar el DNI a www.afip.gob.ar (D-23,
/// R-11). La URL no se registra en el log.
/// </para>
/// </remarks>
public static class QrArca
{
    /// <summary>La URL de verificación de comprobantes de ARCA, con el parámetro <c>p</c> abierto. Constante (R-04).</summary>
    public const string UrlBase = "https://www.afip.gob.ar/fe/qr/?p=";

    /// <summary>Versión del formato del JSON del QR.</summary>
    public const int Version = 1;

    /// <summary>Tipo de documento del receptor identificado con DNI (el <c>DocTipo</c> 96 de WSFEv1).</summary>
    public const int DocTipoDni = 96;

    /// <summary>Tipo de documento del receptor sin identificar (el <c>DocTipo</c> 99 de WSFEv1).</summary>
    public const int DocTipoSinIdentificar = 99;

    /// <summary>Número de documento del receptor sin identificar.</summary>
    public const long DocNroSinIdentificar = 0;

    /// <summary>Moneda: pesos argentinos (el <c>MonId</c> de WSFEv1).</summary>
    public const string MonedaPesos = "PES";

    /// <summary>Cotización de los pesos argentinos (el <c>MonCotiz</c> de WSFEv1).</summary>
    public const int CotizacionPesos = 1;

    /// <summary>Tipo de código de autorización: "E" de CAE.</summary>
    public const string TipoCodigoAutorizacion = "E";

    /// <summary>El código de ARCA del tipo de comprobante: Factura B 6, Factura C 11.</summary>
    /// <exception cref="ArgumentException">El tipo no está definido. El mensaje no incluye el valor recibido.</exception>
    public static int CodigoTipo(TipoComprobante tipo) => tipo switch
    {
        TipoComprobante.FacturaB => 6,
        TipoComprobante.FacturaC => 11,
        _ => throw new ArgumentException("El tipo de comprobante no es válido.", nameof(tipo)),
    };

    /// <summary>
    /// La URL del QR de <paramref name="factura"/>: <see cref="UrlBase"/> seguida del base64 del JSON con los 13 campos
    /// de la especificación de ARCA, en su orden (<c>ver</c>, <c>fecha</c>, <c>cuit</c>, <c>ptoVta</c>, <c>tipoCmp</c>,
    /// <c>nroCmp</c>, <c>importe</c>, <c>moneda</c>, <c>ctz</c>, <c>tipoDocRec</c>, <c>nroDocRec</c>, <c>tipoCodAut</c>,
    /// <c>codAut</c>). El DNI sale de <see cref="Factura.Receptor"/>, que es lo que autorizó ARCA, y solo en Produccion.
    /// </summary>
    /// <exception cref="ArgumentNullException">La factura o el CUIT son null.</exception>
    public static string Url(Factura factura, Cuit cuitEmisor)
    {
        ArgumentNullException.ThrowIfNull(factura);
        ArgumentNullException.ThrowIfNull(cuitEmisor);

        var conDni = factura.Receptor.Dni is { } dni && ReglasComprobanteImpreso.TieneValidezFiscal(factura.Entorno)
            ? Numero(dni)
            : (long?)null;

        var buffer = new ArrayBufferWriter<byte>(512);
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("ver", Version);
            json.WriteString("fecha", factura.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            json.WriteNumber("cuit", Numero(cuitEmisor.Valor));
            json.WriteNumber("ptoVta", factura.PuntoVenta);
            json.WriteNumber("tipoCmp", CodigoTipo(factura.Tipo));
            json.WriteNumber("nroCmp", factura.Numero);
            json.WriteNumber("importe", factura.Importes.Total);
            json.WriteString("moneda", MonedaPesos);
            json.WriteNumber("ctz", CotizacionPesos);
            json.WriteNumber("tipoDocRec", conDni is null ? DocTipoSinIdentificar : DocTipoDni);
            json.WriteNumber("nroDocRec", conDni ?? DocNroSinIdentificar);
            json.WriteString("tipoCodAut", TipoCodigoAutorizacion);
            json.WriteNumber("codAut", Numero(factura.Cae));
            json.WriteEndObject();
        }

        return UrlBase + Convert.ToBase64String(buffer.WrittenSpan);
    }

    /// <summary>
    /// Un texto de dígitos ASCII ya validado por su dueño (CUIT de 11, DNI de 7 u 8, CAE de 14) como número: así lo pide
    /// la especificación de ARCA ("00123456" es 123456).
    /// </summary>
    private static long Numero(string digitos) => long.Parse(digitos, NumberStyles.None, CultureInfo.InvariantCulture);
}
