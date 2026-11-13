using System.Globalization;
using System.Text;
using Optica.Application.Presupuestos;
using Optica.Domain.Marca;
using Optica.Domain.Presupuestos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Optica.Infrastructure.Pdf;

/// <summary>
/// El PDF del presupuesto en Final con QuestPDF (ADR-018): página A4 con el fondo de la marca, el logo, el número y la
/// fecha en el color primario, los datos cargados del cliente, las líneas con sus siete campos, el total y la leyenda
/// de IVA incluido (FR-13 a FR-16). Los importes son los grabados, solo formateados (R-05).
/// <para>
/// Lleva datos personales: no registra nada en el log, el título del documento lleva solo el número y toda falla de
/// QuestPDF, cuyo mensaje copia el texto que no pudo dibujar, se relanza con un texto fijo y sin la excepción original
/// (R-10, D-15).
/// </para>
/// </summary>
public sealed class GeneradorPdfPresupuesto : IGeneradorPdfPresupuesto
{
    private const string MensajeBorrador = "El PDF solo se genera para un presupuesto en Final.";
    private const string MensajeFalla = "No se pudo generar el PDF del presupuesto.";
    private const string Leyenda = "Precios finales, IVA incluido";

    /// <summary>
    /// Coma decimal y punto de miles (D-09), fijos en el código: el PDF no depende de la cultura del sistema y muestra
    /// lo mismo que la pantalla (<c>mostrarImporte</c> y <c>mostrarDecimal</c>).
    /// </summary>
    private static readonly NumberFormatInfo FormatoNumeros = NumberFormatInfo.ReadOnly(new NumberFormatInfo
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
        NegativeSign = "-",
    });

    /// <summary>
    /// La configuración global de QuestPDF, una sola vez y antes del primer documento: licencia Community (D-01, D-13),
    /// sin fuentes del sistema para que el PDF sea el mismo en Windows y en Linux (D-14), una familia que falta (el
    /// Arial del SVG de ejemplo) se dibuja con la fuente incluida (D-14) y un carácter que la fuente no tiene se dibuja
    /// con el símbolo de reemplazo (D-15).
    /// </summary>
    static GeneradorPdfPresupuesto()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }

    /// <inheritdoc />
    public byte[] Generar(Presupuesto presupuesto, IdentidadVisual marca)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        ArgumentNullException.ThrowIfNull(marca);
        if (presupuesto.Estado != EstadoPresupuesto.Final)
        {
            throw new ArgumentException(MensajeBorrador, nameof(presupuesto));
        }

        try
        {
            return Document
                .Create(contenedor => contenedor.Page(pagina => ArmarPagina(pagina, presupuesto, marca)))
                .WithMetadata(new DocumentMetadata
                {
                    Title = string.Create(CultureInfo.InvariantCulture, $"Presupuesto {presupuesto.Numero}"),
                    Author = string.Empty,
                    Subject = string.Empty,
                    Keywords = string.Empty,
                })
                .GeneratePdf();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or InsufficientExecutionStackException))
        {
            // Sin la excepción original: su mensaje puede copiar el texto que no se pudo dibujar (datos del cliente).
            throw new InvalidOperationException(MensajeFalla);
        }
    }

    private static void ArmarPagina(PageDescriptor pagina, Presupuesto presupuesto, IdentidadVisual marca)
    {
        var primario = Color.FromHex(marca.ColorPrimario);
        pagina.Size(PageSizes.A4);
        pagina.Margin(1.5f, Unit.Centimetre);
        pagina.PageColor(Color.FromHex(marca.ColorFondo));
        pagina.DefaultTextStyle(estilo => estilo.FontSize(10));

        pagina.Header().PaddingBottom(12).Row(fila =>
        {
            // Solo el logo, el número y la fecha: ningún otro dato de la óptica (FR-16).
            fila.ConstantItem(160).Height(60).AlignLeft().AlignMiddle().Element(celda => DibujarLogo(celda, marca.Logo));
            fila.RelativeItem().AlignRight().AlignMiddle().Column(columna =>
            {
                columna.Item().Text(texto =>
                    texto.Span(string.Create(CultureInfo.InvariantCulture, $"Presupuesto N.º {presupuesto.Numero}"))
                        .FontSize(16).Bold().FontColor(primario));
                columna.Item().AlignRight().Text(texto =>
                    texto.Span("Fecha: " + presupuesto.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))
                        .FontColor(primario));
            });
        });

        pagina.Content().Column(columna =>
        {
            columna.Spacing(12);
            columna.Item().Element(celda => DibujarCliente(celda, presupuesto.Cliente));
            columna.Item().Element(celda => DibujarLineas(celda, presupuesto.Lineas, primario));
            columna.Item().AlignRight().Text(texto =>
                texto.Span("Total: $ " + Importe(presupuesto.Total)).FontSize(12).Bold());
            columna.Item().Text(Leyenda);
        });
    }

    private static void DibujarLogo(IContainer celda, LogoMarca logo)
    {
        var contenido = logo.Contenido.ToArray();
        if (logo.Formato == FormatoLogo.Svg)
        {
            // El dominio ya validó el SVG: XML bien formado, sin DTD ni entidades externas (R-06).
            using var lector = new StreamReader(
                new MemoryStream(contenido, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            celda.Svg(lector.ReadToEnd()).FitArea();
            return;
        }

        celda.Image(contenido).FitArea();
    }

    /// <summary>Solo los datos cargados, cada uno con su etiqueta (FR-13).</summary>
    private static void DibujarCliente(IContainer celda, ClientePresupuesto cliente)
    {
        (string Etiqueta, string? Valor)[] datos =
        [
            ("Apellido", cliente.Apellido),
            ("Nombre", cliente.Nombre),
            ("DNI", cliente.Dni),
            ("Domicilio", cliente.Domicilio),
            ("Email", cliente.Email),
            ("Nro. de Teléfono", cliente.Telefono),
        ];

        celda.Column(columna =>
        {
            columna.Spacing(2);
            foreach (var (etiqueta, valor) in datos)
            {
                if (string.IsNullOrEmpty(valor))
                {
                    continue;
                }

                columna.Item().Text(texto =>
                {
                    texto.Span(etiqueta + ": ").SemiBold();
                    texto.Span(valor);
                });
            }
        });
    }

    /// <summary>Cada línea en su orden, con los siete campos y los importes grabados (FR-14).</summary>
    private static void DibujarLineas(IContainer celda, IReadOnlyList<LineaPresupuesto> lineas, Color primario)
    {
        celda.Table(tabla =>
        {
            tabla.ColumnsDefinition(columnas =>
            {
                columnas.RelativeColumn(1.1f);
                columnas.RelativeColumn(3);
                columnas.RelativeColumn(1.5f);
                columnas.RelativeColumn(1.1f);
                columnas.RelativeColumn(1.4f);
                columnas.RelativeColumn(1.5f);
                columnas.RelativeColumn(1.5f);
            });

            // El encabezado se repite en cada página si las líneas no entran en una.
            tabla.Header(encabezado =>
            {
                foreach (var titulo in new[]
                {
                    "Código de Artículo", "Descripción", "Precio Unitario ($)", "Cantidad",
                    "Porcentaje de Descuento (%)", "Precio con Descuento ($)", "Precio Final ($)",
                })
                {
                    encabezado.Cell().BorderBottom(1).BorderColor(primario).Padding(3)
                        .Text(texto => texto.Span(titulo).SemiBold().FontColor(primario));
                }
            });

            foreach (var linea in lineas)
            {
                Celda(tabla, linea.CodigoArticulo.ToString(CultureInfo.InvariantCulture), alDerecha: false);
                Celda(tabla, linea.Descripcion, alDerecha: false);
                Celda(tabla, Importe(linea.PrecioUnitario), alDerecha: true);
                Celda(tabla, linea.Cantidad.ToString(CultureInfo.InvariantCulture), alDerecha: true);
                Celda(tabla, Porcentaje(linea.PorcentajeDescuento), alDerecha: true);
                Celda(tabla, Importe(linea.PrecioConDescuento), alDerecha: true);
                Celda(tabla, Importe(linea.PrecioFinal), alDerecha: true);
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

    /// <summary>Un importe con 2 decimales, coma decimal y punto de miles: "2.700,00" (D-09, NFR-03).</summary>
    private static string Importe(decimal valor) => valor.ToString("#,##0.00", FormatoNumeros);

    /// <summary>El descuento con coma decimal y sin ceros a la derecha: "50", "12,5" (D-09).</summary>
    private static string Porcentaje(decimal valor) => valor.ToString("0.##", FormatoNumeros);
}
