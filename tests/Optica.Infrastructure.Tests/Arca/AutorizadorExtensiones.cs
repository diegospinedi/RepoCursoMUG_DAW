using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>
/// El recorrido de una emisión por el puerto de ARCA (ADR-025): el próximo número y la autorización con ese número, con
/// el mismo token. Lo usan las pruebas que ejercitan el ticket de WSAA, el simulador o la composición sin importar en qué
/// paso se corta; "no enviada" del próximo número se devuelve como "no enviada" de la autorización, igual que en
/// <c>EmitirFactura</c>.
/// </summary>
internal static class AutorizadorExtensiones
{
    public static async Task<ResultadoAutorizacion> EmitirAsync(
        this IAutorizadorComprobantes autorizador, SolicitudComprobante solicitud, CancellationToken cancelacion)
    {
        var proximo = await autorizador.ProximoNumeroAsync(solicitud.Tipo, solicitud.PuntoVenta, cancelacion);
        return proximo switch
        {
            ResultadoProximoNumero.Numero numero => await autorizador.AutorizarAsync(solicitud, numero.Valor, cancelacion),
            ResultadoProximoNumero.NoEnviada noEnviada => new ResultadoAutorizacion.NoEnviada(noEnviada.Motivo),
            _ => throw new InvalidOperationException("Resultado del próximo número no previsto."),
        };
    }
}
