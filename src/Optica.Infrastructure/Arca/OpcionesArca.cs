using System.Globalization;
using Microsoft.Extensions.Configuration;
using Optica.Domain.Facturacion;

namespace Optica.Infrastructure.Arca;

/// <summary>Qué responde el simulador de ARCA (<c>Arca:Simulado:Respuesta</c>, D-17).</summary>
public enum RespuestaSimulador
{
    /// <summary><c>autorizar</c> (por defecto): numera y devuelve un CAE ficticio.</summary>
    Autorizar,

    /// <summary><c>rechazar</c>: rechazo con el código 10016 y una descripción fija.</summary>
    Rechazar,

    /// <summary><c>no_responder</c>: espera hasta la cancelación que recibe y devuelve "sin respuesta".</summary>
    NoResponder,
}

/// <summary>
/// Las claves <c>Arca:*</c> leídas y validadas (D-11, D-13, D-15, D-17). Sin <c>Arca:Entorno</c> el entorno es el
/// simulado: una configuración faltante nunca apunta a producción (R-14, ADR-022). Un entorno, tiempo máximo, respuesta
/// del simulador o CUIT inválidos lanzan <see cref="OpcionesArcaInvalidasException"/> (la Api no arranca); la falta del
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

    /// <summary>El tiempo máximo sin la clave (NFR-01).</summary>
    public const int TiempoMaximoPorDefectoSegundos = 30;

    public const int TiempoMaximoMinimoSegundos = 1;
    public const int TiempoMaximoMaximoSegundos = 120;

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
    ];

    // Pesos del dígito verificador del CUIT (módulo 11).
    private static readonly int[] PesosCuit = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    private OpcionesArca()
    {
    }

    public EntornoArca Entorno { get; private init; }

    /// <summary>El plazo de toda la emisión, que aplica <c>EmitirFactura</c> (D-15, D-32).</summary>
    public TimeSpan TiempoMaximo { get; private init; }

    public RespuestaSimulador RespuestaSimulado { get; private init; }

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
    /// máximo no es un entero de 1 a 120; la respuesta del simulador no es <c>autorizar</c>, <c>rechazar</c> ni
    /// <c>no_responder</c>; o, fuera del simulado, el CUIT no son 11 dígitos con dígito verificador válido. El mensaje
    /// nombra la clave y nunca el valor.
    /// </exception>
    public static OpcionesArca Leer(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        var entorno = Valor(configuracion, ClaveEntorno) is { } textoEntorno
            ? Buscar(Entornos, textoEntorno, ClaveEntorno, "Simulado, Homologacion o Produccion")
            : EntornoArca.Simulado;

        var tiempoMaximo = TimeSpan.FromSeconds(Valor(configuracion, ClaveTiempoMaximo) is { } textoTiempo
            ? LeerSegundos(textoTiempo)
            : TiempoMaximoPorDefectoSegundos);

        var respuesta = Valor(configuracion, ClaveRespuestaSimulado) is { } textoRespuesta
            ? Buscar(Respuestas, textoRespuesta, ClaveRespuestaSimulado, "autorizar, rechazar o no_responder")
            : RespuestaSimulador.Autorizar;

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
                RespuestaSimulado = respuesta,
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
            RespuestaSimulado = respuesta,
            CredencialesPresentes = credencialesPresentes,
            Cuit = cuit,
            RutaCertificado = Valor(configuracion, ClaveCertificadoRuta),
            ClaveCertificado = Valor(configuracion, ClaveCertificadoClave),
        };
    }

    /// <summary>11 dígitos ASCII cuyo último es el verificador de módulo 11 de los 10 anteriores.</summary>
    public static bool EsCuitValido(string cuit)
    {
        if (cuit.Length != 11 || !cuit.All(char.IsAsciiDigit))
        {
            return false;
        }

        var suma = 0;
        for (var i = 0; i < PesosCuit.Length; i++)
        {
            suma += (cuit[i] - '0') * PesosCuit[i];
        }

        var verificador = 11 - (suma % 11);
        if (verificador == 11)
        {
            verificador = 0;
        }

        return verificador != 10 && verificador == cuit[10] - '0';
    }

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

    private static int LeerSegundos(string texto)
    {
        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos)
            || segundos is < TiempoMaximoMinimoSegundos or > TiempoMaximoMaximoSegundos)
        {
            throw new OpcionesArcaInvalidasException(
                ClaveTiempoMaximo,
                $"debe ser un número entero de segundos entre {TiempoMaximoMinimoSegundos} y {TiempoMaximoMaximoSegundos}");
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
