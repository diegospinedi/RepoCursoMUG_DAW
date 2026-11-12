using System.Diagnostics;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>
/// Lo que comparten el alta (<see cref="AltaPresupuesto"/>) y la modificación (<see cref="ModificarPresupuesto"/>) para
/// armar un presupuesto con datos ya validados: el cliente del dominio, las líneas pedidas, una línea nueva desde el
/// catálogo con sus rechazos y las líneas válidas después de controlar los topes. Así una regla de las líneas nuevas no
/// cambia en un caso de uso y queda vieja en el otro.
/// </summary>
internal static class ArmadoLineas
{
    /// <summary>El cliente del dominio con los datos que la validación ya aceptó (apellido, nombre y DNI presentes).</summary>
    public static ClientePresupuesto Cliente(DatosCliente datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        return new ClientePresupuesto(
            datos.Apellido!, datos.Nombre!, datos.Dni!, datos.Domicilio, datos.Email, datos.Telefono);
    }

    /// <summary>
    /// Las líneas que la validación ya aceptó (cada una presente, con código, cantidad y descuento), en orden, con su
    /// <c>orden</c> tal como llegó (el alta no lo usa).
    /// </summary>
    public static List<LineaPedida> Pedidas(IReadOnlyList<DatosLinea?> lineas)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        return
        [
            .. lineas.Select(linea => new LineaPedida(
                linea!.Orden, linea.CodigoArticulo!.Value, linea.Cantidad!.Value, linea.PorcentajeDescuento!.Value)),
        ];
    }

    /// <summary>
    /// Una línea nueva, con el artículo del catálogo (D-14 de FEAT-003a): inexistente o inactivo, el error de artículo no
    /// disponible en <c>lineas[i]</c>; precio de venta negativo, el de FR-12; si no, el resultado, válido o con el importe
    /// excedido, que se controla después con <see cref="ValidacionPresupuesto.ControlarTopes"/> (D-11).
    /// </summary>
    /// <exception cref="UnreachableException">El dominio devolvió un rechazo sin error asignado.</exception>
    public static async Task<LineaNueva> LineaNuevaAsync(
        IArticuloRepository articulos, LineaPedida pedida, int indice, CancellationToken operacion)
    {
        ArgumentNullException.ThrowIfNull(articulos);
        ArgumentNullException.ThrowIfNull(pedida);
        var articulo = await articulos.ObtenerAsync(pedida.CodigoArticulo, operacion);
        if (articulo is null)
        {
            return LineaNueva.Rechazada(ValidacionPresupuesto.ArticuloNoDisponible(indice));
        }

        var resultado = LineaPresupuesto.DesdeArticulo(articulo, pedida.Cantidad, pedida.PorcentajeDescuento);
        if (resultado.EsValida || resultado.EsImporteExcedido)
        {
            return LineaNueva.Aceptada(resultado);
        }

        if (resultado.EsPrecioNegativo)
        {
            return LineaNueva.Rechazada(ValidacionPresupuesto.PrecioNegativo(indice));
        }

        if (resultado.EsInactivo)
        {
            return LineaNueva.Rechazada(ValidacionPresupuesto.ArticuloNoDisponible(indice));
        }

        throw new UnreachableException("Rechazo de línea sin error asignado.");
    }

    /// <summary>Las líneas de los resultados, en orden. Sin errores, ControlarTopes ya aceptó todas como válidas.</summary>
    /// <exception cref="UnreachableException">Algún resultado no tiene línea: el control previo no se hizo.</exception>
    public static List<LineaPresupuesto> LineasValidas(IReadOnlyList<ResultadoLinea> resultados)
    {
        ArgumentNullException.ThrowIfNull(resultados);
        var lineas = new List<LineaPresupuesto>(resultados.Count);
        foreach (var resultado in resultados)
        {
            if (!resultado.EsValida)
            {
                throw new UnreachableException("Línea rechazada después de controlar los topes.");
            }

            lineas.Add(resultado.Linea);
        }

        return lineas;
    }
}

/// <summary>
/// Una línea ya validada, sin el artículo leído: su <c>orden</c> si se refiere a una línea grabada (solo en la
/// modificación), el código, la Cantidad y el Descuento.
/// </summary>
/// <remarks>Sin valores en <see cref="object.ToString"/>, como <see cref="DatosLinea"/> (D-17 de FEAT-003a).</remarks>
internal sealed record LineaPedida(int? Orden, long CodigoArticulo, int Cantidad, decimal PorcentajeDescuento)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>Una línea nueva aceptada, con su resultado, o rechazada, con su error: uno de los dos.</summary>
internal sealed class LineaNueva
{
    private LineaNueva(ResultadoLinea? resultado, ErrorValidacion? error)
    {
        Resultado = resultado;
        Error = error;
    }

    /// <summary>El resultado del dominio (válido o con el importe excedido); null si se rechazó.</summary>
    public ResultadoLinea? Resultado { get; }

    /// <summary>El error de la línea; null si se aceptó.</summary>
    public ErrorValidacion? Error { get; }

    public static LineaNueva Aceptada(ResultadoLinea resultado) => new(resultado, null);

    public static LineaNueva Rechazada(ErrorValidacion error) => new(null, error);
}
