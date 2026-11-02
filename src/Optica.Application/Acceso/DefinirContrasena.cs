using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>
/// Define la contraseña si todavía no existe: guarda solo su hash y registra el evento <c>definicion</c>, los
/// dos en la misma confirmación, de modo que no queda una credencial sin evento ni al revés.
/// </summary>
public sealed class DefinirContrasena(
    ICredencialAccesoRepository credenciales,
    IRegistroEventosAcceso eventos,
    IHasherContrasena hasher,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj)
{
    public async Task<Resultado> EjecutarAsync(string? contrasena, CancellationToken cancelacion = default)
    {
        var error = ValidacionContrasena.Validar(contrasena, out var normalizada);
        if (error is not null)
        {
            return Resultado.Invalido(error);
        }

        // Si ya existe, conflicto sin calcular el hash ni tocar la credencial.
        if (await credenciales.ExisteAsync(cancelacion))
        {
            return Resultado.Conflicto();
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        // Se guarda el hash de la forma NFC, la misma que se evaluó en la política y que se verifica al ingresar.
        credenciales.Agregar(new CredencialAcceso(hasher.Calcular(normalizada), ahora));
        eventos.Registrar(new EventoAcceso(TipoEventoAcceso.Definicion, ResultadoEventoAcceso.Exito, ahora));

        try
        {
            await unidadDeTrabajo.ConfirmarAsync(cancelacion);
        }
        catch (ConflictoAlConfirmarException)
        {
            // Otra definición se grabó entre la consulta y la confirmación; no se guardó nada de esta.
            return Resultado.Conflicto();
        }

        return Resultado.Exito();
    }
}
