using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Facturacion;

/// <summary>
/// Descarta la emisión pendiente de un presupuesto después del aviso de importe distinto, sin número u otro entorno
/// (FR-09 a FR-11, ADR-026). En una sola transacción de escritura (<c>BEGIN IMMEDIATE</c>):
/// <list type="number">
/// <item>Lee el presupuesto (no encontrado), su pendiente (<see cref="ResultadoDescarte.SinPendiente"/>) y su
/// observación vigente.</item>
/// <item>Vuelve a comprobar el motivo contra el estado actual (D-12): sin número, si la pendiente sigue sin número; otro
/// entorno, si el entorno de la pendiente sigue siendo distinto del configurado; importe distinto, si la observación es
/// del número que tiene la pendiente. Sin observación, o con un motivo que ya no se sostiene,
/// <see cref="ResultadoDescarte.SinObservacion"/> sin tocar nada (FR-11, AC-10): venga de la pantalla o de un pedido
/// directo a la Api.</item>
/// <item>Graba el descarte (momento UTC, presupuesto, tipo, punto de venta, número, total registrado, total de ARCA,
/// motivo y entorno, FR-10), quita la observación y la pendiente (D-24) y confirma todo junto.</item>
/// </list>
/// No llama a ARCA ni toma el candado de emisión, y es seguro sin él por esta regla: toda pendiente nueva nace sin
/// observación (la emisión normal la graba sola, y la de FR-06 quita la observación de la vieja en la misma transacción
/// que graba la pendiente nueva, D-11, D-24). Así, una observación vigente siempre es de la pendiente que hoy está
/// guardada, y la transacción del descarte (<c>BEGIN IMMEDIATE</c>) lee las dos juntas: no puede borrar una pendiente nueva
/// con la observación de una vieja. Si el descarte gana la carrera a una verificación que va a reenviar (FR-06), lo que
/// impide el envío es el modo reemplazo de <see cref="ProcesoEmision"/>: no encuentra la pendiente verificada y termina
/// "no enviada" sin enviar. El log no lleva datos del cliente.
/// </summary>
public sealed class DescartarEmision(
    IPresupuestoRepository presupuestos,
    IEmisionPendienteRepository pendientes,
    IObservacionEmisionRepository observaciones,
    IDescarteEmisionRepository descartes,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAutorizadorComprobantes autorizador,
    TimeProvider reloj,
    ILogger<DescartarEmision> registro)
{
    public async Task<ResultadoDescarte> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        if (numero < 1)
        {
            return ResultadoDescarte.NoEncontrado;
        }

        var resultado = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => DescartarAsync(numero, operacion), cancelacion);
        if (resultado == ResultadoDescarte.Descartada)
        {
            registro.LogInformation(
                "Se descartó la emisión pendiente del presupuesto {Presupuesto}.", numero);
        }

        return resultado;
    }

    private async Task<ResultadoDescarte> DescartarAsync(long numero, CancellationToken operacion)
    {
        if (await presupuestos.ObtenerAsync(numero, operacion) is null)
        {
            return ResultadoDescarte.NoEncontrado;
        }

        if (await pendientes.ObtenerPendienteAsync(numero, operacion) is not { } pendiente)
        {
            return ResultadoDescarte.SinPendiente;
        }

        if (await observaciones.ObtenerObservacionAsync(numero, operacion) is not { } observacion
            || !SeSostiene(observacion, pendiente))
        {
            return ResultadoDescarte.SinObservacion;
        }

        descartes.Agregar(new DescarteEmision(
            reloj.GetUtcNow(), numero, pendiente.Tipo, pendiente.PuntoVenta, pendiente.Numero, pendiente.Total,
            observacion.TotalArca, observacion.Motivo, pendiente.Entorno));
        observaciones.QuitarObservacion(numero);
        pendientes.Quitar(numero);
        await unidadDeTrabajo.ConfirmarAsync(operacion);
        return ResultadoDescarte.Descartada;
    }

    /// <summary>El motivo de la observación sigue valiendo con la pendiente actual y el entorno configurado (D-12).</summary>
    private bool SeSostiene(ObservacionEmision observacion, EmisionPendiente pendiente) => observacion.Motivo switch
    {
        MotivoObservacionEmision.SinNumero => !pendiente.TieneNumero,
        MotivoObservacionEmision.OtroEntorno => pendiente.Entorno != autorizador.Entorno,
        MotivoObservacionEmision.ImporteDistinto =>
            pendiente.Numero is { } registrado && observacion.Numero == registrado,
        _ => false,
    };
}
