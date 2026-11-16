using System.Globalization;
using Microsoft.Extensions.Configuration;
using Optica.Domain.Facturacion;
using CuitDominio = Optica.Domain.Configuracion.Cuit;

namespace Optica.Infrastructure.Arca;

/// <summary>Qué responde el simulador de ARCA (<c>Arca:Simulado:Respuesta</c>, D-17).</summary>
public enum RespuestaSimulador
{
    /// <summary><c>autorizar</c> (por defecto): numera y devuelve un CAE ficticio.</summary>
    Autorizar,

    /// <summary><c>rechazar</c>: rechazo con el código 10016 y una descripción fija.</summary>
    Rechazar,

    /// <summary><c>no_responder</c>: espera hasta la cancelación que recibe y devuelve "sin respuesta", sin registrar.</summary>
    NoResponder,

    /// <summary>
    /// <c>autorizar_sin_responder</c> (FEAT-008, D-19): registra el comprobante como autorizado, espera la cancelación y
    /// devuelve "sin respuesta" (AC-05).
    /// </summary>
    AutorizarSinResponder,

    /// <summary>
    /// <c>no_responder_una_vez</c> (FEAT-008, D-19): la primera solicitud de autorización del proceso se comporta como
    /// <see cref="NoResponder"/>; las siguientes, como <see cref="Autorizar"/> (AC-06).
    /// </summary>
    NoResponderUnaVez,
}

/// <summary>Qué responde la consulta por número del simulador de ARCA (<c>Arca:Simulado:Consulta</c>, FEAT-008, D-19).</summary>
public enum ConsultaSimulador
{
    /// <summary><c>responder</c> (por defecto): autorizado con sus datos o no autorizado.</summary>
    Responder,

    /// <summary><c>no_responder</c>: espera hasta la cancelación que recibe y devuelve "sin respuesta" (AC-11).</summary>
    NoResponder,
}

/// <summary>
/// Las claves <c>Arca:*</c> leídas y validadas (D-11, D-13, D-15, D-17; FEAT-008 D-19, D-20). Sin <c>Arca:Entorno</c> el
/// entorno es el simulado: una configuración faltante nunca apunta a producción (R-14, ADR-022). Un entorno, tiempo
/// máximo, espera de verificación, respuesta o consulta del simulador o CUIT inválidos lanzan
/// <see cref="OpcionesArcaInvalidasException"/> (la Api no arranca); la falta del
/// CUIT, del certificado o de su clave fuera del simulado no lanza: <see cref="CredencialesCompletas"/> es falso y cada
/// emisión termina "no enviada". El CUIT, la ruta y la clave del certificado nunca se registran ni se muestran (D-13):
/// esta clase no sobrescribe <see cref="object.ToString"/>.
/// </summary>
public sealed class OpcionesArca
{
    public const string ClaveEntorno = "Arca:Entorno";
    public const string ClaveCuit = "Arca:Cuit";
    public const string ClaveCertificadoRuta = "Arca:Certificado:Ruta";
    public const string ClaveCertificadoClave = "Arca:Certificado:Clave";
    public const string ClaveTiempoMaximo = "Arca:TiempoMaximoSegundos";
    public const string ClaveRespuestaSimulado = "Arca:Simulado:Respuesta";
    public const string ClaveConsultaSimulado = "Arca:Simulado:Consulta";
    public const string ClaveEsperaVerificacion = "Arca:EsperaVerificacionSegundos";

    /// <summary>El tiempo máximo sin la clave (NFR-01).</summary>
    public const int TiempoMaximoPorDefectoSegundos = 30;

    public const int TiempoMaximoMinimoSegundos = 1;
    public const int TiempoMaximoMaximoSegundos = 120;

    /// <summary>La espera antes de verificar una emisión pendiente sin la clave (FR-16, D-20): 1 minuto.</summary>
    public const int EsperaVerificacionPorDefectoSegundos = 60;

    public const int EsperaVerificacionMinimaSegundos = 1;
    public const int EsperaVerificacionMaximaSegundos = 3600;

    private const string RespuestasAdmitidas =
        "autorizar, rechazar, no_responder, autorizar_sin_responder o no_responder_una_vez";

    private const string ConsultasAdmitidas = "responder o no_responder";

    private static readonly (string Texto, EntornoArca Entorno)[] Entornos =
    [
        ("Simulado", EntornoArca.Simulado),
        ("Homologacion", EntornoArca.Homologacion),
        ("Produccion", EntornoArca.Produccion),
    ];

