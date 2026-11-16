using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Application.Configuracion;

/// <summary>
/// Los datos del emisor tal como llegan de la operadora (FEAT-009): los cinco textos, el CUIT con o sin guiones y la
/// fecha de inicio como <c>yyyy-MM-dd</c>. Un dato ausente es null.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>DatosEmisorRecibidos { }</c>): la razón social y el CUIT
/// pueden ser de una persona física (R-06).
/// </remarks>
public sealed record DatosEmisorRecibidos(
    string? RazonSocial, string? DomicilioComercial, string? Cuit, string? IngresosBrutos, string? InicioActividades)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Cómo terminó la grabación de los datos del emisor: grabados, con los cinco tal como quedaron (CUIT sin guiones), o
/// inválidos, con un error por campo.
/// </summary>
public sealed class ResultadoGrabacionDatosEmisor
{
    private ResultadoGrabacionDatosEmisor(DatosEmisorVigentes? datos, IReadOnlyList<ErrorValidacion> errores)
    {
        Datos = datos;
        Errores = errores;
    }

    /// <summary>Los datos grabados; null si la grabación fue inválida.</summary>
    public DatosEmisorVigentes? Datos { get; }

    /// <summary>Errores de validación, uno por campo como máximo; vacío si los datos se grabaron.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    [MemberNotNullWhen(true, nameof(Datos))]
    public bool EsGrabado => Datos is not null;

    /// <exception cref="ArgumentNullException"><paramref name="datos"/> es null.</exception>
    public static ResultadoGrabacionDatosEmisor Grabado(DatosEmisorVigentes datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        return new(datos, []);
    }

    /// <exception cref="ArgumentException">No hay errores.</exception>
    public static ResultadoGrabacionDatosEmisor Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Una grabación inválida necesita al menos un error.", nameof(errores));
        }

        return new(null, [.. errores]);
    }
}

/// <summary>
/// Graba los cinco datos del emisor juntos (FR-01 a FR-03 de FEAT-009, ADR-028). La última grabación gana.
/// </summary>
/// <remarks>
/// <para>
/// Valida con <see cref="DatosEmisor.ParaGrabar"/>, con la fecha local de hoy del reloj (D-19): si algún dato incumple las
/// reglas devuelve <see cref="ResultadoGrabacionDatosEmisor.Invalido"/> con un error por campo, sin leer ni grabar nada.
/// Si no, dentro de <see cref="IUnidadDeTrabajo.EjecutarEnTransaccionAsync"/> (ADR-013, como
/// <c>GrabarConfiguracionConRecalculo</c>): lee los datos vigentes, los reemplaza y confirma.
/// </para>
/// <para>
/// Después del commit, y solo si se grabó, registra en el log con la categoría <see cref="CategoriaRegistro"/> la fecha y
/// hora UTC y los NOMBRES de los campos que cambiaron respecto de los vigentes (los cinco, si no había datos). Nunca sus
/// valores, ni anteriores ni nuevos: la razón social y el CUIT pueden ser de una persona física (R-06). Ante cualquier
/// falla al leer o confirmar, la transacción se revierte, la excepción se propaga y no se registra la grabación.
/// </para>
/// </remarks>
public sealed partial class GrabarDatosEmisor(
    IDatosEmisorRepository datosEmisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj,
    ILoggerFactory fabricaRegistros)
{
    /// <summary>
    /// La categoría del registro de cada grabación. Empieza con <c>Optica.Application.Configuracion</c>, la clave con que
    /// <c>appsettings.json</c> la envía también al Registro de eventos de Windows, como la de la configuración de negocio.
    /// </summary>
    public const string CategoriaRegistro = "Optica.Application.Configuracion.DatosEmisor";

    private readonly ILogger _registro =
        (fabricaRegistros ?? throw new ArgumentNullException(nameof(fabricaRegistros))).CreateLogger(CategoriaRegistro);

    public async Task<ResultadoGrabacionDatosEmisor> EjecutarAsync(
        DatosEmisorRecibidos datos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var hoy = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
        var validacion = DatosEmisor.ParaGrabar(
            datos.RazonSocial, datos.DomicilioComercial, datos.Cuit, datos.IngresosBrutos, datos.InicioActividades, hoy);
        if (!validacion.EsValido)
        {
            return ResultadoGrabacionDatosEmisor.Invalido([.. validacion.Infracciones.Select(ValidacionConfiguracion.Traducir)]);
        }

        var nuevos = validacion.Datos;
        var anteriores = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            async token =>
            {
                var vigentes = await datosEmisor.ObtenerAsync(token);
                datosEmisor.Reemplazar(nuevos);
                await unidadDeTrabajo.ConfirmarAsync(token);
                return vigentes;
            },
            cancelacion);

        // Después del commit: se registra solo lo confirmado, y solo los nombres de los campos.
        RegistrarGrabacion(_registro, CamposCambiados(anteriores, nuevos), reloj.GetUtcNow().UtcDateTime);
        return ResultadoGrabacionDatosEmisor.Grabado(DatosEmisorVigentes.Desde(nuevos));
    }

    /// <summary>
    /// Los nombres de la Api de los campos que cambian, en el orden del contrato, separados por coma; "ninguno" si se
    /// grabó lo mismo que estaba.
    /// </summary>
    private static string CamposCambiados(DatosEmisor? anteriores, DatosEmisor nuevos)
    {
        var campos = new List<string>(5);
        if (anteriores is null || !string.Equals(anteriores.RazonSocial, nuevos.RazonSocial, StringComparison.Ordinal))
        {
            campos.Add(ValidacionConfiguracion.CampoRazonSocial);
        }

        if (anteriores is null
            || !string.Equals(anteriores.DomicilioComercial, nuevos.DomicilioComercial, StringComparison.Ordinal))
        {
            campos.Add(ValidacionConfiguracion.CampoDomicilioComercial);
        }

        if (anteriores is null || !anteriores.Cuit.Equals(nuevos.Cuit))
        {
            campos.Add(ValidacionConfiguracion.CampoCuit);
        }

        if (anteriores is null || !string.Equals(anteriores.IngresosBrutos, nuevos.IngresosBrutos, StringComparison.Ordinal))
        {
            campos.Add(ValidacionConfiguracion.CampoIngresosBrutos);
        }

        if (anteriores is null || anteriores.InicioActividades != nuevos.InicioActividades)
        {
            campos.Add(ValidacionConfiguracion.CampoInicioActividades);
        }

        return campos.Count == 0 ? "ninguno" : string.Join(", ", campos);
    }

    // Solo los nombres de los campos y la fecha: nunca los valores (R-06).
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Datos del emisor grabados. Campos cambiados: {CamposCambiados}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarGrabacion(ILogger registro, string camposCambiados, DateTime fechaHoraUtc);
}
