using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// ARCA simulada, el entorno por defecto (D-17, ADR-022): no sale del proceso. Numera por tipo y punto de venta con un
/// contador en <see cref="NombreArchivo"/> del directorio de datos (escritura atómica: archivo temporal y reemplazo),
/// devuelve un CAE ficticio de 14 dígitos con vencimiento a 10 días de la fecha del comprobante, y responde según
/// <c>Arca:Simulado:Respuesta</c>. Sus comprobantes no tienen validez fiscal: la factura guarda el entorno simulado y el
/// PDF lleva la leyenda (AGENTS.md). Singleton: un candado propio serializa la lectura y la escritura del contador.
/// No aplica un plazo propio: con <see cref="RespuestaSimulador.NoResponder"/> espera hasta que se cancela el token que
/// recibe (el plazo de <c>EmitirFactura</c>, D-32). Con un token que no se puede cancelar responde "no enviada" con
/// <see cref="MotivoSinPlazo"/>, sea cual sea la respuesta configurada, igual que el cliente real. Nunca registra la
/// solicitud ni el contenido del archivo.
/// </summary>
public sealed class SimuladorArca : IAutorizadorComprobantes
{
    public const string NombreArchivo = "arca-simulado.json";

    /// <summary>El código de rechazo de <see cref="RespuestaSimulador.Rechazar"/> (AC-20).</summary>
    public const int CodigoRechazo = 10016;

    /// <summary>La descripción fija del rechazo simulado.</summary>
    public const string DescripcionRechazo =
        "Rechazo simulado: el número o la fecha del comprobante no se corresponde con el próximo a autorizar.";

    /// <summary>Lo que se informa cuando el contador no se puede leer o grabar.</summary>
    public const string MotivoContador = "No se pudo usar el contador del simulador de ARCA.";

    /// <summary>
    /// Lo que se informa con un token que no se puede cancelar: sin plazo no se "envía" nada, igual que el cliente real.
    /// </summary>
    public const string MotivoSinPlazo = AutorizadorWsfe.MotivoSinPlazo;

    /// <summary>Lo que se informa cuando la emisión se cancela antes de empezar.</summary>
    public const string MotivoCancelada = "La emisión se canceló antes de enviar la solicitud.";

    /// <summary>Días entre la fecha del comprobante y el vencimiento del CAE simulado.</summary>
    public const int DiasVencimientoCae = 10;

    private const string PropiedadContadores = "contadores";

    // El contador es chico: un archivo más grande no es el del simulador.
    private const long TamanoMaximoArchivo = 64 * 1024;

    private readonly string _archivo;
    private readonly RespuestaSimulador _respuesta;
    private readonly ILogger<SimuladorArca> _registro;
    private readonly SemaphoreSlim _candado = new(1, 1);

