using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// El autorizador de homologación o producción que no envía nada: cada emisión termina "no enviada" con un motivo fijo,
/// sin llamar a nadie, y la Api arranca igual. Se usa en dos casos: faltan el CUIT, el certificado o su clave
/// (<see cref="MotivoNoConfigurada"/>), y, de forma temporal, con las credenciales completas
/// (<see cref="MotivoNoDisponible"/>) hasta que Block 6 de la spec FEAT-007 registre el cliente de WSAA y WSFEv1 en su
/// lugar.
/// </summary>
public sealed class AutorizadorNoConfigurado : IAutorizadorComprobantes
{
    /// <summary>El motivo cuando faltan credenciales.</summary>
    public const string MotivoNoConfigurada = "La facturación con ARCA no está configurada.";

    /// <summary>El motivo con credenciales completas, temporal hasta Block 6.</summary>
    public const string MotivoNoDisponible = "La facturación con ARCA todavía no está disponible en esta versión.";

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

    public Task<ResultadoAutorizacion> AutorizarAsync(SolicitudComprobante solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return Task.FromResult<ResultadoAutorizacion>(new ResultadoAutorizacion.NoEnviada(_motivo));
    }
}
