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

    /// <summary>El ingreso está bloqueado por intentos fallidos (<see cref="PoliticaBloqueo"/>), también en el quinto rechazo.</summary>
    Bloqueado,
}

/// <summary>Cómo terminó un intento de ingreso y, si fue inválido, el campo y la acción que lo corrige.</summary>
public sealed record IntentoIngreso(ResultadoIngreso Tipo, IReadOnlyList<ErrorValidacion> Errores)
{
    public static IntentoIngreso Exito { get; } = new(ResultadoIngreso.Exito, []);

    public static IntentoIngreso SinCredencial { get; } = new(ResultadoIngreso.SinCredencial, []);

    public static IntentoIngreso Rechazo { get; } = new(ResultadoIngreso.Rechazo, []);

    public static IntentoIngreso Bloqueado { get; } = new(ResultadoIngreso.Bloqueado, []);

    public static IntentoIngreso Invalido(ErrorValidacion error) => new(ResultadoIngreso.Invalido, [error]);
}

/// <summary>
/// Verifica la contraseña contra la credencial guardada. Una contraseña fuera de la política devuelve
/// <see cref="ResultadoIngreso.Invalido"/> con el campo y la acción, sin consultar la base, sin calcular el hash y sin
/// registrar evento (decisión del usuario del 2026-10-31, ADR-005). Sin credencial devuelve
/// <see cref="ResultadoIngreso.SinCredencial"/> y no registra nada. Con un bloqueo vigente (<see cref="PoliticaBloqueo"/>,
/// ADR-006) registra <c>ingreso/bloqueado</c>, no calcula el hash y devuelve <see cref="ResultadoIngreso.Bloqueado"/>; si
/// no, verifica el hash y registra <c>ingreso/exito</c> o <c>ingreso/rechazo</c>. Un rechazo que completa el bloqueo (el
/// quinto) devuelve <see cref="ResultadoIngreso.Bloqueado"/> en ese mismo intento: después de confirmar el rechazo se
/// relee lo confirmado y se evalúa la política. Una sola confirmación por pedido.
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

        // La hora se toma después de la consulta: un rechazo que otro pedido confirmó justo antes no queda posterior a
        // ahora (si quedara, la política no lo tomaría como ancla y se perdería el bloqueo).
        var rechazosLeidos = await eventos.ObtenerRechazosIngresoDesdeUltimoReinicioAsync(cancelacion);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        if (PoliticaBloqueo.Evaluar(rechazosLeidos, ahora).EstaBloqueado)
        {
            // Durante el bloqueo no se calcula el hash, ni siquiera con la contraseña correcta.
            return await RegistrarAsync(ResultadoEventoAcceso.Bloqueado, ahora, IntentoIngreso.Bloqueado, cancelacion);
        }

        if (hasher.Verificar(normalizada, credencial.Hash))
        {
            return await RegistrarAsync(ResultadoEventoAcceso.Exito, ahora, IntentoIngreso.Exito, cancelacion);
        }

        // Se confirma el rechazo (con la hora original) y después se decide con dos evaluaciones; basta que una bloquee:
        // - lo confirmado, releído, con la hora tomada después de releer: bloquea aunque lo leído al empezar no alcance,
        //   si otro pedido que leyó lo mismo confirmó antes de esta relectura y entre los dos se llegó al quinto;
        // - lo leído más el rechazo propio, con la hora original: bloquea al que confirma después aunque el otro haya
        //   dejado el quinto (el ancla) con una hora posterior a la suya, que para la primera evaluación queda en el
        //   futuro si el reloj no avanzó.
        await RegistrarAsync(ResultadoEventoAcceso.Rechazo, ahora, IntentoIngreso.Rechazo, cancelacion);
        var confirmados = await eventos.ObtenerRechazosIngresoDesdeUltimoReinicioAsync(cancelacion);
        var ahoraNuevo = reloj.GetUtcNow().UtcDateTime;
        var bloqueado = PoliticaBloqueo.Evaluar(confirmados, ahoraNuevo).EstaBloqueado
            || PoliticaBloqueo.Evaluar([.. rechazosLeidos, ahora], ahora).EstaBloqueado;
        return bloqueado ? IntentoIngreso.Bloqueado : IntentoIngreso.Rechazo;
    }

    /// <summary>Registra el evento <c>ingreso</c> y lo confirma en la única unidad de trabajo del pedido.</summary>
    private async Task<IntentoIngreso> RegistrarAsync(
        ResultadoEventoAcceso resultado, DateTime ahora, IntentoIngreso intento, CancellationToken cancelacion)
    {
        eventos.Registrar(new EventoAcceso(TipoEventoAcceso.Ingreso, resultado, ahora));
        await unidadDeTrabajo.ConfirmarAsync(cancelacion);
        return intento;
    }
}