    /// <param name="directorio">El directorio de datos (ya preparado por la inicialización de la base).</param>
    public SimuladorArca(
        string directorio, RespuestaSimulador respuesta, TimeSpan tiempoMaximo, ILogger<SimuladorArca> registro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directorio);
        ArgumentNullException.ThrowIfNull(registro);
        _archivo = Path.Combine(directorio, NombreArchivo);
        _respuesta = respuesta;
        TiempoMaximo = tiempoMaximo;
        _registro = registro;
    }

    public EntornoArca Entorno => EntornoArca.Simulado;

    public TimeSpan TiempoMaximo { get; }

    public async Task<ResultadoAutorizacion> AutorizarAsync(SolicitudComprobante solicitud, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        // Como el cliente real (ronda 3): sin plazo no se "envía" nada. "Sin respuesta" dejaría una emisión pendiente.
        if (!cancelacion.CanBeCanceled)
        {
            return new ResultadoAutorizacion.NoEnviada(MotivoSinPlazo);
        }

        // Como el cliente real: cancelado antes de "enviar", no se envió nada.
        if (cancelacion.IsCancellationRequested)
        {
            return new ResultadoAutorizacion.NoEnviada(MotivoCancelada);
        }

        switch (_respuesta)
        {
            case RespuestaSimulador.Rechazar:
                _registro.LogInformation(
                    "ARCA simulada rechazó la solicitud ({Tipo}, punto de venta {PuntoVenta}) con el código {Codigo}.",
                    solicitud.Tipo,
                    solicitud.PuntoVenta,
                    CodigoRechazo);
                return new ResultadoAutorizacion.Rechazada(CodigoRechazo, DescripcionRechazo);

            case RespuestaSimulador.NoResponder:
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancelacion);
                }
                catch (OperationCanceledException)
                {
                }

                return new ResultadoAutorizacion.SinRespuesta();

            default:
                return await AutorizarAsync(solicitud.Tipo, solicitud.PuntoVenta, solicitud.Fecha, cancelacion);
        }
    }

    private async Task<ResultadoAutorizacion> AutorizarAsync(
        TipoComprobante tipo, int puntoVenta, DateOnly fecha, CancellationToken cancelacion)
    {
        try
        {
            await _candado.WaitAsync(cancelacion);
        }
        catch (OperationCanceledException)
        {
            return new ResultadoAutorizacion.NoEnviada(MotivoCancelada);
        }

        try
        {
            Dictionary<string, long> contadores;
            try
            {
                contadores = Leer();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
                or FormatException)
            {
                // Solo el nombre del archivo y el tipo de la falla: el texto de la excepción puede traer el contenido o
                // la ruta con el usuario de la PC.
                _registro.LogError(
                    "ARCA simulada: no se pudo leer el contador {Archivo} del directorio de datos ({TipoFalla}). La " +
                    "emisión no se envió; revisá ese archivo o borralo para numerar desde 1.",
                    NombreArchivo,
                    error.GetType().Name);
                return new ResultadoAutorizacion.NoEnviada(MotivoContador);
            }

            var clave = Clave(tipo, puntoVenta);
            var numero = contadores.GetValueOrDefault(clave) + 1;
            if (numero > NumeroComprobante.NumeroMaximo)
            {
                _registro.LogError(
                    "ARCA simulada: el contador {Archivo} llegó al número máximo de comprobante para {Tipo}, punto de " +
                    "venta {PuntoVenta}.",
                    NombreArchivo,
                    tipo,
                    puntoVenta);
                return new ResultadoAutorizacion.NoEnviada(MotivoContador);
            }

            contadores[clave] = numero;
            try
            {
                Grabar(contadores);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _registro.LogError(
                    "ARCA simulada: no se pudo grabar el contador {Archivo} en el directorio de datos ({TipoFalla}). La " +
                    "emisión no se envió.",
                    NombreArchivo,
                    error.GetType().Name);
                return new ResultadoAutorizacion.NoEnviada(MotivoContador);
            }

            var cae = RandomNumberGenerator.GetString("0123456789", ResultadoAutorizacion.Autorizada.LargoCae);
            _registro.LogInformation(
                "ARCA simulada autorizó {Comprobante} (sin validez fiscal).",
                new NumeroComprobante(tipo, puntoVenta, numero).ToString());
            return new ResultadoAutorizacion.Autorizada(numero, cae, fecha.AddDays(DiasVencimientoCae));
        }
        finally
        {
            _candado.Release();
        }
    }

    private static string Clave(TipoComprobante tipo, int puntoVenta) =>
        $"{(tipo == TipoComprobante.FacturaB ? "B" : "C")}-{puntoVenta}";

    /// <summary>El contador guardado; vacío si el archivo todavía no existe.</summary>
    /// <exception cref="JsonException">No es JSON.</exception>
    /// <exception cref="FormatException">Es JSON pero no tiene la forma del contador.</exception>
    private Dictionary<string, long> Leer()
    {
        var contadores = new Dictionary<string, long>(StringComparer.Ordinal);
        if (!File.Exists(_archivo))
        {
            return contadores;
        }

        if (new FileInfo(_archivo).Length > TamanoMaximoArchivo)
        {
            throw new FormatException();
        }

        using var documento = JsonDocument.Parse(
            File.ReadAllBytes(_archivo), new JsonDocumentOptions { MaxDepth = 4 });
        var raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException();
        }

        if (!raiz.TryGetProperty(PropiedadContadores, out var elementos))
        {
            return contadores;
        }

        if (elementos.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException();
        }

        foreach (var elemento in elementos.EnumerateObject())
        {
            if (elemento.Value.ValueKind != JsonValueKind.Number
                || !elemento.Value.TryGetInt64(out var ultimo)
                || ultimo is < 0 or > NumeroComprobante.NumeroMaximo)
            {
                throw new FormatException();
            }

            contadores[elemento.Name] = ultimo;
        }

        return contadores;
    }

    /// <summary>Graba en un temporal del mismo directorio y lo mueve sobre el archivo: nunca queda a medio escribir.</summary>
    private void Grabar(Dictionary<string, long> contadores)
    {
        var contenido = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, SortedDictionary<string, long>>
            {
                [PropiedadContadores] = new(contadores, StringComparer.Ordinal),
            },
            new JsonSerializerOptions { WriteIndented = true });
        var temporal = $"{_archivo}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporal, contenido);
            File.Move(temporal, _archivo, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporal))
                {
                    File.Delete(temporal);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Un temporal que no se pudo borrar no cambia el resultado; la falla original, si la hubo, sigue.
            }
        }
    }
}
