using Optica.Application.Comun;
using Optica.Domain.Acceso;

namespace Optica.Application.Acceso;

/// <summary>Registra el evento <c>salida</c>.</summary>
public sealed class CerrarSesion(
    IRegistroEventosAcceso eventos,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj)
{
    public async Task EjecutarAsync(CancellationToken cancelacion = default)
    {
        eventos.Registrar(new EventoAcceso(
            TipoEventoAcceso.Salida, ResultadoEventoAcceso.Exito, reloj.GetUtcNow().UtcDateTime));
        await unidadDeTrabajo.ConfirmarAsync(cancelacion);
    }
}
