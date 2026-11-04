using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>Cómo terminó un cambio de contraseña.</summary>
public enum ResultadoCambio
{
    Exito,

    /// <summary>
    /// Alguna contraseña no cumple la política, o la actual no coincide; <see cref="IntentoCambio.Errores"/> dice en qué
    /// campo y qué hacer.
    /// </summary>
    Invalido,

    /// <summary>No hay credencial (se restableció con una sesión abierta): no hay nada que cambiar.</summary>
    SinCredencial,

    /// <summary>La credencial cambió o se borró entre la lectura y la confirmación; no se guardó nada.</summary>
    Conflicto,
}

/// <summary>Cómo terminó un cambio de contraseña y, si fue inválido, los campos y las acciones que lo corrigen.</summary>
public sealed record IntentoCambio(ResultadoCambio Tipo, IReadOnlyList<ErrorValidacion> Errores)
{
    public static IntentoCambio Exito { get; } = new(ResultadoCambio.Exito, []);

    public static IntentoCambio SinCredencial { get; } = new(ResultadoCambio.SinCredencial, []);

    public static IntentoCambio Conflicto { get; } = new(ResultadoCambio.Conflicto, []);

    public static IntentoCambio Invalido(IReadOnlyList<ErrorValidacion> errores) => new(ResultadoCambio.Invalido, errores);
}

/// <summary>
/// Cambia la contraseña con la sesión abierta (FR-03). En este orden: valida la forma de las dos con la política
/// (<see cref="ValidacionContrasena"/>, NFC, 8 a 128), cada una en su campo y sin registrar evento; sin credencial
/// devuelve <see cref="ResultadoCambio.SinCredencial"/>; verifica la actual con el hasher y, si no coincide, registra
/// <c>cambio_contrasena/rechazo</c> (que no cuenta para el bloqueo de ingreso, decisión del usuario, R-04) y devuelve
/// el error en <see cref="ValidacionContrasena.CampoActual"/>; si coincide, reemplaza la credencial leída por otra con
/// un hash nuevo (sal nueva) y la fecha de ahora, y registra <c>cambio_contrasena/exito</c>, todo en una sola
/// confirmación. Si la credencial cambió en el medio, devuelve <see cref="ResultadoCambio.Conflicto"/> sin guardar
/// nada. El bloqueo de ingreso no impide cambiarla; las sesiones abiertas siguen válidas.
/// </summary>
public sealed class CambiarContrasena(
    ICredencialAccesoRepository credenciales,
    IRegistroEventosAcceso eventos,
    IHasherContrasena hasher,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj)
{
    /// <summary>Acción en <see cref="ValidacionContrasena.CampoActual"/> cuando la contraseña actual no coincide.</summary>
    public const string AccionActualIncorrecta = "La contraseña actual no es válida.";

    public async Task<IntentoCambio> EjecutarAsync(string? actual, string? nueva, CancellationToken cancelacion = default)
    {
        // Las dos se validan antes de tocar la base: un error de forma se informa en su campo, sin evento.
        var errorActual = ValidacionContrasena.Validar(actual, out var actualNormalizada, ValidacionContrasena.CampoActual);
        var errorNueva = ValidacionContrasena.Validar(nueva, out var nuevaNormalizada, ValidacionContrasena.CampoNueva);
        if (errorActual is not null || errorNueva is not null)
        {
            return IntentoCambio.Invalido([.. new[] { errorActual, errorNueva }.OfType<ErrorValidacion>()]);
        }

        // Tal como se leyó: su hash es el que se compara al confirmar (concurrencia optimista).
        var anterior = await credenciales.ObtenerAsync(cancelacion);
        if (anterior is null)
        {
            return IntentoCambio.SinCredencial;
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        if (!hasher.Verificar(actualNormalizada, anterior.Hash))
        {
            eventos.Registrar(new EventoAcceso(TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Rechazo, ahora));
            // Sin captura de ConflictoAlConfirmarException, igual que el rechazo de IngresarConContrasena: esta
            // confirmación solo inserta un evento con Id autoincremental y no toca la credencial, así que no hay fila
            // leída que otro pedido pueda haber cambiado y no puede dar conflicto.
            await unidadDeTrabajo.ConfirmarAsync(cancelacion);
            return IntentoCambio.Invalido([new ErrorValidacion(ValidacionContrasena.CampoActual, AccionActualIncorrecta)]);
        }

        // Otra instancia, nunca anterior modificada: así el UPDATE filtra por el hash leído.
        var reemplazo = new CredencialAcceso(hasher.Calcular(nuevaNormalizada), ahora);
        credenciales.Reemplazar(anterior, reemplazo);
        eventos.Registrar(new EventoAcceso(TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Exito, ahora));

        try
        {
            await unidadDeTrabajo.ConfirmarAsync(cancelacion);
        }
        catch (ConflictoAlConfirmarException)
        {
            // Otro cambio o un restablecimiento confirmó en el medio; no se guardó nada de este y no se registra.
            return IntentoCambio.Conflicto;
        }

        return IntentoCambio.Exito;
    }
}
