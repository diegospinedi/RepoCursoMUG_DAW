using Optica.Domain.Catalogo;

namespace Optica.Domain.Presupuestos;

/// <summary>
/// Las invariantes de una línea guardada, que comparten la línea del presupuesto y la de la factura (D-34 de FEAT-007):
/// código, descripción, importes, Cantidad y Porcentaje de Descuento. Los mensajes nombran el campo y no incluyen el
/// valor recibido (D-17 de FEAT-003a).
/// </summary>
internal static class ReglasLinea
{
    internal const string MensajeCodigoArticuloInvalido = "El código de artículo no es válido.";
    internal const string MensajeDescripcionInvalida = "La descripción no es válida.";

    /// <summary>Valida los datos de una línea en el orden código, descripción, Precio Unitario, Cantidad, descuento y precios.</summary>
    /// <exception cref="ArgumentException">
    /// El código no es mayor que 0; la descripción falta, no está recortada, tiene UTF-16 inválido o más de 200
    /// caracteres; algún importe es negativo, tiene más de 2 decimales o no entra en el tipo; la cantidad no está entre
    /// 1 y 9.999; o el descuento no está entre 0 y 100 con hasta 2 decimales.
    /// </exception>
    internal static void Validar(
        long codigoArticulo, string? descripcion, decimal precioUnitario, int cantidad, decimal porcentajeDescuento,
        decimal precioConDescuento, decimal precioFinal)
    {
        if (codigoArticulo <= 0)
        {
            throw new ArgumentException(MensajeCodigoArticuloInvalido, nameof(codigoArticulo));
        }

        if (!EsDescripcionValida(descripcion))
        {
            throw new ArgumentException(MensajeDescripcionInvalida, nameof(descripcion));
        }

        CalculoLinea.ValidarImporte(
            precioUnitario, nameof(precioUnitario), CalculoLinea.MensajePrecioUnitarioInvalido);

        CalculoLinea.ValidarCantidad(cantidad);
        CalculoLinea.ValidarDescuento(porcentajeDescuento);

        CalculoLinea.ValidarImporte(
            precioConDescuento, nameof(precioConDescuento), CalculoLinea.MensajePrecioConDescuentoInvalido);
        CalculoLinea.ValidarImporte(precioFinal, nameof(precioFinal), CalculoLinea.MensajePrecioFinalInvalido);
    }

    // Ya recortada, así el largo se mide sobre el texto guardado.
    internal static bool EsDescripcionValida(string? descripcion) =>
        descripcion is not null
        && descripcion.Length > 0
        && NormalizacionCodigo.Recortar(descripcion) == descripcion
        && descripcion.Length <= PoliticaArticulo.LargoMaximoDescripcion
        && NormalizacionCodigo.EsUtf16Valido(descripcion);
}
