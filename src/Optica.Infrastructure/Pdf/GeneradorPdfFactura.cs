using System.Globalization;
using System.Text;
using Optica.Application.Facturacion;
using Optica.Domain.Marca;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Optica.Infrastructure.Pdf;

/// <summary>
/// El PDF de una factura autorizada con QuestPDF (ADR-018) y el QR de ARCA con QRCoder (ADR-027): página A4 con el
/// fondo de la marca, el logo y el comprobante en el color primario, los datos del emisor con su condición frente al
/// IVA, el receptor, las líneas, el total, la leyenda de la Ley 27.743 en la Factura B, el CAE con su vencimiento y el
/// QR (FR-06 a FR-13). Si la factura no tiene validez fiscal, el encabezado de cada página lleva "COMPROBANTE SIMULADO —
/// SIN VALIDEZ FISCAL" (FR-14, FR-15, AGENTS.md). Los importes llegan en centavos y solo se formatean (NFR-02).
/// <para>
/// Lleva datos personales: no registra nada en el log, el título del documento lleva solo el comprobante, la URL del QR
/// no se escribe como texto, y toda falla de QuestPDF o de QRCoder, cuyo mensaje puede copiar lo que no pudo dibujar o
/// codificar, se relanza con un texto fijo y sin la excepción original (D-25). Un logo que QuestPDF no puede decodificar
/// no frena la factura: se dibuja sin logo.
/// </para>
/// <para>
/// QRCoder se usa solo con <see cref="PngByteQRCode"/>, que arma el PNG en memoria sin <c>System.Drawing</c>, sin leer
/// archivos ni abrir la red (R-08).
/// </para>
/// </summary>
public sealed class GeneradorPdfFactura : IGeneradorPdfFactura
{
    /// <summary>La leyenda de FR-14, tal como la exige AGENTS.md. No se quita.</summary>
    internal const string LeyendaSimulado = "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL";

    private const string MensajeFalla = "No se pudo generar el PDF de la factura.";
    private const string LeyendaLey27743 = "Régimen de Transparencia Fiscal al Consumidor (Ley 27.743)";

    /// <summary>Píxeles por módulo del PNG del QR: nítido al tamaño en que se dibuja.</summary>
    private const int PixelesPorModulo = 10;

