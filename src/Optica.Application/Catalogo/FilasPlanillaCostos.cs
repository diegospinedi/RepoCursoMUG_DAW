using Optica.Domain.Catalogo;

namespace Optica.Application.Catalogo;

/// <summary>
/// Una fila no aplicada de la planilla de costos, sin tipos del dominio: el número de fila, el código tal como vino
/// (sin acotar, D-10) y el texto de la razón (FR-13 de FEAT-011).
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>FilaNoAplicadaPlanilla { }</c>): el código es un dato
/// comercial del proveedor y no llega a un log por esa vía (R-05).
/// </remarks>
public sealed record FilaNoAplicadaPlanilla(int Fila, string Codigo, string Razon)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Una fila aplicada para revisar (FR-14, FR-15 de FEAT-011), sin tipos del dominio: el número de fila, el código tal
/// como vino (sin acotar, D-10), el precio de costo aplicado y sus indicaciones. Nada más del artículo: ni margen, ni
/// precio de venta, ni versión, ni descripción, ni estado (R-05).
/// </summary>
/// <remarks><see cref="object.ToString"/> no muestra ningún valor, como <see cref="FilaNoAplicadaPlanilla"/>.</remarks>
public sealed record FilaParaRevisarPlanilla(int Fila, string Codigo, decimal PrecioCosto, IReadOnlyList<string> Indicaciones)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Las filas de <see cref="ResultadoPlanillaCostos.Aplicada"/> sin tipos del dominio, para la Api, que no compila contra
/// <c>Optica.Domain</c>: elige de <see cref="ResultadoFilaPlanilla"/> y del <see cref="Articulo"/> solo lo que se muestra
/// (R-05). Acotar el código para mostrarlo (D-10) es de la Api.
/// </summary>
public static class FilasPlanillaCostos
{
    /// <summary>Las filas no aplicadas, en el orden de la hoja, con el texto de su razón.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="aplicada"/> es null.</exception>
    public static IReadOnlyList<FilaNoAplicadaPlanilla> FilasNoAplicadas(this ResultadoPlanillaCostos.Aplicada aplicada)
    {
        ArgumentNullException.ThrowIfNull(aplicada);
        return
        [
            .. aplicada.NoAplicadas.Select(fila => new FilaNoAplicadaPlanilla(
                fila.Numero,
                fila.Codigo,
                (fila.Razon ?? throw new InvalidOperationException("Una fila no aplicada no tiene razón.")).Texto())),
        ];
    }

    /// <summary>Las filas aplicadas para revisar, en el orden de la hoja, con el costo aplicado y sus indicaciones.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="aplicada"/> es null.</exception>
    public static IReadOnlyList<FilaParaRevisarPlanilla> FilasParaRevisar(this ResultadoPlanillaCostos.Aplicada aplicada)
    {
        ArgumentNullException.ThrowIfNull(aplicada);
        return
        [
            .. aplicada.ParaRevisar.Select(fila => new FilaParaRevisarPlanilla(
                fila.Numero,
                fila.Codigo,
                (fila.Articulo ?? throw new InvalidOperationException("Una fila para revisar no está aplicada.")).PrecioCosto,
                fila.Indicaciones)),
        ];
    }
}
