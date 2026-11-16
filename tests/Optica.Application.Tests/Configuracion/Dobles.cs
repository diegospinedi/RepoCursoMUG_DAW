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

/// <summary>
/// Los datos del emisor en memoria, con su unidad de trabajo (ADR-028, FEAT-009). Como el repositorio real, sin fila
/// devuelve null y <see cref="Reemplazar"/> solo deja el cambio pendiente: lo guarda <see cref="ConfirmarAsync"/>. Como
/// la unidad de trabajo real, una operación que termina con el cambio sin confirmar se revierte y lanza
/// <see cref="InvalidOperationException"/>, y una que lanza deja los datos como estaban.
/// </summary>
internal sealed class DatosEmisorEnMemoria : IDatosEmisorRepository, Application.Comun.IUnidadDeTrabajo
{
    private DatosEmisor? _pendiente;

    /// <summary>Los datos confirmados; null mientras nunca se cargaron.</summary>
    public DatosEmisor? Guardados { get; set; }

    public int Lecturas { get; private set; }

    public int Reemplazos { get; private set; }

    public int Confirmaciones { get; private set; }

    public int Transacciones { get; private set; }

    /// <summary>Lecturas y reemplazos hechos fuera de una operación: la grabación tiene que hacerlos dentro (ADR-013).</summary>
    public int AccesosFueraDeTransaccion { get; private set; }

    /// <summary>Si no es null, <see cref="ObtenerAsync"/> la lanza (una falla de la base al leer).</summary>
    public Exception? ErrorAlLeer { get; set; }

    /// <summary>
    /// Si no es null, <see cref="ConfirmarAsync"/> la lanza sin guardar y descarta lo pendiente, como la unidad de trabajo
    /// real ante una falla al grabar.
    /// </summary>
    public Exception? ErrorAlConfirmar { get; set; }

    private bool EnTransaccion { get; set; }

    /// <summary>Datos válidos armados con las reglas de grabación del dominio.</summary>
    public static DatosEmisor Crear(
        string razonSocial, string domicilioComercial, string cuit, string ingresosBrutos, string inicioActividades)
    {
        var resultado = DatosEmisor.ParaGrabar(
            razonSocial, domicilioComercial, cuit, ingresosBrutos, inicioActividades, new DateOnly(2026, 10, 30));
        return resultado.Datos ?? throw new InvalidOperationException("Los datos de prueba no son válidos.");
    }

    public Task<DatosEmisor?> ObtenerAsync(CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        Lecturas++;
        if (!EnTransaccion)
        {
            AccesosFueraDeTransaccion++;
        }

        if (ErrorAlLeer is { } error)
        {
            throw error;
        }

        return Task.FromResult(Guardados);
    }

    public void Reemplazar(DatosEmisor datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        Reemplazos++;
        if (!EnTransaccion)
        {
            AccesosFueraDeTransaccion++;
        }

        _pendiente = datos;
    }

    public async Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        var guardadosAlEntrar = Guardados;
        Transacciones++;
        EnTransaccion = true;
        try
        {
            var resultado = await operacion(cancelacion);
            if (_pendiente is not null)
            {
                throw new InvalidOperationException("La operación terminó con cambios sin confirmar.");
            }

            return resultado;
        }
        catch
        {
            Guardados = guardadosAlEntrar;
            _pendiente = null;
            throw;
        }
        finally
        {
            EnTransaccion = false;
        }
    }

    public Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        Confirmaciones++;
        if (ErrorAlConfirmar is { } error)
        {
            _pendiente = null;
            throw error;
        }

        if (_pendiente is not null)
        {
            Guardados = _pendiente;
            _pendiente = null;
        }

        return Task.CompletedTask;
    }
}
