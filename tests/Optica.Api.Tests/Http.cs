using System.Text;
using System.Text.Json;

namespace Optica.Api.Tests;

/// <summary>Atajos para los pedidos de las pruebas, con la cookie de sesión manejada a mano.</summary>
public static class Http
{
    public const string Contrasena = "clave-de-prueba-1";

    public static string CuerpoContrasena(string? contrasena) =>
        JsonSerializer.Serialize(new { contrasena });

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

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        return JsonDocument.Parse(texto).RootElement;
    }

    /// <summary>Los mensajes de <c>errors.contrasena</c> de un ValidationProblemDetails.</summary>
    public static async Task<string[]> ErroresContrasenaAsync(HttpResponseMessage respuesta)
    {
        var json = await JsonAsync(respuesta);
        return json.GetProperty("errors").GetProperty("contrasena").EnumerateArray()
            .Select(valor => valor.GetString()!)
            .ToArray();
    }
}