    private static readonly (string Texto, RespuestaSimulador Respuesta)[] Respuestas =
    [
        ("autorizar", RespuestaSimulador.Autorizar),
        ("rechazar", RespuestaSimulador.Rechazar),
        ("no_responder", RespuestaSimulador.NoResponder),
        ("autorizar_sin_responder", RespuestaSimulador.AutorizarSinResponder),
        ("no_responder_una_vez", RespuestaSimulador.NoResponderUnaVez),
    ];

    private static readonly (string Texto, ConsultaSimulador Consulta)[] Consultas =
    [
        ("responder", ConsultaSimulador.Responder),
        ("no_responder", ConsultaSimulador.NoResponder),
    ];

    private OpcionesArca()
    {
    }

    public EntornoArca Entorno { get; private init; }

    /// <summary>El plazo de toda la emisión, que aplica <c>EmitirFactura</c> (D-15, D-32).</summary>
    public TimeSpan TiempoMaximo { get; private init; }

    /// <summary>
    /// Cuánto tiene que pasar desde que una emisión quedó pendiente para poder verificarla (FR-16, D-20): 60 s por
    /// defecto; el harness e2e usa menos. La aplica la verificación (Block 5 de FEAT-008).
    /// </summary>
    public TimeSpan EsperaVerificacion { get; private init; }

    public RespuestaSimulador RespuestaSimulado { get; private init; }

    /// <summary>Qué responde la consulta por número del simulador (D-19).</summary>
    public ConsultaSimulador ConsultaSimulado { get; private init; }

    /// <summary>11 dígitos con dígito verificador válido, o null si falta. Fuera del simulado únicamente.</summary>
    public string? Cuit { get; private init; }

    /// <summary>La ruta del PFX, o null si falta. Nunca va al log (D-13).</summary>
    public string? RutaCertificado { get; private init; }

    /// <summary>La clave del PFX, o null si falta. Nunca va al log ni a <c>appsettings.json</c> (D-13, D-26).</summary>
    public string? ClaveCertificado { get; private init; }

    /// <summary>
    /// Verdadero si hay alguna de <c>Arca:Cuit</c>, <c>Arca:Certificado:Ruta</c> o <c>Arca:Certificado:Clave</c> con
    /// valor, en cualquier entorno. En simulado no se validan ni se conservan; sirve para la advertencia del arranque
    /// (R-10). Nunca cambia el entorno (R-14).
    /// </summary>
    public bool CredencialesPresentes { get; private init; }

    /// <summary>Verdadero si están el CUIT, la ruta del certificado y su clave (lo que necesita el cliente real).</summary>
    public bool CredencialesCompletas => Cuit is not null && RutaCertificado is not null && ClaveCertificado is not null;

    /// <summary>Las claves de credenciales que faltan, para la advertencia del arranque (nombres, nunca valores).</summary>
    public IReadOnlyList<string> ClavesFaltantes =>
    [
        .. Cuit is null ? [ClaveCuit] : Array.Empty<string>(),
        .. RutaCertificado is null ? [ClaveCertificadoRuta] : Array.Empty<string>(),
        .. ClaveCertificado is null ? [ClaveCertificadoClave] : Array.Empty<string>(),
    ];

