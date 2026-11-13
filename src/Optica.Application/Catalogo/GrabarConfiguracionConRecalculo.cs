using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Application.Configuracion;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Application.Catalogo;

/// <summary>
/// Cómo terminó la grabación de la configuración: grabada, con la cantidad de artículos cuyo precio de venta cambió
/// (D-09 de FEAT-002c), o inválida, con un error por campo.
/// </summary>
public sealed class ResultadoGrabacionConfiguracion
{
    private ResultadoGrabacionConfiguracion(int? preciosCambiados, IReadOnlyList<ErrorValidacion> errores)
    {
        PreciosCambiados = preciosCambiados;
        Errores = errores;
    }

    /// <summary>Cuántos precios de venta cambiaron; null si la grabación fue inválida.</summary>
    public int? PreciosCambiados { get; }

    /// <summary>Errores de validación; vacío si la configuración se grabó.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    [MemberNotNullWhen(true, nameof(PreciosCambiados))]
    public bool EsGrabado => PreciosCambiados is not null;

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preciosCambiados"/> es negativo.</exception>
    public static ResultadoGrabacionConfiguracion Grabado(int preciosCambiados)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(preciosCambiados);
        return new ResultadoGrabacionConfiguracion(preciosCambiados, []);
    }

    /// <exception cref="ArgumentException">No hay errores.</exception>
    public static ResultadoGrabacionConfiguracion Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Una grabación inválida necesita al menos un error.", nameof(errores));
        }

        return new ResultadoGrabacionConfiguracion(null, [.. errores]);
    }
}

