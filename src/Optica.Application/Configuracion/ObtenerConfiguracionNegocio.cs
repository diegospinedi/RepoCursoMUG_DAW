using Optica.Domain.Configuracion;

namespace Optica.Application.Configuracion;

/// <summary>
/// Los parámetros de negocio vigentes, con la condición fiscal en su texto de la Api (<c>responsable_inscripto</c> o
/// <c>monotributo</c>), <see cref="FaltaTope"/> verdadero mientras el tope no tenga valor (FR-08), el punto de venta
/// (null mientras no se cargue, FR-28 de FEAT-007) y las 6 alícuotas que acepta ARCA, para la lista de la pantalla
/// (AC-28). La alícuota puede no estar entre ellas si se grabó antes de FR-26 (D-06).
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>ConfiguracionVigente { }</c>, D-28 de FEAT-002c): la
/// alícuota, la condición fiscal y el tope son información comercial confidencial y no llegan al log por esa vía.
/// <see cref="AlicuotasAdmitidas"/> sin valor toma <see cref="Domain.Configuracion.AlicuotasAdmitidas.Todas"/>, la
/// misma instancia siempre: dos configuraciones vigentes con los mismos valores son iguales.
/// </remarks>
public sealed record ConfiguracionVigente(
    decimal AlicuotaIva, string CondicionFiscal, decimal MultiploRedondeo, decimal? TopeIdentificacion, bool FaltaTope,
    int? PuntoVenta = null, IReadOnlyList<decimal>? AlicuotasAdmitidas = null)
{
    public IReadOnlyList<decimal> AlicuotasAdmitidas { get; init; } =
        AlicuotasAdmitidas ?? Domain.Configuracion.AlicuotasAdmitidas.Todas;

    internal static ConfiguracionVigente Desde(ConfiguracionNegocio configuracion) => new(
        configuracion.AlicuotaIva,
        PoliticaConfiguracionNegocio.TextoDe(configuracion.CondicionFiscal),
        configuracion.MultiploRedondeo,
        configuracion.TopeIdentificacion,
        configuracion.FaltaTope,
        configuracion.PuntoVenta);

    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Consulta la configuración de negocio vigente leyendo el repositorio en cada llamada, sin caché (FR-06). Sin fila
/// de configuración propaga la <see cref="InvalidOperationException"/> del repositorio (D-04).
/// </summary>
public sealed class ObtenerConfiguracionNegocio(IConfiguracionNegocioRepository configuracion)
{
    public async Task<ConfiguracionVigente> EjecutarAsync(CancellationToken cancelacion = default) =>
        ConfiguracionVigente.Desde(await configuracion.ObtenerAsync(cancelacion));
}
