using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Backup;

namespace Optica.Application.Backup;

/// <summary>
/// El campo de la Api de la ubicación del backup (<c>POST /api/configuracion/backup</c>) y las acciones que corrigen cada
/// rechazo. Las acciones son texto fijo: nunca repiten la ruta recibida (D-12, R-05).
/// </summary>
public static class ValidacionUbicacionBackup
{
    public const string CampoRuta = "ruta";

    public const string AccionFaltante = "Ingresá la carpeta del backup.";
    public const string AccionCaracteres = "Escribí la carpeta del backup en una sola línea, sin caracteres especiales.";
    public const string AccionLarga = "La ruta de la carpeta del backup puede tener hasta 260 caracteres.";
    public const string AccionNoAbsoluta =
        "Ingresá la ruta completa de la carpeta, empezando por la unidad (por ejemplo, E:\\Backups).";
    public const string AccionNoLocal =
        "La carpeta no puede estar en una unidad o una ruta de red. Elegí un pendrive, un disco externo o una carpeta "
        + "sincronizada con la nube.";
    public const string AccionCarpetaDatos =
        "La carpeta no puede ser la de los datos del sistema, ni estar dentro de ella ni contenerla. Elegí otra.";
    public const string AccionInexistente =
        "La carpeta no existe: no se puede escribir en ella. Revisá que el pendrive o el disco estén conectados.";
    public const string AccionSinEscritura = "No se puede escribir en la carpeta elegida. Revisá sus permisos o elegí otra.";
    public const string AccionMismaUnidad =
        "La carpeta debe estar en otra unidad que la base de datos (un pendrive o un disco externo) o en una carpeta "
        + "sincronizada con la nube (OneDrive, Google Drive o Dropbox).";

    /// <summary>El campo y la acción de una infracción del dominio.</summary>
    /// <exception cref="UnreachableException">El dominio sumó una infracción y falta traducirla aquí.</exception>
    public static ErrorValidacion Traducir(InfraccionUbicacionBackup infraccion) => infraccion switch
    {
        InfraccionUbicacionBackup.RutaFaltante => new(CampoRuta, AccionFaltante),
        InfraccionUbicacionBackup.RutaConCaracteresInvalidos => new(CampoRuta, AccionCaracteres),
        InfraccionUbicacionBackup.RutaDemasiadoLarga => new(CampoRuta, AccionLarga),
        InfraccionUbicacionBackup.RutaNoAbsoluta => new(CampoRuta, AccionNoAbsoluta),
        _ => throw new UnreachableException("Infracción de la ubicación del backup sin acción asignada."),
    };
}

/// <summary>
/// Cómo terminó la grabación de la ubicación del backup: grabada, con la ruta tal como quedó, o rechazada, con un único
/// error en el campo <c>ruta</c>.
/// </summary>
public sealed class ResultadoGrabacionUbicacionBackup
{
    private ResultadoGrabacionUbicacionBackup(UbicacionBackupVigente? ubicacion, IReadOnlyList<ErrorValidacion> errores)
    {
        Ubicacion = ubicacion;
        Errores = errores;
    }

    /// <summary>La ubicación grabada; null si se rechazó.</summary>
    public UbicacionBackupVigente? Ubicacion { get; }

    /// <summary>Un error en el campo <c>ruta</c> si se rechazó; vacío si se grabó.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    [MemberNotNullWhen(true, nameof(Ubicacion))]
    public bool EsGrabado => Ubicacion is not null;

    /// <exception cref="ArgumentNullException"><paramref name="ubicacion"/> es null.</exception>
    public static ResultadoGrabacionUbicacionBackup Grabado(UbicacionBackupVigente ubicacion)
    {
        ArgumentNullException.ThrowIfNull(ubicacion);
        return new(ubicacion, []);
    }

    /// <exception cref="ArgumentNullException"><paramref name="error"/> es null.</exception>
    public static ResultadoGrabacionUbicacionBackup Rechazado(ErrorValidacion error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(null, [error]);
    }
}

