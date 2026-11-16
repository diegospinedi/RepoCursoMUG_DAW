namespace Optica.Domain.Configuracion;

/// <summary>
/// La Clave Única de Identificación Tributaria (FR-03 de FEAT-009, D-12): 11 dígitos ASCII cuyo último es el
/// verificador de módulo 11 de los 10 anteriores. <see cref="Valor"/> siempre está en forma canónica, sin guiones. Es la
/// única implementación del módulo 11: también la usa la validación de <c>Arca:Cuit</c> (D-18).
/// </summary>
/// <remarks>
/// Sin <c>ToString</c> con el valor, como el resto de los datos del emisor (R-06): el CUIT puede ser de una persona
/// física. Quien lo necesite lo pide por <see cref="Valor"/>.
/// </remarks>
public sealed class Cuit : IEquatable<Cuit>
{
    /// <summary>Cantidad de dígitos del CUIT, contando el verificador.</summary>
    public const int Largo = 11;

    private const char Guion = '-';

    // Pesos del dígito verificador (módulo 11), de izquierda a derecha sobre los 10 primeros dígitos.
    private static readonly int[] Pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    private Cuit(string valor)
    {
        Valor = valor;
    }

    /// <summary>Los 11 dígitos, sin guiones: la forma en que se guarda y se envía.</summary>
    public string Valor { get; }

    /// <summary>
    /// El CUIT de un texto escrito por la operadora: se recortan del principio y del final los espacios y la marca de
    /// orden de bytes (<see cref="RecorteTexto.Recortar"/>, el mismo recorte del resto de los datos del emisor), se admiten
    /// guiones en cualquier posición y se quitan ("20-12345678-6" da 20123456786, AC-03). Null si queda vacío, si hay
    /// otro carácter (espacios internos, puntos, letras, dígitos no ASCII), si no son 11 dígitos o si el verificador no
    /// corresponde, incluido el verificador 10, que ningún dígito representa (AC-04). Nunca lanza.
    /// </summary>
    public static Cuit? Interpretar(string? texto)
    {
        if (texto is null)
        {
            return null;
        }

        var sinGuiones = RecorteTexto.Recortar(texto).Replace(Guion.ToString(), string.Empty, StringComparison.Ordinal);
        return EsValidoCanonico(sinGuiones) ? new Cuit(sinGuiones) : null;
    }

    /// <summary>
    /// Verdadero si el texto es exactamente un CUIT válido en forma canónica: 11 dígitos ASCII, sin guiones ni espacios,
    /// con el verificador de módulo 11 (un verificador 10 es inválido). Nunca lanza.
    /// </summary>
    public static bool EsValidoCanonico(string? texto)
    {
        if (texto is null || texto.Length != Largo)
        {
            return false;
        }

        var suma = 0;
        for (var i = 0; i < Largo; i++)
        {
            if (!char.IsAsciiDigit(texto[i]))
            {
                return false;
            }

            if (i < Pesos.Length)
            {
                suma += (texto[i] - '0') * Pesos[i];
            }
        }

        var verificador = 11 - (suma % 11);
        if (verificador == 11)
        {
            verificador = 0;
        }

        return verificador != 10 && verificador == texto[Largo - 1] - '0';
    }

    public bool Equals(Cuit? other) => other is not null && string.Equals(Valor, other.Valor, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Cuit);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Valor);
}

/// <summary>
/// El recorte de los textos de los datos del emisor y del CUIT: uno solo, así un dato pegado desde otro programa se
/// recorta igual en todos los campos. Propio de este módulo: no usa tipos de Catálogo.
/// </summary>
internal static class RecorteTexto
{
    private const char MarcaDeOrdenDeBytes = '\uFEFF';

    /// <summary>
    /// Quita del principio y del final los espacios (<see cref="char.IsWhiteSpace(char)"/>) y la marca de orden de bytes
    /// U+FEFF, que suele llegar pegada desde otro programa; conserva los internos.
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

    private static bool EsRecortable(char caracter) => char.IsWhiteSpace(caracter) || caracter == MarcaDeOrdenDeBytes;
}
