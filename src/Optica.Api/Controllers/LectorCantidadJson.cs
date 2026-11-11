using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Optica.Api.Controllers;

/// <summary>
/// Lee la <c>cantidad</c> de una línea del presupuesto (D-01, D-07). Un entero JSON escrito con dígitos que entra en un
/// <c>int</c> se entrega tal cual. Uno que no entra se satura: positivo, <see cref="int.MaxValue"/>; negativo,
/// <see cref="int.MinValue"/>. Así el caso de uso lo rechaza junto al campo con el texto que corresponde ("Usá hasta
/// 9.999 unidades." o el de AC-02) y no termina como un cuerpo ilegible. <c>null</c> es una cantidad ausente.
/// </summary>
/// <remarks>
/// Cualquier otro valor (un decimal como 2.5, un exponente como 1e3, un texto, un arreglo, un objeto) lanza
/// <see cref="JsonException"/> sin mensaje propio ni causa: es un cuerpo ilegible en <c>lineas[i].cantidad</c> y el
/// lector de MVC lo registra sin el valor (R-04). El valor saturado no se registra ni se devuelve: solo decide el texto.
/// </remarks>
internal sealed class LectorCantidadJson : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(ref Utf8JsonReader lector, Type tipo, JsonSerializerOptions opciones)
    {
        if (lector.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (lector.TokenType != JsonTokenType.Number)
        {
            throw new JsonException();
        }

        if (lector.TryGetInt32(out var cantidad))
        {
            return cantidad;
        }

        var crudo = lector.HasValueSequence ? lector.ValueSequence.ToArray() : lector.ValueSpan.ToArray();
        var negativo = crudo.Length > 0 && crudo[0] == (byte)'-';
        var digitos = negativo ? crudo.AsSpan(1) : crudo.AsSpan();
        if (digitos.Length == 0 || digitos.ContainsAnyExceptInRange((byte)'0', (byte)'9'))
        {
            throw new JsonException();
        }

        return negativo ? int.MinValue : int.MaxValue;
    }

    public override void Write(Utf8JsonWriter escritor, int? valor, JsonSerializerOptions opciones)
    {
        ArgumentNullException.ThrowIfNull(escritor);
        if (valor is { } cantidad)
        {
            escritor.WriteNumberValue(cantidad);
        }
        else
        {
            escritor.WriteNullValue();
        }
    }
}