/// <summary>
/// Graba la carpeta donde se guardan las copias de la base (FR-01 a FR-03 de FEAT-010). La última grabación gana.
/// </summary>
/// <remarks>
/// <para>
/// Antes de la transacción, y en este orden, rechaza con un error en el campo <c>ruta</c>: lo que incumple las reglas del
/// dominio (vacía, con caracteres de control, de más de 260 caracteres o relativa); una ruta UNC, de dispositivo o en una
/// unidad de red (D-17); las mismas reglas del dominio sobre la forma normalizada que devuelve el inspector, que es la que
/// se graba; la carpeta de datos, una dentro de ella o una que la contenga (D-19, regla del inspector); una carpeta inexistente
/// (FR-03); una de la misma unidad que la base que no está dentro de una carpeta de nube conocida (FR-02, AC-02, AC-17);
/// y una en la que no se puede escribir (FR-03, con la sonda del inspector, la última para no escribir en una carpeta que
/// igual se rechaza). Ninguna acción repite la ruta.
/// </para>
/// <para>
/// Si pasa, dentro de <see cref="IUnidadDeTrabajo.EjecutarEnTransaccionAsync"/> (ADR-013) la reemplaza y confirma. Después
/// del commit registra en el log, con la categoría <see cref="CategoriaRegistro"/>, la fecha y hora UTC y el tipo de
/// destino ("otra unidad" o "nube en la misma unidad"); nunca la ruta, que puede llevar el nombre de una persona (D-12,
/// R-05). Ante una falla al grabar, la transacción se revierte, la excepción se propaga y no se registra nada.
/// </para>
/// </remarks>
public sealed partial class GrabarUbicacionBackup(
    IUbicacionBackupRepository ubicaciones,
    IUnidadDeTrabajo unidadDeTrabajo,
    IInspectorUbicaciones inspector,
    IDatosLocales datosLocales,
    TimeProvider reloj,
    ILoggerFactory fabricaRegistros)
{
    /// <summary>
    /// La categoría del registro de cada grabación. Empieza con <c>Optica.Application.Configuracion</c>, la clave con que
    /// <c>appsettings.json</c> la envía también al Registro de eventos de Windows, como los demás cambios de la
    /// configuración: el cambio de ubicación queda auditado sin tocar la configuración del log.
    /// </summary>
    public const string CategoriaRegistro = "Optica.Application.Configuracion.UbicacionBackup";

    private const string DestinoOtraUnidad = "otra unidad";
    private const string DestinoNube = "nube en la misma unidad";

    private readonly ILogger _registro =
        (fabricaRegistros ?? throw new ArgumentNullException(nameof(fabricaRegistros))).CreateLogger(CategoriaRegistro);

    public async Task<ResultadoGrabacionUbicacionBackup> EjecutarAsync(
        string? ruta, CancellationToken cancelacion = default)
    {
        var validacion = UbicacionBackup.ParaGrabar(ruta);
        if (!validacion.EsValido)
        {
            return ResultadoGrabacionUbicacionBackup.Rechazado(
                ValidacionUbicacionBackup.Traducir(validacion.Infracciones[0]));
        }

        // UNC, dispositivo o red se juzga sobre la ruta tal como llegó (recortada): la normalización no debe ocultarlo.
        var escrita = validacion.Ubicacion.Ruta;
        if (!inspector.EsLocal(escrita))
        {
            return Rechazo(ValidacionUbicacionBackup.AccionNoLocal);
        }

        // Se graba la forma normalizada (sin "..", sin separador final), y las reglas del dominio, incluido el largo de 260,
        // se vuelven a validar sobre ella.
        if (inspector.Normalizar(escrita) is not { } normalizada)
        {
            return Rechazo(ValidacionUbicacionBackup.AccionNoAbsoluta);
        }

        var revalidacion = UbicacionBackup.ParaGrabar(normalizada);
        if (!revalidacion.EsValido)
        {
            return ResultadoGrabacionUbicacionBackup.Rechazado(
                ValidacionUbicacionBackup.Traducir(revalidacion.Infracciones[0]));
        }

        var ubicacion = revalidacion.Ubicacion;
        var (rechazo, destino) = Evaluar(ubicacion.Ruta);
        if (rechazo is not null)
        {
            return Rechazo(rechazo);
        }

        await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            async token =>
            {
                ubicaciones.Reemplazar(ubicacion);
                await unidadDeTrabajo.ConfirmarAsync(token);
                return true;
            },
            cancelacion);

        // Después del commit: solo lo confirmado, y sin la ruta.
        RegistrarGrabacion(_registro, destino, reloj.GetUtcNow().UtcDateTime);
        return ResultadoGrabacionUbicacionBackup.Grabado(UbicacionBackupVigente.Desde(ubicacion));
    }

    private static ResultadoGrabacionUbicacionBackup Rechazo(string accion) =>
        ResultadoGrabacionUbicacionBackup.Rechazado(new ErrorValidacion(ValidacionUbicacionBackup.CampoRuta, accion));

    /// <summary>
    /// La acción del primer rechazo del sistema de archivos sobre la ruta normalizada, o null y el tipo de destino si la
    /// carpeta sirve.
    /// </summary>
    private (string? Rechazo, string Destino) Evaluar(string ruta)
    {
        var datos = datosLocales.Directorio;
        if (inspector.Superpuestas(ruta, datos))
        {
            return (ValidacionUbicacionBackup.AccionCarpetaDatos, string.Empty);
        }

        if (!inspector.Existe(ruta))
        {
            return (ValidacionUbicacionBackup.AccionInexistente, string.Empty);
        }

        var destino = DestinoOtraUnidad;
        if (inspector.MismaUnidad(ruta, datos))
        {
            if (!inspector.EnNubeConocida(ruta))
            {
                return (ValidacionUbicacionBackup.AccionMismaUnidad, string.Empty);
            }

            destino = DestinoNube;
        }

        return inspector.PuedeEscribir(ruta)
            ? (null, destino)
            : (ValidacionUbicacionBackup.AccionSinEscritura, string.Empty);
    }

    // La fecha y el tipo de destino: nunca la ruta (D-12, R-05).
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Ubicación del backup grabada. Destino: {Destino}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarGrabacion(ILogger registro, string destino, DateTime fechaHoraUtc);
}
