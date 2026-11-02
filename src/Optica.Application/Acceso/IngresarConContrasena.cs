using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>Resultado de un intento de ingreso.</summary>
public enum ResultadoIngreso
{
    Exito,
    SinCredencial,
    Rechazo,

    /// <summary>La contraseña no cumple la política; <see cref="IntentoIngreso.Errores"/> dice qué corregir.</summary>
    Invalido,
}

/// <summary>Cómo terminó un intento de ingreso y, si fue inválido, el campo y la acción que lo corrige.</summary>
public sealed record IntentoIngreso(ResultadoIngreso Tipo, IReadOnlyList<ErrorValidacion> Errores)
{
    public static IntentoIngreso Exito { get; } = new(ResultadoIngreso.Exito, []);

    public static IntentoIngreso SinCredencial { get; } = new(ResultadoIngreso.SinCredencial, []);

    public static IntentoIngreso Rechazo { get; } = new(ResultadoIngreso.Rechazo, []);

    public static IntentoIngreso Invalido(ErrorValidacion error) => new(ResultadoIngreso.Invalido, [error]);
}

/// <summary>
/// Verifica la contraseña contra la credencial guardada. Una contraseña fuera de la política devuelve
/// <see cref="ResultadoIngreso.Invalido"/> con el campo y la acción, sin consultar la base, sin calcular el hash y sin
/// registrar evento (decisión del usuario del 2026-10-31, ADR-005). Sin credencial devuelve
/// <see cref="ResultadoIngreso.SinCredencial"/> y no registra nada; si no, registra el evento <c>ingreso</c> con su
/// resultado.
/// </summary>
public sealed class IngresarConContrasena(
    ICredencialAccesoRepository credenciales,
    IRegistroEventosAcceso eventos,
    IHasherContrasena hasher,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj)
{
    public async Task<IntentoIngreso> EjecutarAsync(string? contrasena, CancellationToken cancelacion = default)
    {
        // Se verifica la forma NFC (normalizada una sola vez), la misma que se usó al definir.
        var error = ValidacionContrasena.Validar(contrasena, out var normalizada);
        if (error is not null)
        {
            return IntentoIngreso.Invalido(error);
        }

        var credencial = await credenciales.ObtenerAsync(cancelacion);
        if (credencial is null)
        {
            return IntentoIngreso.SinCredencial;
        }

        var coincide = hasher.Verificar(normalizada, credencial.Hash);
        eventos.Registrar(new EventoAcceso(
            TipoEventoAcceso.Ingreso,
            coincide ? ResultadoEventoAcceso.Exito : ResultadoEventoAcceso.Rechazo,
            reloj.GetUtcNow().UtcDateTime));
        await unidadDeTrabajo.ConfirmarAsync(cancelacion);

        return coincide ? IntentoIngreso.Exito : IntentoIngreso.Rechazo;
    }
}