    /// <summary>Coma decimal y punto de miles (D-09 de FEAT-006), fijos: el PDF no depende de la cultura del sistema.</summary>
    private static readonly NumberFormatInfo FormatoNumeros = NumberFormatInfo.ReadOnly(new NumberFormatInfo
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
        NegativeSign = "-",
    });

    static GeneradorPdfFactura()
    {
        ConfiguracionQuestPdf.Asegurar();
    }

    /// <inheritdoc />
    public byte[] Generar(DocumentoFactura documento)
    {
        ArgumentNullException.ThrowIfNull(documento);

        try
        {
            var qr = GenerarQr(documento.UrlQr);
            var logo = Logo.Decodificar(documento.Marca.Logo);
            try
            {
                return Document
                    .Create(contenedor => contenedor.Page(pagina => ArmarPagina(pagina, documento, logo, qr)))
                    .WithMetadata(new DocumentMetadata
                    {
                        Title = documento.Comprobante.ToString(),
                        Author = string.Empty,
                        Subject = string.Empty,
                        Keywords = string.Empty,
                    })
                    .GeneratePdf();
            }
            finally
            {
                logo?.Dispose();
            }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or InsufficientExecutionStackException))
        {
            // Sin la excepción original: su mensaje puede copiar el texto que no se pudo dibujar o codificar (el DNI del
            // receptor, la URL del QR).
            throw new InvalidOperationException(MensajeFalla);
        }
    }

    /// <summary>El PNG del QR de la URL de ARCA, en memoria (ADR-027).</summary>
    private static byte[] GenerarQr(string url)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(datos).GetGraphic(PixelesPorModulo);
    }

    private static void ArmarPagina(PageDescriptor pagina, DocumentoFactura documento, Logo? logo, byte[] qr)
    {
        var primario = Color.FromHex(documento.Marca.ColorPrimario);
        pagina.Size(PageSizes.A4);
        pagina.Margin(1.5f, Unit.Centimetre);
        pagina.PageColor(Color.FromHex(documento.Marca.ColorFondo));
        pagina.DefaultTextStyle(estilo => estilo.FontSize(10));

        // El encabezado se repite en cada página: la leyenda de comprobante simulado queda en todas (FR-14, R-05).
        pagina.Header().PaddingBottom(10).Column(columna =>
        {
            if (documento.LlevaLeyendaSimulado)
            {
                columna.Item().PaddingBottom(6).Border(1).BorderColor(Colors.Red.Darken2).Padding(4).AlignCenter()
                    .Text(texto => texto.Span(LeyendaSimulado).FontSize(13).Bold().FontColor(Colors.Red.Darken2));
            }

            columna.Item().Row(fila =>
            {
                fila.ConstantItem(160).Height(60).AlignLeft().AlignMiddle().Element(celda => logo?.Dibujar(celda));
                fila.RelativeItem().AlignRight().AlignMiddle().Column(datos =>
                {
                    datos.Item().AlignRight().Text(texto =>
                        texto.Span("Factura " + Letra(documento)).FontSize(18).Bold().FontColor(primario));
                    datos.Item().AlignRight().Text(texto => texto.Span(
                        string.Create(CultureInfo.InvariantCulture, $"Punto de venta: {documento.Comprobante.PuntoVenta:D4}"))
                        .FontColor(primario));
                    datos.Item().AlignRight().Text(texto => texto.Span(
                        string.Create(CultureInfo.InvariantCulture, $"Número: {documento.Comprobante.Numero:D8}"))
                        .FontColor(primario));
                    datos.Item().AlignRight().Text(texto =>
                        texto.Span("Fecha de emisión: " + Fecha(documento.Fecha)).FontColor(primario));
                });
            });
        });

        pagina.Content().Column(columna =>
        {
            columna.Spacing(10);
            columna.Item().Element(celda => DibujarEmisor(celda, documento));
            columna.Item().Element(celda => DibujarReceptor(celda, documento));
            columna.Item().Element(celda => DibujarLineas(celda, documento.Lineas, primario));
            columna.Item().AlignRight().Text(texto =>
                texto.Span("Total: $ " + Importe(documento.TotalCentavos)).FontSize(12).Bold());

            // FR-11: solo la Factura B, con el IVA contenido calculado al emitirla y sin discriminarlo por línea. FR-12:
            // la C no muestra ningún importe de IVA.
            if (documento.IvaContenidoCentavos is { } iva)
            {
                columna.Item().Column(leyenda =>
                {
                    leyenda.Item().Text(LeyendaLey27743);
                    leyenda.Item().Text("IVA contenido: $ " + Importe(iva));
                });
            }

            columna.Item().Row(fila =>
            {
                fila.ConstantItem(100).Height(100).Image(qr).FitArea();
                fila.RelativeItem().PaddingLeft(10).AlignMiddle().Column(cae =>
                {
                    cae.Item().Text(texto =>
                    {
                        texto.Span("CAE: ").SemiBold();
                        texto.Span(documento.Cae);
                    });
                    cae.Item().Text(texto =>
                    {
                        texto.Span("Vencimiento del CAE: ").SemiBold();
                        texto.Span(Fecha(documento.VencimientoCae));
                    });
                });
            });
        });

        pagina.Footer().AlignCenter().Text(texto =>
        {
            texto.Span("Página ");
            texto.CurrentPageNumber();
            texto.Span(" de ");
            texto.TotalPages();
        });
    }

    /// <summary>Los cinco datos del emisor y su condición frente al IVA (FR-06).</summary>
    private static void DibujarEmisor(IContainer celda, DocumentoFactura documento)
    {
        (string Etiqueta, string Valor)[] datos =
        [
            ("Razón social", documento.RazonSocial),
            ("Domicilio comercial", documento.DomicilioComercial),
            ("CUIT", Cuit(documento.CuitEmisor)),
            ("Ingresos Brutos", documento.IngresosBrutos),
            ("Inicio de actividades", Fecha(documento.InicioActividades)),
            ("Condición frente al IVA", documento.CondicionIva),
        ];
        DibujarDatos(celda, datos);
    }

    /// <summary>"Consumidor Final" sin DNI, o el DNI con Apellido y Nombre (FR-08).</summary>
    private static void DibujarReceptor(IContainer celda, DocumentoFactura documento)
    {
        if (documento.EsConsumidorFinalSinIdentificar)
        {
            DibujarDatos(celda, [("Receptor", "Consumidor Final")]);
            return;
        }

        DibujarDatos(
            celda,
            [
                ("DNI", documento.DniReceptor!),
                ("Apellido", documento.ApellidoReceptor!),
                ("Nombre", documento.NombreReceptor!),
            ]);
    }

    private static void DibujarDatos(IContainer celda, (string Etiqueta, string Valor)[] datos)
    {
        celda.Column(columna =>
        {
            columna.Spacing(2);
            foreach (var (etiqueta, valor) in datos)
            {
                columna.Item().Text(texto =>
                {
                    texto.Span(etiqueta + ": ").SemiBold();
                    texto.Span(valor);
                });
            }
        });
    }

    /// <summary>Cada línea en su orden con descripción, cantidad e importe (FR-09).</summary>
    private static void DibujarLineas(IContainer celda, IReadOnlyList<LineaDocumentoFactura> lineas, Color primario)
    {
        celda.Table(tabla =>
        {
            tabla.ColumnsDefinition(columnas =>
            {
                columnas.RelativeColumn(5);
                columnas.RelativeColumn(1.2f);
                columnas.RelativeColumn(1.8f);
            });

            // El encabezado se repite en cada página si las líneas no entran en una.
            tabla.Header(encabezado =>
            {
                foreach (var titulo in new[] { "Descripción", "Cantidad", "Importe ($)" })
                {
                    encabezado.Cell().BorderBottom(1).BorderColor(primario).Padding(3)
                        .Text(texto => texto.Span(titulo).SemiBold().FontColor(primario));
                }
            });

            foreach (var linea in lineas)
            {
                Celda(tabla, linea.Descripcion, alDerecha: false);
                Celda(tabla, linea.Cantidad.ToString(CultureInfo.InvariantCulture), alDerecha: true);
                Celda(tabla, Importe(linea.ImporteCentavos), alDerecha: true);
            }
        });
    }

    private static void Celda(TableDescriptor tabla, string valor, bool alDerecha)
    {
        var celda = tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
        if (alDerecha)
        {
            celda = celda.AlignRight();
        }

        celda.Text(valor);
    }

    private static string Letra(DocumentoFactura documento) =>
        documento.Tipo == Domain.Facturacion.TipoComprobante.FacturaB ? "B" : "C";

    private static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>El CUIT con sus guiones: "20-00000000-1".</summary>
    private static string Cuit(string canonico) => $"{canonico[..2]}-{canonico[2..10]}-{canonico[10..]}";

    /// <summary>
    /// Un importe en centavos con 2 decimales, coma decimal y punto de miles, sin pasar por punto flotante: 1002 →
    /// "10,02", 271002 → "2.710,02" (NFR-02). El documento garantiza que no es negativo.
    /// </summary>
    private static string Importe(long centavos) =>
        (centavos / 100).ToString("#,##0", FormatoNumeros) + ","
        + (centavos % 100).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// El logo ya decodificado por QuestPDF, una sola vez por documento: se repite en el encabezado de cada página. Si no
    /// se puede decodificar, la factura se dibuja sin logo.
    /// </summary>
    private sealed class Logo : IDisposable
    {
        private readonly Image? _imagen;
        private readonly SvgImage? _svg;

        private Logo(Image? imagen, SvgImage? svg)
        {
            _imagen = imagen;
            _svg = svg;
        }

        public static Logo? Decodificar(LogoMarca logo)
        {
            try
            {
                var contenido = logo.Contenido.ToArray();
                if (logo.Formato == FormatoLogo.Svg)
                {
                    // El dominio ya validó el SVG: XML bien formado, sin DTD ni entidades externas (R-06 de FEAT-006).
                    using var lector = new StreamReader(
                        new MemoryStream(contenido, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    return new Logo(null, SvgImage.FromText(lector.ReadToEnd()));
                }

                return new Logo(Image.FromBinaryData(contenido), null);
            }
            catch (Exception error) when (error is not (OutOfMemoryException or InsufficientExecutionStackException))
            {
                return null;
            }
        }

        public void Dibujar(IContainer celda)
        {
            if (_svg is not null)
            {
                celda.Svg(_svg).FitArea();
            }
            else if (_imagen is not null)
            {
                celda.Image(_imagen).FitArea();
            }
        }

        public void Dispose()
        {
            (_imagen as IDisposable)?.Dispose();
            (_svg as IDisposable)?.Dispose();
        }
    }
}
