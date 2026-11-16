using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>
/// ARCA simulada, el entorno por defecto (D-17, ADR-022): no sale del proceso. Guarda en <see cref="NombreArchivo"/> del
/// directorio de datos (escritura atómica: archivo temporal y reemplazo), por tipo y punto de venta, el último número
/// autorizado y los últimos <see cref="ComprobantesPorClave"/> comprobantes autorizados (número, total en centavos, CAE,
/// vencimiento y fecha; nunca datos del receptor, D-13). Autoriza solo el número último + 1 (si no, rechazo 10016, como
/// ARCA), devuelve un CAE ficticio de 14 dígitos con vencimiento a 10 días de la fecha del comprobante y responde según
/// <c>Arca:Simulado:Respuesta</c>; la consulta por número responde según <c>Arca:Simulado:Consulta</c> (D-19). Sus
/// comprobantes no tienen validez fiscal: la factura guarda el entorno simulado y el PDF lleva la leyenda (AGENTS.md).
/// Singleton: un candado propio serializa la lectura y la escritura del archivo.
/// No aplica un plazo propio: las respuestas que no responden esperan hasta que se cancela el token que recibe (el plazo
/// de quien llama, D-32, D-10). Con un token que no se puede cancelar responde "no enviada" / "no consultada" con
/// <see cref="MotivoSinPlazo"/>, sea cual sea la respuesta configurada, igual que el cliente real. Nunca registra la
/// solicitud ni el contenido del archivo.
/// </summary>
public sealed class SimuladorArca : IAutorizadorComprobantes
{
    public const string NombreArchivo = "arca-simulado.json";

    /// <summary>El código de rechazo de <see cref="RespuestaSimulador.Rechazar"/> y de un número que no es el próximo.</summary>
    public const int CodigoRechazo = 10016;

    /// <summary>La descripción fija del rechazo simulado.</summary>
    public const string DescripcionRechazo =
        "Rechazo simulado: el número o la fecha del comprobante no se corresponde con el próximo a autorizar.";

    /// <summary>Lo que se informa cuando el archivo del simulador no se puede leer o grabar.</summary>
    public const string MotivoContador = "No se pudo usar el contador del simulador de ARCA.";

    /// <summary>
    /// Lo que se informa con un token que no se puede cancelar: sin plazo no se "envía" nada, igual que el cliente real.
    /// </summary>
    public const string MotivoSinPlazo = AutorizadorWsfe.MotivoSinPlazo;

    /// <summary>Lo que se informa cuando la emisión se cancela antes de empezar.</summary>
    public const string MotivoCancelada = "La emisión se canceló antes de enviar la solicitud.";

    /// <summary>Días entre la fecha del comprobante y el vencimiento del CAE simulado.</summary>
    public const int DiasVencimientoCae = 10;

    /// <summary>Cuántos comprobantes autorizados se conservan por tipo y punto de venta: los de número más alto (D-13).</summary>
    public const int ComprobantesPorClave = 50;

    /// <summary>El tamaño máximo del archivo, al leerlo y al grabarlo (D-13).</summary>
    public const long TamanoMaximoArchivo = 256 * 1024;

    private const string PropiedadContadores = "contadores";
    private const string PropiedadComprobantes = "comprobantes";
    private const string PropiedadNumero = "numero";
    private const string PropiedadTotal = "totalCentavos";
    private const string PropiedadCae = "cae";
    private const string PropiedadVencimiento = "vencimiento";
    private const string PropiedadFecha = "fecha";
    private const string FormatoFecha = "yyyy-MM-dd";
    private const int PuntoVentaMaximo = 99999;

    private static readonly string[] PropiedadesComprobante =
        [PropiedadNumero, PropiedadTotal, PropiedadCae, PropiedadVencimiento, PropiedadFecha];

    private readonly string _archivo;
    private readonly RespuestaSimulador _respuesta;
    private readonly ConsultaSimulador _consulta;
    private readonly ILogger<SimuladorArca> _registro;
    private readonly SemaphoreSlim _candado = new(1, 1);

    // no_responder_una_vez: 0 hasta que llega la primera solicitud de autorización del proceso.
    private int _primeraSolicitudAtendida;