/// <summary>
/// Graba los parámetros de negocio juntos y, si cambian la alícuota, la condición fiscal o el múltiplo (D-07),
/// recalcula el precio de venta de todo el catálogo, activos e inactivos, en la misma transacción (FR-01, FR-02 y AC-03
/// de FEAT-002c). Es un caso de uso de Catálogo porque Catálogo depende de Configuración y no al revés (D-10).
/// </summary>
/// <remarks>
/// <para>
/// Si algún campo incumple la política de escritura (con las 6 alícuotas de ARCA, FR-26 de FEAT-007) devuelve
/// <see cref="ResultadoGrabacionConfiguracion.Invalido"/> con todos los errores, sin leer ni grabar nada. Si no, dentro de
/// <see cref="IUnidadDeTrabajo.EjecutarEnTransaccionAsync"/> (ADR-013): lee la vigente, arma la nueva con
/// <see cref="ConfiguracionNegocio.ParaGrabar"/> tomando el punto de venta guardado si el pedido no trae uno (D-27 y D-33
/// de FEAT-007) y, si los parámetros de precio cambian, lista los artículos y recalcula cada uno. Si alguno
/// queda fuera de rango, devuelve inválido con el error de D-01 en el múltiplo sin registrar nada (D-03). Si no,
/// reemplaza la configuración, registra solo los precios que cambian (D-04) y confirma todo junto. La última grabación
/// gana (D-03 de FEAT-001c).
/// </para>
/// <para>
/// Después del commit, y solo si se grabó, registra en el log con la categoría <see cref="CategoriaRegistro"/> los
/// valores anteriores y nuevos (con el punto de venta), la fecha y hora UTC y la cantidad de precios cambiados; nunca códigos ni importes de
/// artículos (R-04 de FEAT-001c, D-11 y D-17). Sin fila de configuración y ante cualquier falla al confirmar (en
/// <see cref="IUnidadDeTrabajo.ConfirmarAsync"/> o en el commit de la operación), la transacción se revierte, la
/// excepción se propaga y no se registra la grabación.
/// </para>
/// </remarks>
public sealed partial class GrabarConfiguracionConRecalculo(
    IConfiguracionNegocioRepository configuracion,
    IArticuloRepository articulos,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj,
    ILoggerFactory fabricaRegistros)
{
    /// <summary>
    /// La categoría del registro de cada grabación (D-17). Empieza con <c>Optica.Application.Configuracion</c>, la clave
    /// con que <c>appsettings.json</c> la envía también al Registro de eventos de Windows (R-04 de FEAT-001c).
    /// </summary>
    public const string CategoriaRegistro = "Optica.Application.Configuracion.Grabacion";

    private readonly ILogger _registro =
        (fabricaRegistros ?? throw new ArgumentNullException(nameof(fabricaRegistros))).CreateLogger(CategoriaRegistro);

    public async Task<ResultadoGrabacionConfiguracion> EjecutarAsync(
        DatosConfiguracion datos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var errores = ValidacionConfiguracion.Validar(datos);
        if (errores.Count > 0)
        {
            return ResultadoGrabacionConfiguracion.Invalido(errores);
        }

        var desenlace = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => GrabarEnTransaccionAsync(datos, operacion), cancelacion);
        if (desenlace.Error is { } error)
        {
            return ResultadoGrabacionConfiguracion.Invalido([error]);
        }

        // Después del commit de la operación: se registra solo lo confirmado (D-11).
        var anterior = desenlace.Anterior!;
        var nueva = desenlace.Nueva!;
        RegistrarGrabacion(
            _registro,
            anterior.AlicuotaIva,
            PoliticaConfiguracionNegocio.TextoDe(anterior.CondicionFiscal),
            anterior.MultiploRedondeo,
            anterior.TopeIdentificacion,
            anterior.PuntoVenta,
            nueva.AlicuotaIva,
            PoliticaConfiguracionNegocio.TextoDe(nueva.CondicionFiscal),
            nueva.MultiploRedondeo,
            nueva.TopeIdentificacion,
            nueva.PuntoVenta,
            desenlace.PreciosCambiados,
            reloj.GetUtcNow().UtcDateTime);

        return ResultadoGrabacionConfiguracion.Grabado(desenlace.PreciosCambiados);
    }

    // Todo lo que lee y graba, dentro de la transacción de escritura (ADR-013, D-05).
    private async Task<Desenlace> GrabarEnTransaccionAsync(DatosConfiguracion datos, CancellationToken cancelacion)
    {
        var anterior = await configuracion.ObtenerAsync(cancelacion);

        // La política ya aceptó los datos: los valores existen y la condición es un texto conocido. Sin punto de venta en
        // el pedido se conserva el guardado, leído en esta misma transacción (D-27 y D-33 de FEAT-007).
        var nueva = ConfiguracionNegocio.ParaGrabar(
            datos.AlicuotaIva!.Value,
            PoliticaConfiguracionNegocio.LeerCondicion(datos.CondicionFiscal)!.Value,
            datos.MultiploRedondeo!.Value,
            datos.TopeIdentificacion,
            datos.PuntoVenta ?? anterior.PuntoVenta);

        var recalculados = new List<Articulo>();
        if (nueva.CambiaParametrosDePrecioRespectoDe(anterior))
        {
            var fueraDeRango = new List<Articulo>();
            foreach (var articulo in await articulos.ListarParaRecalculoAsync(cancelacion))
            {
                var recalculo = articulo.ConPrecioRecalculado(nueva);
                if (recalculo.EsFueraDeRango)
                {
                    fueraDeRango.Add(articulo);
                }
                else if (recalculo.EsCambiado)
                {
                    recalculados.Add(recalculo.Articulo);
                }
            }

            // D-03: el rechazo se decide antes de registrar nada, así no queda nada pendiente.
            if (fueraDeRango.Count > 0)
            {
                return Desenlace.Rechazado(ValidacionArticulo.PrecioVentaRecalculadoFueraDeRango(fueraDeRango));
            }
        }

        configuracion.Reemplazar(nueva);
        foreach (var recalculado in recalculados)
        {
            articulos.RegistrarPrecioRecalculado(recalculado);
        }

        // D-03: la configuración y los precios en una sola confirmación.
        await unidadDeTrabajo.ConfirmarAsync(cancelacion);
        return Desenlace.Grabado(anterior, nueva, recalculados.Count);
    }

    // Solo los valores de la configuración, la cantidad y la fecha: nunca el cuerpo del pedido, códigos ni importes de
    // artículos (R-04 de FEAT-001c, D-11).
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Configuración de negocio grabada. Anterior: alícuota {AlicuotaIvaAnterior}, condición " +
            "{CondicionFiscalAnterior}, múltiplo {MultiploRedondeoAnterior}, tope {TopeIdentificacionAnterior}, punto " +
            "de venta {PuntoVentaAnterior}. Nueva: alícuota {AlicuotaIva}, condición {CondicionFiscal}, múltiplo " +
            "{MultiploRedondeo}, tope {TopeIdentificacion}, punto de venta {PuntoVenta}. Precios de venta cambiados: " +
            "{PreciosCambiados}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarGrabacion(
        ILogger registro,
        decimal alicuotaIvaAnterior,
        string condicionFiscalAnterior,
        decimal multiploRedondeoAnterior,
        decimal? topeIdentificacionAnterior,
        int? puntoVentaAnterior,
        decimal alicuotaIva,
        string condicionFiscal,
        decimal multiploRedondeo,
        decimal? topeIdentificacion,
        int? puntoVenta,
        int preciosCambiados,
        DateTime fechaHoraUtc);

    /// <summary>
    /// Lo que devuelve la operación: grabada, con la anterior, la nueva y la cantidad, o rechazada, con el error.
    /// </summary>
    private sealed record Desenlace(
        ConfiguracionNegocio? Anterior, ConfiguracionNegocio? Nueva, int PreciosCambiados, ErrorValidacion? Error)
    {
        public static Desenlace Grabado(ConfiguracionNegocio anterior, ConfiguracionNegocio nueva, int preciosCambiados) =>
            new(anterior, nueva, preciosCambiados, null);

        public static Desenlace Rechazado(ErrorValidacion error) => new(null, null, 0, error);
    }
}
