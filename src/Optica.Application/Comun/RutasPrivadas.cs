using System.Text;

namespace Optica.Application.Comun;

/// <summary>
/// Quita del texto que va al log la carpeta del usuario de la PC (por ejemplo <c>C:\Users\nombre</c>), que lleva su
/// nombre: la reemplaza por <c>%USERPROFILE%</c> en Windows y por <c>~</c> en los demás sistemas. Está en Application
/// porque la usan la Api (errores no controlados) y la infraestructura (directorio de datos visible), y ninguna de las
/// dos puede depender de la otra.
/// </summary>
public static class RutasPrivadas
{
    public static string Ocultar(string texto) =>
        Ocultar(
            texto,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OperatingSystem.IsWindows());

    /// <summary>
    /// Reemplaza cada aparición de la carpeta del usuario que termina en un límite de segmento: con perfil
    /// <c>/home/maria</c>, <c>/home/maria/datos</c> se oculta y <c>/home/mariana/datos</c> no se toca.
    /// </summary>
    /// <param name="perfil">La carpeta del usuario; vacía si el sistema no la informa.</param>
    /// <param name="windows">
    /// En Windows se compara sin distinguir mayúsculas, <c>/</c> y <c>\</c> valen lo mismo y se usa
    /// <c>%USERPROFILE%</c>.
    /// </param>
    public static string Ocultar(string texto, string perfil, bool windows)
    {
        var carpeta = perfil.TrimEnd('/', '\\');
        if (carpeta.Length == 0)
        {
            return texto;
        }

        var reemplazo = windows ? "%USERPROFILE%" : "~";
        var resultado = new StringBuilder(texto.Length);
        var i = 0;
        while (i < texto.Length)
        {
            if (Coincide(texto, i, carpeta, windows) && !ContinuaElSegmento(texto, i + carpeta.Length))
            {
                resultado.Append(reemplazo);
                i += carpeta.Length;
            }
            else
            {
                resultado.Append(texto[i]);
                i++;
            }
        }

        return resultado.ToString();
    }

    private static bool Coincide(string texto, int inicio, string carpeta, bool windows)
    {
        if (inicio + carpeta.Length > texto.Length)
        {
            return false;
        }

        for (var j = 0; j < carpeta.Length; j++)
        {
            var leido = texto[inicio + j];
            var esperado = carpeta[j];
            var igual = leido == esperado
                || (windows
                    && ((EsSeparador(leido) && EsSeparador(esperado))
                        || char.ToUpperInvariant(leido) == char.ToUpperInvariant(esperado)));
            if (!igual)
            {
                return false;
            }
        }

        return true;
    }

    private static bool EsSeparador(char caracter) => caracter is '/' or '\\';

    /// <summary>
    /// Verdadero si el carácter en <paramref name="posicion"/> sigue el nombre de la carpeta (por ejemplo, la "na" de
    /// "mariana"): una letra, un dígito, <c>_</c> o <c>-</c>, o un punto seguido de uno de ellos. Un punto que cierra
    /// la oración no sigue el nombre.
    /// </summary>
    private static bool ContinuaElSegmento(string texto, int posicion)
    {
        if (posicion >= texto.Length)
        {
            return false;
        }

        var caracter = texto[posicion];
        return char.IsLetterOrDigit(caracter) || caracter is '_' or '-'
            || (caracter == '.' && ContinuaElSegmento(texto, posicion + 1));
    }
}