    /// <summary>Lee y valida las claves <c>Arca:*</c>. Una clave vacía o solo con espacios cuenta como ausente.</summary>
    /// <exception cref="OpcionesArcaInvalidasException">
    /// El entorno no es <c>Simulado</c>, <c>Homologacion</c> ni <c>Produccion</c> (sin distinguir mayúsculas); el tiempo
    /// máximo no es un entero de 1 a 120; la espera de verificación no es un entero de 1 a 3600; la respuesta del
    /// simulador no es <c>autorizar</c>, <c>rechazar</c>, <c>no_responder</c>, <c>autorizar_sin_responder</c> ni
    /// <c>no_responder_una_vez</c>; la consulta del simulador no es <c>responder</c> ni <c>no_responder</c>; o, fuera del
    /// simulado, el CUIT no son 11 dígitos con dígito verificador válido. El mensaje nombra la clave y los valores
    /// admitidos, nunca el valor recibido.
    /// </exception>
    public static OpcionesArca Leer(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        var entorno = Valor(configuracion, ClaveEntorno) is { } textoEntorno
            ? Buscar(Entornos, textoEntorno, ClaveEntorno, "Simulado, Homologacion o Produccion")
            : EntornoArca.Simulado;

        var tiempoMaximo = TimeSpan.FromSeconds(Valor(configuracion, ClaveTiempoMaximo) is { } textoTiempo
            ? LeerSegundos(textoTiempo, ClaveTiempoMaximo, TiempoMaximoMinimoSegundos, TiempoMaximoMaximoSegundos)
            : TiempoMaximoPorDefectoSegundos);

        var esperaVerificacion = TimeSpan.FromSeconds(Valor(configuracion, ClaveEsperaVerificacion) is { } textoEspera
            ? LeerSegundos(
                textoEspera, ClaveEsperaVerificacion, EsperaVerificacionMinimaSegundos, EsperaVerificacionMaximaSegundos)
            : EsperaVerificacionPorDefectoSegundos);

        var respuesta = Valor(configuracion, ClaveRespuestaSimulado) is { } textoRespuesta
            ? Buscar(Respuestas, textoRespuesta, ClaveRespuestaSimulado, RespuestasAdmitidas)
            : RespuestaSimulador.Autorizar;

        var consulta = Valor(configuracion, ClaveConsultaSimulado) is { } textoConsulta
            ? Buscar(Consultas, textoConsulta, ClaveConsultaSimulado, ConsultasAdmitidas)
            : ConsultaSimulador.Responder;

        var credencialesPresentes = Valor(configuracion, ClaveCuit) is not null
            || Valor(configuracion, ClaveCertificadoRuta) is not null
            || Valor(configuracion, ClaveCertificadoClave) is not null;

        if (entorno == EntornoArca.Simulado)
        {
            // El simulador no usa credenciales: ni se validan ni se conservan; solo se anota que están (R-10).
            return new OpcionesArca
            {
                Entorno = entorno,
                TiempoMaximo = tiempoMaximo,
                EsperaVerificacion = esperaVerificacion,
                RespuestaSimulado = respuesta,
                ConsultaSimulado = consulta,
                CredencialesPresentes = credencialesPresentes,
            };
        }

        var cuit = Valor(configuracion, ClaveCuit);
        if (cuit is not null && !EsCuitValido(cuit))
        {
            throw new OpcionesArcaInvalidasException(
                ClaveCuit, "deben ser 11 dígitos, sin guiones, con dígito verificador válido");
        }

        return new OpcionesArca
        {
            Entorno = entorno,
            TiempoMaximo = tiempoMaximo,
            EsperaVerificacion = esperaVerificacion,
            RespuestaSimulado = respuesta,
            ConsultaSimulado = consulta,
            CredencialesPresentes = credencialesPresentes,
            Cuit = cuit,
            RutaCertificado = Valor(configuracion, ClaveCertificadoRuta),
            ClaveCertificado = Valor(configuracion, ClaveCertificadoClave),
        };
    }

    /// <summary>
    /// 11 dígitos ASCII, sin guiones, cuyo último es el verificador de módulo 11 de los 10 anteriores (un verificador
    /// 10 es inválido). Delega en <see cref="CuitDominio.EsValidoCanonico"/>, la única implementación del
    /// módulo 11 (D-12 y D-18 de FEAT-009): un CUIT con guiones, válido para los datos del emisor, acá se rechaza, así
    /// nunca llega con guiones a WSAA/WSFEv1.
    /// </summary>
    public static bool EsCuitValido(string cuit) => CuitDominio.EsValidoCanonico(cuit);

    private static string? Valor(IConfiguration configuracion, string clave) =>
        string.IsNullOrWhiteSpace(configuracion[clave]) ? null : configuracion[clave]!.Trim();

    // Comparación explícita contra los nombres: Enum.TryParse aceptaría números ("2" sería Produccion).
    private static T Buscar<T>((string Texto, T Valor)[] opciones, string texto, string clave, string admitidos)
    {
        foreach (var (nombre, valor) in opciones)
        {
            if (string.Equals(nombre, texto, StringComparison.OrdinalIgnoreCase))
            {
                return valor;
            }
        }

        throw new OpcionesArcaInvalidasException(clave, $"debe ser {admitidos}");
    }

    private static int LeerSegundos(string texto, string clave, int minimo, int maximo)
    {
        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos)
            || segundos < minimo
            || segundos > maximo)
        {
            throw new OpcionesArcaInvalidasException(
                clave, $"debe ser un número entero de segundos entre {minimo} y {maximo}");
        }

        return segundos;
    }
}

/// <summary>
/// Una clave <c>Arca:*</c> tiene un valor inválido: la Api no arranca. El mensaje nombra la clave y lo que se espera,
/// nunca el valor recibido (puede ser un dato de la empresa).
/// </summary>
public sealed class OpcionesArcaInvalidasException : Exception
{
    public OpcionesArcaInvalidasException(string clave, string esperado)
        : base($"El valor de la clave de configuración '{clave}' no es válido: {esperado}.")
    {
        Clave = clave;
    }

    /// <summary>La clave de configuración que hay que corregir.</summary>
    public string Clave { get; }
}
