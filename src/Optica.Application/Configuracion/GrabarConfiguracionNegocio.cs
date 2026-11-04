using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Application.Configuracion;

/// <summary>
/// Los cuatro parámetros tal como llegan; la condición fiscal en su texto de la Api. Un tope ausente es válido y deja
/// la configuración sin tope (D-02).
/// </summary>
public sealed record DatosConfiguracion(
    decimal? AlicuotaIva, string? CondicionFiscal, decimal? MultiploRedondeo, decimal? TopeIdentificacion);

/// <summary>
/// Graba los cuatro parámetros juntos; la última grabación gana (D-03). Si algún campo incumple la política devuelve
/// <see cref="Resultado.Invalido"/> con todos los errores, uno por campo, sin leer, grabar ni registrar nada. Si no,
/// lee la vigente, la reemplaza, confirma y registra en el log los valores anteriores y nuevos con la fecha y hora UTC
/// (R-04, D-06). Sin fila de configuración (D-04) y ante cualquier falla al confirmar, la excepción se propaga y no se
/// registra la grabación.
/// </summary>
public sealed partial class GrabarConfiguracionNegocio(
    IConfiguracionNegocioRepository configuracion,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj,
    ILogger<GrabarConfiguracionNegocio> registro)
{
    public async Task<Resultado> EjecutarAsync(DatosConfiguracion datos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var errores = ValidacionConfiguracion.Validar(datos);
        if (errores.Count > 0)
        {
            return Resultado.Invalido([.. errores]);
        }

        // La política ya aceptó los cuatro: los valores existen y la condición es un texto conocido.
        var nueva = new ConfiguracionNegocio(
            datos.AlicuotaIva!.Value,
            PoliticaConfiguracionNegocio.LeerCondicion(datos.CondicionFiscal)!.Value,
            datos.MultiploRedondeo!.Value,
            datos.TopeIdentificacion);

        // La vigente solo se lee para el registro: el reemplazo no depende de ella.
        var anterior = await configuracion.ObtenerAsync(cancelacion);
        configuracion.Reemplazar(nueva);
        await unidadDeTrabajo.ConfirmarAsync(cancelacion);

        RegistrarGrabacion(
            anterior.AlicuotaIva,
            PoliticaConfiguracionNegocio.TextoDe(anterior.CondicionFiscal),
            anterior.MultiploRedondeo,
            anterior.TopeIdentificacion,
            nueva.AlicuotaIva,
            PoliticaConfiguracionNegocio.TextoDe(nueva.CondicionFiscal),
            nueva.MultiploRedondeo,
            nueva.TopeIdentificacion,
            reloj.GetUtcNow().UtcDateTime);

        return Resultado.Exito();
    }

    // Solo los cuatro valores y la fecha: nunca el cuerpo del pedido (R-04).
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Configuración de negocio grabada. Anterior: alícuota {AlicuotaIvaAnterior}, condición " +
            "{CondicionFiscalAnterior}, múltiplo {MultiploRedondeoAnterior}, tope {TopeIdentificacionAnterior}. " +
            "Nueva: alícuota {AlicuotaIva}, condición {CondicionFiscal}, múltiplo {MultiploRedondeo}, tope " +
            "{TopeIdentificacion}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private partial void RegistrarGrabacion(
        decimal alicuotaIvaAnterior,
        string condicionFiscalAnterior,
        decimal multiploRedondeoAnterior,
        decimal? topeIdentificacionAnterior,
        decimal alicuotaIva,
        string condicionFiscal,
        decimal multiploRedondeo,
        decimal? topeIdentificacion,
        DateTime fechaHoraUtc);
}
