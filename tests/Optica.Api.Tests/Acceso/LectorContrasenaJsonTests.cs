using System.Buffers;
using System.Text;
using System.Text.Json;
using Optica.Api.Acceso;
using Optica.Api.Controllers;

namespace Optica.Api.Tests.Acceso;

/// <summary>
/// El lector entrega el texto tal como llegó, sin aplicar reglas del dominio: un sustituto suelto escapado llega tal
/// cual (lo rechaza Application), y un texto que no se puede decodificar es un cuerpo ilegible.
/// </summary>
public sealed class LectorContrasenaJsonTests
{
    [Fact]
    public void Leer_SustitutoSueltoEscapado_LlegaTalCual()
    {
        // En una lista y no en InlineData: los atributos no conservan un sustituto suelto.
        var casos = new (string Json, string Esperado)[]
        {
            ("{\"contrasena\":\"abcdefgh\\ud800\"}", "abcdefgh\ud800"),
            ("{\"contrasena\":\"\\udc00abcdefgh\"}", "\udc00abcdefgh"),
            ("{\"contrasena\":\"a\\\"b\\\\c\\/d\\b\\f\\n\\r\\t\\u00f1\\ud800\"}", "a\"b\\c/d\b\f\n\r\tñ\ud800"),
        };

        foreach (var (json, esperado) in casos)
        {
            Assert.Equal(esperado, Leer(Encoding.UTF8.GetBytes(json))?.Contrasena);
        }
    }

    [Fact]
    public void Leer_TextoValido_LlegaTalCual()
    {
        Assert.Equal("clave-ñandú-1", Leer(Encoding.UTF8.GetBytes("{\"contrasena\":\"clave-ñandú-1\"}"))?.Contrasena);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]
    [InlineData(new byte[] { 0xC3 })]
    public void Leer_BytesUtf8MalFormados_CuerpoIlegible(byte[] invalidos)
    {
        var json = Encoding.ASCII.GetBytes("{\"contrasena\":\"abcdefgh")
            .Concat(invalidos)
            .Concat(Encoding.ASCII.GetBytes("\\ud800\"}"))
            .ToArray();

        var error = Assert.Throws<JsonException>(() => Leer(json));

        // Ni el valor ni la causa original (que nombra los bytes) viajan en la excepción.
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("abcdefgh", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Leer_ValorPartidoEnVariosSegmentos_LlegaTalCual()
    {
        var json = Encoding.UTF8.GetBytes("{\"contrasena\":\"abcdefgh\\ud800\"}");
        var corte = Array.IndexOf(json, (byte)'h');
        var lector = new Utf8JsonReader(Segmentos(json[..corte], json[corte..]));
        lector.Read();
        lector.Read();
        lector.Read();
        Assert.True(lector.HasValueSequence);

        var valor = new LectorContrasenaJson().Read(ref lector, typeof(string), JsonSerializerOptions.Web);

        Assert.Equal("abcdefgh\ud800", valor);
    }

    private static SolicitudContrasena? Leer(byte[] json) =>
        JsonSerializer.Deserialize<SolicitudContrasena>(json, JsonSerializerOptions.Web);

    private static ReadOnlySequence<byte> Segmentos(byte[] primero, byte[] segundo)
    {
        var inicio = new Segmento(primero);
        var fin = inicio.Agregar(segundo);
        return new ReadOnlySequence<byte>(inicio, 0, fin, segundo.Length);
    }

    private sealed class Segmento : ReadOnlySequenceSegment<byte>
    {
        public Segmento(byte[] datos) => Memory = datos;

        public Segmento Agregar(byte[] datos)
        {
            var siguiente = new Segmento(datos) { RunningIndex = RunningIndex + Memory.Length };
            Next = siguiente;
            return siguiente;
        }
    }
}
