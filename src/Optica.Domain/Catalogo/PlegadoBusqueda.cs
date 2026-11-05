using System.Globalization;
using System.Text;

namespace Optica.Domain.Catalogo;

/// <summary>
/// La única regla para comparar textos en la búsqueda de artículos (FR-04, D-04) y para ordenarlos por descripción
/// (D-15). La base la usa a través de funciones registradas en cada conexión (ADR-011).
/// </summary>
public static class PlegadoBusqueda
{
    // Con escapes y no con el carácter: una normalización del archivo no los cambia.
    private const char Virgulilla = '\U00000303';
    private const string Enie = "\U000000F1";

    // "{" (U+007B) ordena en binario después de cualquier letra ASCII minúscula, así "n{" queda entre "nz" y "o".
    private const string EnieEnOrden = "n{";

    /// <summary>
    /// El texto plegado: recortado como en D-17 de FEAT-002a, sin ninguna marca de acento (aguda, grave, diéresis,
    /// circunflejo, virgulilla y demás) salvo la virgulilla que sigue a una "n" o "N", en forma NFC, en minúsculas
    /// invariantes y recortado otra vez, así un texto hecho solo de marcas queda vacío. "Armazón" da "armazon", "Año"
    /// da "año" y "São" da "sao"; las letras que no se descomponen (ł, ø, æ, ß) quedan como están (D-04). Nunca
    /// lanza: con UTF-16 inválido devuelve el texto recortado en minúsculas, sin normalizar (ADR-011).
    /// </summary>
    public static string Plegar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);

        var recortado = NormalizacionCodigo.Recortar(texto);
        if (!NormalizacionCodigo.EsUtf16Valido(recortado))
        {
            return recortado.ToLowerInvariant();
        }

        // Con UTF-16 válido Normalize no lanza.
        var plegado = QuitarAcentos(recortado.Normalize(NormalizationForm.FormD))
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();
        return NormalizacionCodigo.Recortar(plegado);
    }

    /// <summary>
    /// La clave con la que se ordena por descripción: <see cref="Plegar"/> con cada "ñ" escrita como "n{", que en
    /// orden binario va después de "n" seguida de cualquier letra ASCII y antes de "o": "nube" &lt; "ñandú" &lt; "ojo"
    /// (D-15). Las demás letras que no son ASCII ordenan después de la z.
    /// </summary>
    public static string ClaveOrden(string texto) =>
        Plegar(texto).Replace(Enie, EnieEnOrden, StringComparison.Ordinal);

    // Precondición: el texto es UTF-16 válido y está en NFD.
    private static string QuitarAcentos(string descompuesto)
    {
        var resultado = new StringBuilder(descompuesto.Length);
        foreach (var caracter in descompuesto.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(caracter) == UnicodeCategory.NonSpacingMark && !EsVirgulillaDeEnie(caracter, resultado))
            {
                continue;
            }

            resultado.Append(caracter.ToString());
        }

        return resultado.ToString();
    }

    // La virgulilla se conserva solo si lo último que quedó es una "n" o "N": NFC la recompone como "ñ".
    private static bool EsVirgulillaDeEnie(Rune marca, StringBuilder anterior) =>
        marca.Value == Virgulilla
        && anterior.Length > 0
        && anterior[^1] is ('n' or 'N');
}
