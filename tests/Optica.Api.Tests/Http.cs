using System.Text;
using System.Text.Json;

namespace Optica.Api.Tests;

/// <summary>Atajos para los pedidos de las pruebas, con la cookie de sesión manejada a mano.</summary>
public static class Http
{
    public const string Contrasena = "clave-de-prueba-1";

    public static string CuerpoContrasena(string? contrasena) =>
        JsonSerializer.Serialize(new { contrasena });

    /// <summary>
    /// Cuerpo de <c>cambiar</c>. Escribe cada carácter fuera de ASCII como escape <c>\uXXXX</c>, así un sustituto
    /// suelto viaja tal cual (el serializador lo cambiaría por U+FFFD); null queda como <c>null</c> de JSON.
    /// </summary>
    public static string CuerpoCambio(string? actual, string? nueva) =>
        "{\"contrasenaActual\":" + TextoJson(actual) + ",\"contrasenaNueva\":" + TextoJson(nueva) + "}";

    /// <summary>Pide el cambio de contraseña con la cookie de sesión indicada.</summary>
    public static Task<HttpResponseMessage> CambiarAsync(HttpClient cliente, string? actual, string? nueva, string? cookie) =>
        PostJsonAsync(cliente, "/api/acceso/cambiar", CuerpoCambio(actual, nueva), cookie);

    private static string TextoJson(string? texto)
    {
        if (texto is null)
        {
            return "null";
        }

        var json = new StringBuilder("\"");
        foreach (var caracter in texto)
        {
            if (caracter is '"' or '\\')
            {
                json.Append('\\').Append(caracter);
            }
            else if (caracter < 0x20 || caracter > 0x7E)
            {
                json.Append("\\u").Append(((int)caracter).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                json.Append(caracter);
            }
        }

        return json.Append('"').ToString();
    }

    public static Task<HttpResponseMessage> PostJsonAsync(
        HttpClient cliente, string ruta, string json, string? cookie = null) =>
        EnviarAsync(cliente, HttpMethod.Post, ruta, new StringContent(json, Encoding.UTF8, "application/json"), cookie);

    public static Task<HttpResponseMessage> GetAsync(HttpClient cliente, string ruta, string? cookie = null) =>
        EnviarAsync(cliente, HttpMethod.Get, ruta, null, cookie);

    public static Task<HttpResponseMessage> EnviarAsync(
        HttpClient cliente, HttpMethod metodo, string ruta, HttpContent? contenido, string? cookie = null)
    {
        var pedido = new HttpRequestMessage(metodo, ruta) { Content = contenido };
        if (cookie is not null)
        {
            pedido.Headers.Add("Cookie", cookie);
        }

        return cliente.SendAsync(pedido);
    }

    /// <summary>La línea <c>Set-Cookie</c> completa de la respuesta, o null si no hay.</summary>
    public static string? SetCookie(HttpResponseMessage respuesta) =>
        respuesta.Headers.TryGetValues("Set-Cookie", out var valores) ? valores.FirstOrDefault() : null;

    /// <summary>El par <c>nombre=valor</c> de la cookie emitida, listo para mandar en el encabezado <c>Cookie</c>.</summary>
    public static string? Cookie(HttpResponseMessage respuesta) => SetCookie(respuesta)?.Split(';')[0];

    /// <summary>Define la contraseña y devuelve la cookie de sesión emitida.</summary>
    public static async Task<string> DefinirAsync(HttpClient cliente, string contrasena = Contrasena)
    {
        var respuesta = await PostJsonAsync(cliente, "/api/acceso/definir", CuerpoContrasena(contrasena));
        Assert.Equal(System.Net.HttpStatusCode.Created, respuesta.StatusCode);
        return Cookie(respuesta) ?? throw new InvalidOperationException("Definir no emitió la cookie de sesión.");
    }

    /// <summary>
    /// Contenido que no declara su largo: viaja sin <c>Content-Length</c> (en partes), como un cliente que lo omite.
    /// </summary>
    public sealed class ContenidoSinLargo : HttpContent
    {
        private readonly byte[] _datos;

        public ContenidoSinLargo(string texto, string? tipo)
        {
            _datos = Encoding.UTF8.GetBytes(texto);
            if (tipo is not null)
            {
                Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo);
            }
        }

        protected override Task SerializeToStreamAsync(Stream destino, System.Net.TransportContext? contexto) =>
            destino.WriteAsync(_datos).AsTask();

        protected override bool TryComputeLength(out long largo)
        {
            largo = 0;
            return false;
        }
    }

    /// <summary>
    /// El final exacto del GET de un presupuesto sin factura y sin emisión pendiente (FEAT-007, D-19, D-28): las dos
    /// claves de facturación van después de <c>version</c>.
    /// </summary>
    public const string SufijoSinFacturacion = ",\"factura\":null,\"emisionPendiente\":false}";

    /// <summary>
    /// El GET de un presupuesto sin factura ni pendiente, sin las dos claves de facturación: exige que el texto termine
    /// con <see cref="SufijoSinFacturacion"/> y devuelve el resto, que es exactamente la respuesta del alta, de la
    /// modificación o del paso a Final (que no llevan esas claves, D-28). Así las pruebas que comparan el GET con esas
    /// respuestas byte a byte siguen comparando lo mismo.
    /// </summary>
    public static JsonElement SinFacturacion(JsonElement lectura)
    {
        var texto = lectura.GetRawText();
        Assert.EndsWith(SufijoSinFacturacion, texto, StringComparison.Ordinal);
        using var documento = JsonDocument.Parse(texto[..^SufijoSinFacturacion.Length] + "}");
        return documento.RootElement.Clone();
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        return JsonDocument.Parse(texto).RootElement;
    }

    /// <summary>Los mensajes de <c>errors.contrasena</c> de un ValidationProblemDetails.</summary>
    public static Task<string[]> ErroresContrasenaAsync(HttpResponseMessage respuesta) =>
        ErroresDelCampoAsync(respuesta, "contrasena");

    /// <summary>Los mensajes de <c>errors.contrasenaActual</c> de un ValidationProblemDetails.</summary>
    public static Task<string[]> ErroresContrasenaActualAsync(HttpResponseMessage respuesta) =>
        ErroresDelCampoAsync(respuesta, "contrasenaActual");

    /// <summary>Los mensajes de <c>errors.contrasenaNueva</c> de un ValidationProblemDetails.</summary>
    public static Task<string[]> ErroresContrasenaNuevaAsync(HttpResponseMessage respuesta) =>
        ErroresDelCampoAsync(respuesta, "contrasenaNueva");

    /// <summary>Los campos presentes en <c>errors</c> de un ValidationProblemDetails, en orden.</summary>
    public static async Task<string[]> CamposConErrorAsync(HttpResponseMessage respuesta)
    {
        var json = await JsonAsync(respuesta);
        return json.GetProperty("errors").EnumerateObject().Select(campo => campo.Name).ToArray();
    }

    private static async Task<string[]> ErroresDelCampoAsync(HttpResponseMessage respuesta, string campo)
    {
        var json = await JsonAsync(respuesta);
        return json.GetProperty("errors").GetProperty(campo).EnumerateArray()
            .Select(valor => valor.GetString()!)
            .ToArray();
    }
}
