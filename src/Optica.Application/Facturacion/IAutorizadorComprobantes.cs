using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>
/// El acceso a ARCA para autorizar un comprobante (FR-17, D-11, ADR-022). La implementación vive en
/// <c>Optica.Infrastructure.Arca</c> y la elige la raíz de composición según <c>Arca:Entorno</c>: el simulador (por
/// defecto, también sin la clave) o el cliente de WSAA y WSFEv1. La aplicación no conoce SOAP, certificados ni códigos de
/// WSFEv1.
/// </summary>
public interface IAutorizadorComprobantes
{
    /// <summary>El entorno de ARCA contra el que se autoriza; la factura lo guarda (FR-18).</summary>
    EntornoArca Entorno { get; }

    /// <summary>
    /// El plazo de toda la emisión (<c>Arca:TiempoMaximoSegundos</c>, 30 s por defecto, NFR-01). Lo aplica
    /// <c>EmitirFactura</c> sobre la espera del candado y la llamada (D-32); el autorizador no aplica un plazo propio.
    /// </summary>
    TimeSpan TiempoMaximo { get; }

    /// <summary>
    /// Pide la autorización de <paramref name="solicitud"/>. No lanza por fallas de ARCA ni de la red: las clasifica en
    /// el resultado (D-16). La cancelación de <paramref name="cancelacion"/> antes de enviar el pedido de autorización es
    /// <see cref="ResultadoAutorizacion.NoEnviada"/>, y después es <see cref="ResultadoAutorizacion.SinRespuesta"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="solicitud"/> es null.</exception>
    Task<ResultadoAutorizacion> AutorizarAsync(SolicitudComprobante solicitud, CancellationToken cancelacion);
}
