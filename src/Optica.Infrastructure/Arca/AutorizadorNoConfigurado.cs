using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// El autorizador de homologación o producción que no envía nada: cada operación termina "no enviada" o "no consultada"
/// con un motivo fijo, sin llamar a nadie, y la Api arranca igual. Se usa cuando faltan el CUIT, el certificado o su clave
/// (<see cref="MotivoNoConfigurada"/>); con las credenciales completas, la raíz de composición registra
/// <see cref="AutorizadorWsfe"/>.
/// </summary>
public sealed class AutorizadorNoConfigurado : IAutorizadorComprobantes
{
    /// <summary>El motivo cuando faltan credenciales.</summary>
    public const string MotivoNoConfigurada = "La facturación con ARCA no está configurada.";

    private readonly string _motivo;

    /// <param name="motivo">Un texto fijo, sin valores de configuración ni datos del cliente.</param>
    /// <exception cref="ArgumentException">
    /// El entorno es el simulado, que no necesita configuración, o el motivo está vacío.
    /// </exception>
    public AutorizadorNoConfigurado(EntornoArca entorno, TimeSpan tiempoMaximo, string motivo)
    {
        if (entorno is not (EntornoArca.Homologacion or EntornoArca.Produccion))
        {
            throw new ArgumentException("El entorno no es homologación ni producción.", nameof(entorno));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        Entorno = entorno;
        TiempoMaximo = tiempoMaximo;
        _motivo = motivo;
    }

    public EntornoArca Entorno { get; }

    public TimeSpan TiempoMaximo { get; }

    public Task<ResultadoProximoNumero> ProximoNumeroAsync(
        TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion)
    {
        _ = new NumeroComprobante(tipo, puntoVenta, NumeroComprobante.NumeroMinimo); // valida tipo y punto de venta
        return Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.NoEnviada(_motivo));
    }

    public Task<ResultadoAutorizacion> AutorizarAsync(
        SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        _ = new NumeroComprobante(solicitud.Tipo, solicitud.PuntoVenta, numero);
        return Task.FromResult<ResultadoAutorizacion>(new ResultadoAutorizacion.NoEnviada(_motivo));
    }

    public Task<ResultadoConsulta> ConsultarAsync(
        TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion)
    {
        _ = new NumeroComprobante(tipo, puntoVenta, numero);
        return Task.FromResult<ResultadoConsulta>(new ResultadoConsulta.NoConsultada(_motivo));
    }
}
