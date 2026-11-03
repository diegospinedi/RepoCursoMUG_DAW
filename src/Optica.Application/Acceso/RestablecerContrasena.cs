using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>Cómo terminó un restablecimiento que llegó a confirmarse.</summary>
public enum ResultadoRestablecimiento
{
    /// <summary>Credencial eliminada (si había), evento registrado y claves de sesión renovadas.</summary>
    Restablecida,

    /// <summary>
    /// La credencial se eliminó y el evento quedó registrado, pero no se pudieron vaciar las claves de sesión: las
    /// cookies anteriores podrían seguir valiendo. Hay que repetir el restablecimiento.
    /// </summary>
    ClavesSinRenovar,
}

/// <summary>
/// El resultado de un restablecimiento: cómo terminó, si había una contraseña definida y, si no se pudieron renovar las
/// claves, el tipo de la falla (solo el tipo: su texto puede traer la ruta con el usuario de Windows).
/// </summary>
public sealed record IntentoRestablecimiento(ResultadoRestablecimiento Tipo, bool HabiaCredencial, string? TipoError);

/// <summary>
/// Restablece la contraseña (FR-04): si hay credencial la elimina y siempre registra <c>restablecimiento/exito</c>, los
/// dos en una sola confirmación, sin tocar ningún otro dato; el evento reinicia la cuenta del bloqueo
/// (<see cref="PoliticaBloqueo.EventosQueReinician"/>). Después renueva las claves de sesión
/// (<see cref="IDatosLocales.RenovarClaves"/>), así ninguna cookie anterior sigue valiendo. Repetirlo no hace daño:
/// registra otro evento y vuelve a renovar. Si la confirmación falla, la excepción se propaga sin haber tocado nada (ni
/// las claves); un conflicto llega como <see cref="ConflictoAlConfirmarException"/>, sin causa. Solo lo usa el comando
/// de consola de la PC (FR-05): no hay endpoint que lo invoque, y solo vale con la Api detenida (guarda las claves en
/// memoria).
/// </summary>
public sealed class RestablecerContrasena(
    ICredencialAccesoRepository credenciales,
    IRegistroEventosAcceso eventos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IDatosLocales datos,
    TimeProvider reloj)
{
    public async Task<IntentoRestablecimiento> EjecutarAsync(CancellationToken cancelacion = default)
    {
        var habiaCredencial = await credenciales.ExisteAsync(cancelacion);
        if (habiaCredencial)
        {
            credenciales.Eliminar();
        }

        eventos.Registrar(new EventoAcceso(
            TipoEventoAcceso.Restablecimiento, ResultadoEventoAcceso.Exito, reloj.GetUtcNow().UtcDateTime));
        await unidadDeTrabajo.ConfirmarAsync(cancelacion);

        try
        {
            datos.RenovarClaves();
        }
        catch (Exception error)
        {
            // Toda falla de acá ocurre después de confirmar: no puede llegar como "no se cambió nada".
            return new IntentoRestablecimiento(ResultadoRestablecimiento.ClavesSinRenovar, habiaCredencial, error.GetType().Name);
        }

        return new IntentoRestablecimiento(ResultadoRestablecimiento.Restablecida, habiaCredencial, null);
    }
}