    /// <param name="directorio">El directorio de datos (ya preparado por la inicialización de la base).</param>
    public SimuladorArca(
        string directorio,
        RespuestaSimulador respuesta,
        ConsultaSimulador consulta,
        TimeSpan tiempoMaximo,
        ILogger<SimuladorArca> registro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directorio);
        ArgumentNullException.ThrowIfNull(registro);
        _archivo = Path.Combine(directorio, NombreArchivo);
        _respuesta = respuesta;
        _consulta = consulta;
        TiempoMaximo = tiempoMaximo;
        _registro = registro;
    }

    public EntornoArca Entorno => EntornoArca.Simulado;

    public TimeSpan TiempoMaximo { get; }

    /// <summary>
    /// El último número del tipo y punto de venta + 1, sin grabar nada. Sin plazo, cancelado o con el archivo ilegible,
    /// "no enviada".
    /// </summary>
    public async Task<ResultadoProximoNumero> ProximoNumeroAsync(
        TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion)
    {
        _ = new NumeroComprobante(tipo, puntoVenta, NumeroComprobante.NumeroMinimo); // valida tipo y punto de venta
        if (!cancelacion.CanBeCanceled)
        {
            return new ResultadoProximoNumero.NoEnviada(MotivoSinPlazo);
        }

        try
        {
            await _candado.WaitAsync(cancelacion);
        }
        catch (OperationCanceledException)
        {
            return new ResultadoProximoNumero.NoEnviada(MotivoCancelada);
        }

        try
        {
            if (!IntentarLeer(out var estado))
            {
                return new ResultadoProximoNumero.NoEnviada(MotivoContador);
            }

            var numero = estado.Contador(Clave(tipo, puntoVenta)) + 1;
            if (numero > NumeroComprobante.NumeroMaximo)
            {
                RegistrarNumeroMaximo(tipo, puntoVenta);
                return new ResultadoProximoNumero.NoEnviada(MotivoContador);
            }

            return new ResultadoProximoNumero.Numero(numero);
        }
        finally
        {
            _candado.Release();
        }
    }

    /// <summary>
    /// Autoriza <paramref name="numero"/> si es el último + 1 del tipo y punto de venta (si no, rechazo 10016), lo registra
    /// en el archivo y responde según <c>Arca:Simulado:Respuesta</c>: <c>autorizar</c> devuelve el CAE;
    /// <c>autorizar_sin_responder</c> registra y espera la cancelación ("sin respuesta"); <c>rechazar</c> rechaza sin
    /// registrar; <c>no_responder</c> espera la cancelación sin registrar; <c>no_responder_una_vez</c> hace eso con la
    /// primera solicitud del proceso y autoriza las siguientes.
    /// </summary>
    public async Task<ResultadoAutorizacion> AutorizarAsync(
        SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        _ = new NumeroComprobante(solicitud.Tipo, solicitud.PuntoVenta, numero);

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
                await EsperarCancelacionAsync(cancelacion);
                return new ResultadoAutorizacion.SinRespuesta();

            case RespuestaSimulador.NoResponderUnaVez
                when Interlocked.Exchange(ref _primeraSolicitudAtendida, 1) == 0:
                await EsperarCancelacionAsync(cancelacion);
                return new ResultadoAutorizacion.SinRespuesta();
        }

        var resultado = await RegistrarAsync(solicitud, numero, cancelacion);
        if (_respuesta == RespuestaSimulador.AutorizarSinResponder && resultado is ResultadoAutorizacion.Autorizada)
        {
            // Registrado (y el candado ya liberado): como un ARCA que autorizó y cuya respuesta no llegó.
            await EsperarCancelacionAsync(cancelacion);
            return new ResultadoAutorizacion.SinRespuesta();
        }

        return resultado;
    }

    /// <summary>
    /// El comprobante registrado del tipo, punto de venta y número: autorizado con su total, CAE, vencimiento y fecha, o
    /// no autorizado si ese número nunca se autorizó. Un número ya autorizado cuyos datos no están en el archivo (más
    /// viejo que los últimos <see cref="ComprobantesPorClave"/>, o de un archivo de FEAT-007) es "sin respuesta": ARCA sí
    /// lo autorizó y "no autorizado" llevaría a emitir de nuevo. Con <c>Arca:Simulado:Consulta = no_responder</c> espera
    /// la cancelación y devuelve "sin respuesta".
    /// </summary>
    public async Task<ResultadoConsulta> ConsultarAsync(
        TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion)
    {
        var comprobante = new NumeroComprobante(tipo, puntoVenta, numero);
        if (!cancelacion.CanBeCanceled)
        {
            return new ResultadoConsulta.NoConsultada(MotivoSinPlazo);
        }

        if (cancelacion.IsCancellationRequested)
        {
            return new ResultadoConsulta.NoConsultada(MotivoCancelada);
        }

        if (_consulta == ConsultaSimulador.NoResponder)
        {
            await EsperarCancelacionAsync(cancelacion);
            return new ResultadoConsulta.SinRespuesta();
        }

        try
        {
            await _candado.WaitAsync(cancelacion);
        }
        catch (OperationCanceledException)
        {
            return new ResultadoConsulta.NoConsultada(MotivoCancelada);
        }

        try
        {
            if (!IntentarLeer(out var estado))
            {
                return new ResultadoConsulta.NoConsultada(MotivoContador);
            }

            var clave = Clave(tipo, puntoVenta);
            if (estado.Comprobantes.TryGetValue(clave, out var lista)
                && lista.Find(c => c.Numero == numero) is { } encontrado)
            {
                return new ResultadoConsulta.Autorizado(
                    encontrado.TotalCentavos / 100m, encontrado.Cae, encontrado.Vencimiento, encontrado.Fecha);
            }

            if (numero <= estado.Contador(clave))
            {
                _registro.LogWarning(
                    "ARCA simulada: {Comprobante} figura autorizado pero sus datos ya no están en {Archivo} (solo se " +
                    "guardan los últimos {Cantidad}); la consulta queda sin respuesta.",
                    comprobante.ToString(),
                    NombreArchivo,
                    ComprobantesPorClave);
                return new ResultadoConsulta.SinRespuesta();
            }

            return new ResultadoConsulta.NoAutorizado();
        }
        finally
        {
            _candado.Release();
        }
    }

    private static async Task EsperarCancelacionAsync(CancellationToken cancelacion)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancelacion);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<ResultadoAutorizacion> RegistrarAsync(
        SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
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
            if (!IntentarLeer(out var estado))
            {
                return new ResultadoAutorizacion.NoEnviada(MotivoContador);
            }

            var clave = Clave(solicitud.Tipo, solicitud.PuntoVenta);
            var esperado = estado.Contador(clave) + 1;
            if (esperado > NumeroComprobante.NumeroMaximo)
            {
                RegistrarNumeroMaximo(solicitud.Tipo, solicitud.PuntoVenta);
                return new ResultadoAutorizacion.NoEnviada(MotivoContador);
            }

            if (numero != esperado)
            {
                _registro.LogInformation(
                    "ARCA simulada rechazó la solicitud ({Tipo}, punto de venta {PuntoVenta}) con el código {Codigo}: el " +
                    "número no es el próximo a autorizar.",
                    solicitud.Tipo,
                    solicitud.PuntoVenta,
                    CodigoRechazo);
                return new ResultadoAutorizacion.Rechazada(CodigoRechazo, DescripcionRechazo);
            }

            var cae = RandomNumberGenerator.GetString("0123456789", ResultadoAutorizacion.Autorizada.LargoCae);
            var vencimiento = solicitud.Fecha.AddDays(DiasVencimientoCae);
            estado.Contadores[clave] = numero;
            if (!estado.Comprobantes.TryGetValue(clave, out var lista))
            {
                lista = [];
                estado.Comprobantes[clave] = lista;
            }

            lista.Add(new ComprobanteSimulado(
                numero, decimal.ToInt64(solicitud.Importes.Total * 100m), cae, vencimiento, solicitud.Fecha));
            lista.Sort((a, b) => a.Numero.CompareTo(b.Numero));
            if (lista.Count > ComprobantesPorClave)
            {
                lista.RemoveRange(0, lista.Count - ComprobantesPorClave);
            }

            var contenido = Serializar(estado);
            if (contenido.Length > TamanoMaximoArchivo)
            {
                // Grabado no se podría volver a leer: no se registra el comprobante y no se "envía" nada.
                _registro.LogError(
                    "ARCA simulada: grabar el comprobante dejaría {Archivo} por encima del tamaño máximo de {Maximo} " +
                    "bytes. La emisión no se envió y el archivo no se modificó.",
                    NombreArchivo,
                    TamanoMaximoArchivo);
                return new ResultadoAutorizacion.NoEnviada(MotivoContador);
            }

            try
            {
                Grabar(contenido);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _registro.LogError(
                    "ARCA simulada: no se pudo grabar {Archivo} en el directorio de datos ({TipoFalla}). La emisión no se " +
                    "envió.",
                    NombreArchivo,
                    error.GetType().Name);
                return new ResultadoAutorizacion.NoEnviada(MotivoContador);
            }

            _registro.LogInformation(
                "ARCA simulada autorizó {Comprobante} (sin validez fiscal).",
                new NumeroComprobante(solicitud.Tipo, solicitud.PuntoVenta, numero).ToString());
            return new ResultadoAutorizacion.Autorizada(numero, cae, vencimiento);
        }
        finally
        {
            _candado.Release();
        }
    }

    private bool IntentarLeer(out EstadoSimulador estado)
    {
        try
        {
            estado = Leer();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or FormatException)
        {
            // Solo el nombre del archivo y el tipo de la falla: el texto de la excepción puede traer el contenido o la
            // ruta con el usuario de la PC.
            _registro.LogError(
                "ARCA simulada: no se pudo leer el archivo {Archivo} del directorio de datos ({TipoFalla}). No se envió " +
                "nada a autorizar ni a consultar; revisá ese archivo; no lo borres si hay emisiones pendientes de " +
                "verificación (el simulador olvidaría los comprobantes que ya autorizó).",
                NombreArchivo,
                error.GetType().Name);
            estado = null!;
            return false;
        }
    }

    private void RegistrarNumeroMaximo(TipoComprobante tipo, int puntoVenta) =>
        _registro.LogError(
            "ARCA simulada: el contador {Archivo} llegó al número máximo de comprobante para {Tipo}, punto de venta " +
            "{PuntoVenta}.",
            NombreArchivo,
            tipo,
            puntoVenta);

    private static string Clave(TipoComprobante tipo, int puntoVenta) =>
        $"{(tipo == TipoComprobante.FacturaB ? "B" : "C")}-{puntoVenta}";

    /// <summary><c>B-&lt;pv&gt;</c> o <c>C-&lt;pv&gt;</c> con pv de 1 a 99999, sin ceros a la izquierda.</summary>
    private static bool EsClaveValida(string clave)
    {
        if (clave.Length is < 3 or > 7 || clave[0] is not ('B' or 'C') || clave[1] != '-' || clave[2] == '0')
        {
            return false;
        }

        var digitos = clave.AsSpan(2);
        foreach (var caracter in digitos)
        {
            if (!char.IsAsciiDigit(caracter))
            {
                return false;
            }
        }

        return int.Parse(digitos, NumberStyles.None, CultureInfo.InvariantCulture) is >= 1 and <= PuntoVentaMaximo;
    }

    /// <summary>El estado guardado; vacío si el archivo todavía no existe. Los archivos de FEAT-007 no tienen comprobantes.</summary>
    /// <exception cref="JsonException">No es JSON o supera la profundidad máxima.</exception>
    /// <exception cref="FormatException">Es JSON pero no tiene la forma del archivo del simulador.</exception>
    private EstadoSimulador Leer()
    {
        var estado = new EstadoSimulador();
        if (!File.Exists(_archivo))
        {
            return estado;
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

        JsonElement? contadores = null;
        JsonElement? comprobantes = null;
        foreach (var propiedad in raiz.EnumerateObject())
        {
            switch (propiedad.Name)
            {
                case PropiedadContadores when contadores is null:
                    contadores = propiedad.Value;
                    break;
                case PropiedadComprobantes when comprobantes is null:
                    comprobantes = propiedad.Value;
                    break;
                default:
                    throw new FormatException(); // desconocida o repetida
            }
        }

        if (contadores is { } elementosContadores)
        {
            foreach (var (clave, valor) in Propiedades(elementosContadores))
            {
                estado.Contadores[clave] = Entero(valor, 0);
            }
        }

        if (comprobantes is { } elementosComprobantes)
        {
            foreach (var (clave, valor) in Propiedades(elementosComprobantes))
            {
                estado.Comprobantes[clave] = LeerLista(valor, estado.Contador(clave));
            }
        }

        return estado;
    }

    /// <summary>Las propiedades de un objeto con claves de tipo y punto de venta válidas y sin repetir.</summary>
    private static List<(string Clave, JsonElement Valor)> Propiedades(JsonElement objeto)
    {
        if (objeto.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException();
        }

        var propiedades = new List<(string, JsonElement)>();
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propiedad in objeto.EnumerateObject())
        {
            if (!EsClaveValida(propiedad.Name) || !vistas.Add(propiedad.Name))
            {
                throw new FormatException();
            }

            propiedades.Add((propiedad.Name, propiedad.Value));
        }

        return propiedades;
    }

    private static List<ComprobanteSimulado> LeerLista(JsonElement lista, long contador)
    {
        if (lista.ValueKind != JsonValueKind.Array || lista.GetArrayLength() > ComprobantesPorClave)
        {
            throw new FormatException();
        }

        var comprobantes = new List<ComprobanteSimulado>();
        var numeros = new HashSet<long>();
        foreach (var elemento in lista.EnumerateArray())
        {
            if (elemento.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException();
            }

            var valores = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var propiedad in elemento.EnumerateObject())
            {
                // Solo las cinco propiedades del comprobante, sin repetir: ningún dato del receptor ni otro campo.
                if (!PropiedadesComprobante.Contains(propiedad.Name) || !valores.TryAdd(propiedad.Name, propiedad.Value))
                {
                    throw new FormatException();
                }
            }

            if (valores.Count != PropiedadesComprobante.Length)
            {
                throw new FormatException();
            }

            var numero = Entero(valores[PropiedadNumero], NumeroComprobante.NumeroMinimo);
            if (numero > contador || !numeros.Add(numero))
            {
                throw new FormatException();
            }

            var cae = valores[PropiedadCae];
            if (cae.ValueKind != JsonValueKind.String
                || cae.GetString() is not { Length: ResultadoAutorizacion.Autorizada.LargoCae } textoCae
                || !textoCae.All(char.IsAsciiDigit))
            {
                throw new FormatException();
            }

            comprobantes.Add(new ComprobanteSimulado(
                numero,
                Entero(valores[PropiedadTotal], 0, long.MaxValue),
                textoCae,
                Fecha(valores[PropiedadVencimiento]),
                Fecha(valores[PropiedadFecha])));
        }

        comprobantes.Sort((a, b) => a.Numero.CompareTo(b.Numero));
        return comprobantes;
    }

    private static long Entero(JsonElement valor, long minimo, long maximo = NumeroComprobante.NumeroMaximo)
    {
        if (valor.ValueKind != JsonValueKind.Number || !valor.TryGetInt64(out var entero) || entero < minimo
            || entero > maximo)
        {
            throw new FormatException();
        }

        return entero;
    }

    private static DateOnly Fecha(JsonElement valor)
    {
        if (valor.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(
                valor.GetString(), FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            throw new FormatException();
        }

        return fecha;
    }

    /// <summary>El archivo compacto: contadores y listas ordenados por clave, comprobantes por número.</summary>
    private static byte[] Serializar(EstadoSimulador estado)
    {
        using var memoria = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(memoria))
        {
            escritor.WriteStartObject();
            escritor.WriteStartObject(PropiedadContadores);
            foreach (var (clave, valor) in estado.Contadores.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                escritor.WriteNumber(clave, valor);
            }

            escritor.WriteEndObject();
            escritor.WriteStartObject(PropiedadComprobantes);
            foreach (var (clave, lista) in estado.Comprobantes.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                escritor.WriteStartArray(clave);
                foreach (var comprobante in lista)
                {
                    escritor.WriteStartObject();
                    escritor.WriteNumber(PropiedadNumero, comprobante.Numero);
                    escritor.WriteNumber(PropiedadTotal, comprobante.TotalCentavos);
                    escritor.WriteString(PropiedadCae, comprobante.Cae);
                    escritor.WriteString(
                        PropiedadVencimiento, comprobante.Vencimiento.ToString(FormatoFecha, CultureInfo.InvariantCulture));
                    escritor.WriteString(
                        PropiedadFecha, comprobante.Fecha.ToString(FormatoFecha, CultureInfo.InvariantCulture));
                    escritor.WriteEndObject();
                }

                escritor.WriteEndArray();
            }

            escritor.WriteEndObject();
            escritor.WriteEndObject();
        }

        return memoria.ToArray();
    }

    /// <summary>
    /// Graba en un temporal del mismo directorio, lo baja al disco y lo mueve sobre el archivo: nunca queda a medio
    /// escribir, y un corte de luz después de autorizar no hace retroceder el contador (WARN-1: un contador viejo daría
    /// un "no autorizado" falso en la consulta y habilitaría una reemisión).
    /// </summary>
    /// <exception cref="IOException">No se pudo grabar.</exception>
    private void Grabar(byte[] contenido)
    {
        var temporal = $"{_archivo}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var flujo = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                flujo.Write(contenido);
                flujo.Flush(flushToDisk: true);
            }

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

    /// <summary>Un comprobante autorizado guardado: sin datos del receptor (D-13).</summary>
    private sealed record ComprobanteSimulado(
        long Numero, long TotalCentavos, string Cae, DateOnly Vencimiento, DateOnly Fecha);

    private sealed class EstadoSimulador
    {
        public Dictionary<string, long> Contadores { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<ComprobanteSimulado>> Comprobantes { get; } = new(StringComparer.Ordinal);

        /// <summary>El último número autorizado de la clave; 0 si falta.</summary>
        public long Contador(string clave) => Contadores.GetValueOrDefault(clave);
    }
}
