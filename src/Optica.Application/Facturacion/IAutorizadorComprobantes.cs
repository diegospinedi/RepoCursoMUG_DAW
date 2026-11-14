using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>
/// El acceso a ARCA (FR-17, D-11, ADR-022, ADR-025): el próximo número, la autorización con ese número y la consulta de
/// un comprobante por número. La implementación vive en <c>Optica.Infrastructure.Arca</c> y la
/// elige la raíz de composición según <c>Arca:Entorno</c>: el simulador (por defecto, también sin la clave) o el cliente
/// de WSAA y WSFEv1. La aplicación no conoce SOAP, certificados ni códigos de WSFEv1. Ninguna operación lanza por fallas
/// de ARCA ni de la red, y un token que no se puede cancelar (sin plazo) termina "no enviada" o "no consultada" sin tocar
/// la red.
/// </summary>
public interface IAutorizadorComprobantes
{
    /// <summary>El entorno de ARCA contra el que se autoriza; la factura lo guarda (FR-18).</summary>
    EntornoArca Entorno { get; }

    /// <summary>
    /// El plazo de toda la emisión (<c>Arca:TiempoMaximoSegundos</c>, 30 s por defecto, NFR-01). Lo aplica quien llama
    /// sobre la espera del candado y las llamadas (D-32, D-10); el autorizador no aplica un plazo propio.
    /// </summary>
    TimeSpan TiempoMaximo { get; }

    /// <summary>
    /// El próximo número del tipo en el punto de venta: el último autorizado + 1 (FR-01, AC-01). Es de solo lectura en
    /// ARCA: cualquier falla, o la cancelación, es <see cref="ResultadoProximoNumero.NoEnviada"/>.
    /// </summary>
    /// <exception cref="ArgumentException">El tipo no está definido o el punto de venta no está entre 1 y 99999.</exception>
    Task<ResultadoProximoNumero> ProximoNumeroAsync(
        TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion);

    /// <summary>
    /// Pide la autorización de <paramref name="solicitud"/> con el número <paramref name="numero"/> (el que se registró
    /// en la emisión pendiente; no se vuelve a pedir el último). Clasifica las fallas en el resultado (D-16): la
    /// cancelación de <paramref name="cancelacion"/> antes de enviar el pedido de autorización es
    /// <see cref="ResultadoAutorizacion.NoEnviada"/>, y después es <see cref="ResultadoAutorizacion.SinRespuesta"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="solicitud"/> es null.</exception>
    /// <exception cref="ArgumentException"><paramref name="numero"/> no está entre 1 y 99.999.999.</exception>
    Task<ResultadoAutorizacion> AutorizarAsync(
        SolicitudComprobante solicitud, long numero, CancellationToken cancelacion);

    /// <summary>
    /// Consulta en ARCA el comprobante del tipo, punto de venta y número (FR-04, FR-05): autorizado con su total, CAE,
    /// vencimiento y fecha; no autorizado si ARCA no lo tiene; sin respuesta si se envió y no hubo respuesta legible
    /// (falla de la red, cancelación, otro error de ARCA); no consultada si falló antes de enviar. Nunca devuelve el
    /// documento del receptor (D-14).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El tipo no está definido, el punto de venta no está entre 1 y 99999 o el número no está entre 1 y 99.999.999.
    /// </exception>
    Task<ResultadoConsulta> ConsultarAsync(
        TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion);
}
