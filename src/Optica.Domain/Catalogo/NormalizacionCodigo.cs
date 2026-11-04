using System.Text;

namespace Optica.Domain.Catalogo;

/// <summary>
/// La única regla para comparar códigos en el proveedor (FR-02, ADR-009) y para recortar los textos del artículo
/// (D-13, D-17).
/// </summary>
public static class NormalizacionCodigo
{
    private const char MarcaDeOrdenDeBytes = '\uFEFF';

    /// <summary>
    /// Quita del principio y del final todo carácter para el que <see cref="char.IsWhiteSpace(char)"/> es verdadero
    /// (espacio común, espacio duro, tabulador, U+0085 y demás, D-13) y también U+FEFF, la marca de orden de bytes
    /// que suele llegar pegada desde una planilla (D-17); conserva los internos. Un texto hecho solo de esos
    /// caracteres queda vacío.
    /// </summary>
    public static string Recortar(string texto)
    {
        var inicio = 0;
        while (inicio < texto.Length && EsRecortable(texto[inicio]))
        {
            inicio++;
        }

        var fin = texto.Length;
        while (fin > inicio && EsRecortable(texto[fin - 1]))
        {
            fin--;
        }

        return texto[inicio..fin];
    }

    /// <summary>
    /// La clave con la que se comparan dos códigos: recortado, en forma NFC y en mayúsculas invariantes, así
    /// " abc-1 " y "ABC-1" dan la misma clave (AC-02).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El texto no es UTF-16 válido. <see cref="PoliticaArticulo"/> lo rechaza antes (D-16), así que una entidad
    /// válida nunca llega a este caso.
    /// </exception>
    public static string Clave(string texto) =>
        Recortar(texto).Normalize(NormalizationForm.FormC).ToUpperInvariant();

    /// <summary>
    /// Verdadero si el texto es UTF-16 válido: cada sustituto alto va seguido de uno bajo y ningún sustituto bajo
    /// aparece suelto (D-16).
    /// </summary>
    public static bool EsUtf16Valido(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            if (char.IsHighSurrogate(texto[i]))
            {
                if (i + 1 >= texto.Length || !char.IsLowSurrogate(texto[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(texto[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EsRecortable(char caracter) => char.IsWhiteSpace(caracter) || caracter == MarcaDeOrdenDeBytes;
}
