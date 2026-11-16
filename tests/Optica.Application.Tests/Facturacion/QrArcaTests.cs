using System.Text;
using System.Text.Json;
using Optica.Application.Facturacion;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// El QR de ARCA (FR-10, AC-12 de FEAT-009, ADR-027): la URL constante de verificación y el JSON del comprobante en
/// base64. Datos ficticios: CUIT 20-00000000-1 (D-33), DNI que empieza con 00 (D-08) y un CAE inventado.
/// </summary>
public sealed class QrArcaTests
{
    private const string Cae = "71234567890123";
    private const string DniFicticio = "00123456";

    private static readonly Cuit CuitEmisor = Cuit.Interpretar("20-00000000-1")!;

    [Fact]
    public void QrArca_FacturaBConDniEnProduccion_UrlYJsonDeArca()
    {
        var factura = FacturaB(ReceptorFactura.ConDni(DniFicticio), EntornoArca.Produccion);

        var json = Decodificar(QrArca.Url(factura, CuitEmisor));

        // Los 13 campos de la especificación de ARCA, con sus tipos (números y textos), y ninguno más.
        Assert.Equal(13, json.EnumerateObject().Count());
        Assert.Equal(1, json.GetProperty("ver").GetInt32());
        Assert.Equal("2026-10-30", json.GetProperty("fecha").GetString());
        Assert.Equal(20000000001L, json.GetProperty("cuit").GetInt64());
        Assert.Equal(3, json.GetProperty("ptoVta").GetInt32());
        Assert.Equal(6, json.GetProperty("tipoCmp").GetInt32());
        Assert.Equal(125L, json.GetProperty("nroCmp").GetInt64());
        Assert.Equal(2710.02m, json.GetProperty("importe").GetDecimal());
        Assert.Equal("PES", json.GetProperty("moneda").GetString());
        Assert.Equal(1, json.GetProperty("ctz").GetInt32());
        Assert.Equal(96, json.GetProperty("tipoDocRec").GetInt32());
        Assert.Equal(123456L, json.GetProperty("nroDocRec").GetInt64());
        Assert.Equal("E", json.GetProperty("tipoCodAut").GetString());
        Assert.Equal(71234567890123L, json.GetProperty("codAut").GetInt64());

        // El orden de la especificación de ARCA.
        Assert.Equal(
            ["ver", "fecha", "cuit", "ptoVta", "tipoCmp", "nroCmp", "importe", "moneda", "ctz", "tipoDocRec", "nroDocRec",
                "tipoCodAut", "codAut"],
            json.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void QrArca_ConsumidorFinal_TipoDoc99SinDni()
    {
        var factura = FacturaC(ReceptorFactura.SinIdentificar, EntornoArca.Produccion);

        var url = QrArca.Url(factura, CuitEmisor);
        var json = Decodificar(url);

        Assert.Equal(99, json.GetProperty("tipoDocRec").GetInt32());
        Assert.Equal(0L, json.GetProperty("nroDocRec").GetInt64());
        Assert.Equal(11, json.GetProperty("tipoCmp").GetInt32());
        Assert.Equal(2710.02m, json.GetProperty("importe").GetDecimal());
    }

    [Theory]
    [InlineData(EntornoArca.Homologacion)]
    [InlineData(EntornoArca.Simulado)]
    public void QrArca_FueraDeProduccion_SinDni(EntornoArca entorno)
    {
        // D-23 (R-11): el comprobante no existe en ARCA; escanearlo no tiene que mandar el DNI a www.afip.gob.ar.
        var factura = FacturaB(ReceptorFactura.ConDni(DniFicticio), entorno);

        var url = QrArca.Url(factura, CuitEmisor);
        var json = Decodificar(url);

        Assert.Equal(99, json.GetProperty("tipoDocRec").GetInt32());
        Assert.Equal(0L, json.GetProperty("nroDocRec").GetInt64());
        // Ningún campo lleva el DNI (el CAE de prueba contiene los dígitos 123456: se comparan valores enteros).
        Assert.DoesNotContain("123456", json.EnumerateObject().Select(p => p.Value.GetRawText()));

        // El resto del comprobante sí va: la URL sigue siendo la de ese comprobante.
        Assert.Equal(6, json.GetProperty("tipoCmp").GetInt32());
        Assert.Equal(125L, json.GetProperty("nroCmp").GetInt64());
        Assert.Equal(71234567890123L, json.GetProperty("codAut").GetInt64());
    }

    [Fact]
    public void QrArca_HostYRutaConstantes()
    {
        // R-04: el host y la ruta son constantes; solo el valor de p sale de los datos de la factura.
        foreach (var factura in new[]
        {
            FacturaB(ReceptorFactura.ConDni(DniFicticio), EntornoArca.Produccion),
            FacturaB(ReceptorFactura.SinIdentificar, EntornoArca.Homologacion),
            FacturaC(ReceptorFactura.SinIdentificar, EntornoArca.Simulado),
        })
        {
            var texto = QrArca.Url(factura, CuitEmisor);
            Assert.StartsWith("https://www.afip.gob.ar/fe/qr/?p=", texto, StringComparison.Ordinal);

            var url = new Uri(texto);
            Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
            Assert.Equal("www.afip.gob.ar", url.Host);
            Assert.Equal("/fe/qr/", url.AbsolutePath);
            Assert.True(url.IsDefaultPort);
            Assert.Equal(string.Empty, url.Fragment);

            // Un solo parámetro, p, con base64 válido.
            var parametro = texto["https://www.afip.gob.ar/fe/qr/?p=".Length..];
            Assert.DoesNotContain('&', parametro);
            Assert.DoesNotContain('#', parametro);

            // El JSON es ASCII escapado sin '>', '?', '~' ni DEL: en su base64 no aparecen '+' ni '/' (solo '=' de
            // relleno), así p no necesita codificarse para la URL.
            Assert.DoesNotContain('+', parametro);
            Assert.DoesNotContain('/', parametro);
            Assert.NotEmpty(Convert.FromBase64String(parametro));
        }

        Assert.Equal("https://www.afip.gob.ar/fe/qr/?p=", QrArca.UrlBase);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Datos ficticios

    /// <summary>AC-11: una línea de 3 unidades con importe 10,02 y otra de 2.700,00; total 2.710,02.</summary>
    private static LineaFactura[] Lineas() =>
    [
        new LineaFactura(1, 1, "Lente orgánico", 6.67m, 3, 50m, 3.34m, 10.02m),
        new LineaFactura(2, 2, "Armazón metálico", 2700.00m, 1, 0m, 2700.00m, 2700.00m),
    ];

    /// <summary>AC-08: la Factura B 0003-00000125 del 30/10/2026.</summary>
    private static Factura FacturaB(ReceptorFactura receptor, EntornoArca entorno) =>
        new(155, TipoComprobante.FacturaB, 3, 125, new DateOnly(2026, 10, 30), ConceptoFactura.Productos, receptor,
            ImportesFactura.ParaFacturaB(2710.02m, 21m), Cae, new DateOnly(2026, 11, 9), entorno, Lineas());

    private static Factura FacturaC(ReceptorFactura receptor, EntornoArca entorno) =>
        new(155, TipoComprobante.FacturaC, 3, 125, new DateOnly(2026, 10, 30), ConceptoFactura.Productos, receptor,
            ImportesFactura.ParaFacturaC(2710.02m), Cae, new DateOnly(2026, 11, 9), entorno, Lineas());

    /// <summary>El JSON del parámetro p de la URL del QR.</summary>
    private static JsonElement Decodificar(string url)
    {
        Assert.StartsWith(QrArca.UrlBase, url, StringComparison.Ordinal);
        var base64 = url[QrArca.UrlBase.Length..];
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        using var documento = JsonDocument.Parse(json);
        return documento.RootElement.Clone();
    }
}
