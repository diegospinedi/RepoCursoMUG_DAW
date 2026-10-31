using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>Resultado de un intento de ingreso.</summary>
public enum ResultadoIngreso
{
    Exito,
    SinCredencial,
    Rechazo,
}

/// <summary>
/// Verifica la contraseña contra la credencial guardada. Sin credencial devuelve
/// <see cref="ResultadoIngreso.SinCredencial"/> y no registra nada; si no, registra el evento <c>ingreso</c>
/// con su resultado.
/// </summary>
public sealed class IngresarConContrasena(
    ICredencialAccesoRepository credenciales,
    IRegistroEventosAcceso eventos,
    IHasherContrasena hasher,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj)
{
    public async Task<ResultadoIngreso> EjecutarAsync(string? contrasena, CancellationToken cancelacion = default)
    {
        var credencial = await credenciales.ObtenerAsync(cancelacion);
        if (credencial is null)
        {
            return ResultadoIngreso.SinCredencial;
        }

        // Una contraseña fuera de la política no puede coincidir con la guardada: se rechaza sin calcular el hash.
        // Se verifica la forma NFC (normalizada una sola vez), la misma que se usó al definir.
        var coincide = PoliticaContrasena.Evaluar(contrasena, out var normalizada) == InfraccionContrasena.Ninguna
            && hasher.Verificar(normalizada, credencial.Hash);
        var resultado = coincide ? ResultadoIngreso.Exito : ResultadoIngreso.Rechazo;

        eventos.Registrar(new EventoAcceso(
            TipoEventoAcceso.Ingreso,
            coincide ? ResultadoEventoAcceso.Exito : ResultadoEventoAcceso.Rechazo,
            reloj.GetUtcNow().UtcDateTime));
        await unidadDeTrabajo.ConfirmarAsync(cancelacion);

        return resultado;
    }
}
