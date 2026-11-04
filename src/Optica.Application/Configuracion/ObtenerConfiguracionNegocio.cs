using Optica.Domain.Configuracion;

namespace Optica.Application.Configuracion;

/// <summary>
/// Los parámetros de negocio vigentes, con la condición fiscal en su texto de la Api (<c>responsable_inscripto</c> o
/// <c>monotributo</c>) y <see cref="FaltaTope"/> verdadero mientras el tope no tenga valor (FR-08).
/// </summary>
public sealed record ConfiguracionVigente(
    decimal AlicuotaIva, string CondicionFiscal, decimal MultiploRedondeo, decimal? TopeIdentificacion, bool FaltaTope)
{
    internal static ConfiguracionVigente Desde(ConfiguracionNegocio configuracion) => new(
        configuracion.AlicuotaIva,
        PoliticaConfiguracionNegocio.TextoDe(configuracion.CondicionFiscal),
        configuracion.MultiploRedondeo,
        configuracion.TopeIdentificacion,
        configuracion.FaltaTope);
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
