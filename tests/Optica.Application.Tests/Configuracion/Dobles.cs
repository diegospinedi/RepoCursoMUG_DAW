using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Configuracion;

/// <summary>
/// La configuración de negocio en memoria para la consulta, leída en cada llamada. Como el repositorio real, sin fila
/// lanza <see cref="InvalidOperationException"/> (D-04). La grabación se prueba con el doble combinado de Catálogo
/// (<c>CatalogoYConfiguracionEnMemoria</c>, D-10 y D-20 de FEAT-002c): aquí no se reemplaza nada.
/// </summary>
internal sealed class ConfiguracionEnMemoria : IConfiguracionNegocioRepository
{
    /// <summary>Los valores iniciales de la migración (FR-07, FR-08).</summary>
    public static ConfiguracionNegocio Inicial() =>
        new(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

    public ConfiguracionNegocio? Guardada { get; set; } = Inicial();

    public int Lecturas { get; private set; }

    public Task<ConfiguracionNegocio> ObtenerAsync(CancellationToken cancelacion = default)
    {
        Lecturas++;
        return Guardada is null
            ? throw new InvalidOperationException("No hay fila de configuración de negocio.")
            : Task.FromResult(Guardada);
    }

    public void Reemplazar(ConfiguracionNegocio nueva) =>
        throw new InvalidOperationException("La consulta de la configuración no la reemplaza.");
}
