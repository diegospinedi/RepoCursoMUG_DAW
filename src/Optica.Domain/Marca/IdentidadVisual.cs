using System.Diagnostics.CodeAnalysis;

namespace Optica.Domain.Marca;

/// <summary>
/// La identidad visual que muestra la aplicación: color primario, color de fondo, logo y si es la marca de ejemplo
/// (no la de la óptica). No se llama <c>Marca</c> para no chocar con el espacio de nombres
/// <c>Optica.Domain.Marca</c>. Solo existe con colores <c>#RRGGBB</c> y un logo válido.
/// </summary>
public sealed class IdentidadVisual
{
    private IdentidadVisual(string colorPrimario, string colorFondo, LogoMarca logo, bool esEjemplo)
    {
        ColorPrimario = colorPrimario;
        ColorFondo = colorFondo;
        Logo = logo;
        EsEjemplo = esEjemplo;
    }

    public string ColorPrimario { get; }

    public string ColorFondo { get; }

    public LogoMarca Logo { get; }

    /// <summary>Verdadero si es la marca de ejemplo del repositorio y no la instalada de la óptica.</summary>
    public bool EsEjemplo { get; }

    /// <summary>
    /// Crea la identidad visual. Un color que no tiene el formato <c>#RRGGBB</c> lanza
    /// <see cref="MarcaInvalidaException"/>; el mensaje nombra la regla y nunca repite el valor recibido.
    /// </summary>
    public static IdentidadVisual Crear(string? colorPrimario, string? colorFondo, LogoMarca logo, bool esEjemplo)
    {
        ArgumentNullException.ThrowIfNull(logo);
        if (!EsColorValido(colorPrimario))
        {
            throw new MarcaInvalidaException("El color primario no tiene el formato #RRGGBB.");
        }

        if (!EsColorValido(colorFondo))
        {
            throw new MarcaInvalidaException("El color de fondo no tiene el formato #RRGGBB.");
        }

        return new IdentidadVisual(colorPrimario, colorFondo, logo, esEjemplo);
    }

    /// <summary>
    /// Verdadero solo para <c>#</c> seguido de exactamente seis dígitos hexadecimales (<c>^#[0-9A-Fa-f]{6}$</c>), sin
    /// espacios ni saltos de línea antes o después.
    /// </summary>
    public static bool EsColorValido([NotNullWhen(true)] string? color)
    {
        if (color is not { Length: 7 } || color[0] != '#')
        {
            return false;
        }

        foreach (var caracter in color.AsSpan(1))
        {
            if (!char.IsAsciiHexDigit(caracter))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// La marca no cumple una regla del dominio. El mensaje explica la regla incumplida sin copiar el valor recibido ni
/// rutas del sistema de archivos.
/// </summary>
public sealed class MarcaInvalidaException(string mensaje) : Exception(mensaje);
