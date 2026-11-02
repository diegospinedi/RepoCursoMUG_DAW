using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Optica.Api.Acceso;

/// <summary>
/// Lee el campo <c>contrasena</c> del cuerpo sin validarlo: la política la aplica el caso de uso (Application).
/// <list type="bullet">
/// <item>
/// Un texto JSON se entrega tal cual, también si tiene un sustituto UTF-16 suelto escrito como escape (<c>\ud800</c>):
/// es lo que llegó, y el caso de uso lo rechaza como caracteres no válidos.
/// </item>
/// <item>Un texto con bytes UTF-8 mal formados no se puede leer: lanza <see cref="JsonException"/> (cuerpo ilegible).</item>
/// <item>Cualquier otro valor (número, arreglo, objeto) es un cuerpo ilegible: lanza <see cref="JsonException"/>.</item>
/// </list>
/// Las <see cref="JsonException"/> van sin mensaje propio ni causa: el lector de MVC las registra y no deben llevar el
/// valor.
/// </summary>
public sealed class LectorContrasenaJson : JsonConverter<string>
{
    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public override string? Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones)
    {
        if (lector.TokenType != JsonTokenType.String)
        {
            throw new JsonException();
        }

        try
        {
            return lector.GetString();
        }
        catch (InvalidOperationException)
        {
            // GetString rechaza los sustitutos sueltos escapados y los bytes mal formados, con un mensaje que nombra
            // el valor; no se propaga. Se decodifica el texto crudo: los bytes tienen que ser UTF-8 válido.
            var crudo = lector.HasValueSequence ? lector.ValueSequence.ToArray() : lector.ValueSpan.ToArray();
            return Desescapar(crudo);
        }
    }

    /// <summary>La contraseña nunca se serializa.</summary>
    public override void Write(Utf8JsonWriter escritor, string valor, JsonSerializerOptions opciones) =>
        throw new NotSupportedException("La contraseña no se serializa.");

    /// <summary>
    /// Decodifica el UTF-8 crudo de un texto JSON y resuelve sus escapes, sin exigir que los <c>\uXXXX</c> formen
    /// pares de sustitutos. Los escapes ya los validó el lector de JSON.
    /// </summary>
    private static string Desescapar(byte[] crudo)
    {
        string texto;
        try
        {
            texto = Utf8Estricto.GetString(crudo);
        }
        catch (DecoderFallbackException)
        {
            // La excepción original nombra los bytes inválidos; no se adjunta.
            throw new JsonException();
        }

        var resultado = new StringBuilder(texto.Length);
        for (var i = 0; i < texto.Length; i++)
        {
            if (texto[i] != '\\')
            {
                resultado.Append(texto[i]);
                continue;
            }

            i++;
            switch (texto[i])
            {
                case 'u':
                    resultado.Append((char)ushort.Parse(
                        texto.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                case 'b':
                    resultado.Append('\b');
                    break;
                case 'f':
                    resultado.Append('\f');
                    break;
                case 'n':
                    resultado.Append('\n');
                    break;
                case 'r':
                    resultado.Append('\r');
                    break;
                case 't':
                    resultado.Append('\t');
                    break;
                default:
                    // '"', '\\' y '/'.
                    resultado.Append(texto[i]);
                    break;
            }
        }

        return resultado.ToString();
    }
}
