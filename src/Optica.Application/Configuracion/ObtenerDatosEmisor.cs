using System.Globalization;
using Optica.Domain.Configuracion;

namespace Optica.Application.Configuracion;

/// <summary>
/// Los datos del emisor vigentes, en la forma del contrato de la Api (FEAT-009, ADR-028): los cinco textos, con el CUIT
/// sin guiones y la fecha de inicio como <c>yyyy-MM-dd</c>; los cinco null mientras nunca se cargaron (D-22).
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>DatosEmisorVigentes { }</c>), como
/// <see cref="ConfiguracionVigente"/>: la razón social y el CUIT pueden ser de una persona física (R-06) y no llegan al
/// log por esa vía.
/// </remarks>
public sealed record DatosEmisorVigentes(
    string? RazonSocial, string? DomicilioComercial, string? Cuit, string? IngresosBrutos, string? InicioActividades)
{
    /// <summary>Los cinco datos sin cargar.</summary>
    public static DatosEmisorVigentes SinCargar { get; } = new(null, null, null, null, null);

    /// <exception cref="ArgumentNullException"><paramref name="datos"/> es null.</exception>
    public static DatosEmisorVigentes Desde(DatosEmisor datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        return new(
            datos.RazonSocial,
            datos.DomicilioComercial,
            datos.Cuit.Valor,
            datos.IngresosBrutos,
            datos.InicioActividades.ToString(DatosEmisor.FormatoFecha, CultureInfo.InvariantCulture));
    }

    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Consulta los datos del emisor leyendo el repositorio en cada llamada, sin caché. Sin datos cargados devuelve
/// <see cref="DatosEmisorVigentes.SinCargar"/>; una fila corrupta propaga la <see cref="InvalidOperationException"/> del
/// repositorio, que la Api responde con su 500 genérico.
/// </summary>
public sealed class ObtenerDatosEmisor(IDatosEmisorRepository datosEmisor)
{
    public async Task<DatosEmisorVigentes> EjecutarAsync(CancellationToken cancelacion = default) =>
        await datosEmisor.ObtenerAsync(cancelacion) is { } datos
            ? DatosEmisorVigentes.Desde(datos)
            : DatosEmisorVigentes.SinCargar;
}
