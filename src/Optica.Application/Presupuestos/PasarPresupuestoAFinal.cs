using Optica.Application.Comun;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>
/// Pasa un presupuesto en Borrador a Final, de forma irreversible (FR-03, FR-06 de FEAT-003c). Antes de leer la base
/// valida el <c>estado</c> pedido y la versión (<see cref="ValidacionPresupuesto.ValidarPasoAFinal"/>): con algún error,
/// inválido sin leer nada. Si no, dentro de una operación en transacción (ADR-013): lee el presupuesto (inexistente, no
/// encontrado; en Final, final, también si se pidió "borrador", AC-08); con "borrador" sobre un Borrador, inválido en
/// <c>estado</c> (D-10); con otra versión, cambiado (D-01). Si no, aplica <see cref="Presupuesto.PasarAFinal"/>, registra
/// el cambio de estado con la versión enviada (<see cref="IPresupuestoRepository.CambiarEstado"/>: solo el estado y la
/// versión, D-10) y confirma. Grabado devuelve el presupuesto releído después de la operación, con su número, fecha y
/// líneas sin cambios.
/// </summary>
/// <remarks>
/// Un <see cref="ConflictoAlConfirmarException"/> se filtra por motivo: <see cref="MotivoConflicto.Concurrencia"/> relee
/// y decide entre no encontrado, final y cambiado; <see cref="MotivoConflicto.RegistroCerrado"/> es final; cualquier otro
/// motivo, y cualquier otra falla al confirmar, se propaga sin atenderla ni envolverla. No recibe un registrador.
/// </remarks>
public sealed class PasarPresupuestoAFinal(IPresupuestoRepository presupuestos, IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<ResultadoEdicionPresupuesto> EjecutarAsync(
        long numero, string? estado, long? version, CancellationToken cancelacion = default)
    {
        var errores = ValidacionPresupuesto.ValidarPasoAFinal(estado, version);
        if (errores.Count > 0)
        {
            return ResultadoEdicionPresupuesto.Invalido(errores);
        }

        var versionEnviada = version!.Value;
        var pideBorrador = estado == PresupuestoGrabado.EstadoBorrador;

        // ADR-013: la lectura, la verificación de la versión, el registro, la confirmación y la atención de un conflicto,
        // en una sola transacción de escritura. Null: el paso a Final se confirmó.
        var rechazo = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => PasarEnTransaccionAsync(numero, versionEnviada, pideBorrador, operacion), cancelacion);
        if (rechazo is not null)
        {
            return rechazo;
        }

        // Después de la operación. Sin cancelación a propósito: el paso a Final ya quedó confirmado.
        var grabado = await presupuestos.ObtenerAsync(numero, CancellationToken.None)
            ?? throw new InvalidOperationException("El presupuesto confirmado no se encontró por su número: estado corrupto.");
        return ResultadoEdicionPresupuesto.Grabado(PresupuestoGrabado.Desde(grabado));
    }

    private async Task<ResultadoEdicionPresupuesto?> PasarEnTransaccionAsync(
        long numero, long versionEnviada, bool pideBorrador, CancellationToken operacion)
    {
        var leido = await presupuestos.ObtenerAsync(numero, operacion);
        if (leido is null)
        {
            return ResultadoEdicionPresupuesto.NoEncontrado;
        }

        if (leido.Estado == EstadoPresupuesto.Final)
        {
            // AC-08 y FR-06: un Final no vuelve a Borrador ni pasa de nuevo a Final.
            return ResultadoEdicionPresupuesto.Final;
        }

        if (pideBorrador)
        {
            // D-10: un Borrador ya está en Borrador; pedirlo no es un conflicto sino un estado que no se puede pedir.
            return ResultadoEdicionPresupuesto.Invalido([ValidacionPresupuesto.EstadoInvalido]);
        }

        if (leido.Version != versionEnviada)
        {
            // D-01: no se pasa a Final un contenido que la operadora no vio.
            return ResultadoEdicionPresupuesto.Cambiado;
        }

        presupuestos.CambiarEstado(leido.PasarAFinal(), versionEnviada);

        try
        {
            await unidadDeTrabajo.ConfirmarAsync(operacion);
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.Concurrencia)
        {
            var releido = await presupuestos.ObtenerAsync(numero, operacion);
            return releido switch
            {
                null => ResultadoEdicionPresupuesto.NoEncontrado,
                { Estado: EstadoPresupuesto.Final } => ResultadoEdicionPresupuesto.Final,
                _ => ResultadoEdicionPresupuesto.Cambiado,
            };
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.RegistroCerrado)
        {
            return ResultadoEdicionPresupuesto.Final;
        }

        return null;
    }
}
