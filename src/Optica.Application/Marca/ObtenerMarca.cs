using Optica.Domain.Marca;

namespace Optica.Application.Marca;

/// <summary>Colores de la marca y si es la de ejemplo. No contiene rutas del sistema de archivos.</summary>
public sealed record MarcaVisible(string Primario, string Fondo, bool EsEjemplo);

/// <summary>
/// El logo listo para servir: una copia de su contenido, el tipo de contenido (<c>image/png</c>, <c>image/jpeg</c> o
/// <c>image/svg+xml</c>) y si es SVG (se sirve aislado, R-10).
/// </summary>
public sealed record LogoVisible(byte[] Contenido, string TipoContenido, bool EsSvg);

/// <summary>
/// Entrega la marca vigente (FR-06): los colores y el logo de la marca instalada o, si falta o es inválida, los de la
/// marca de ejemplo (FR-07). La elección la hace <see cref="IProveedorMarca"/>.
/// </summary>
public sealed class ObtenerMarca(IProveedorMarca proveedor)
{
    public const string TipoPng = "image/png";
    public const string TipoJpeg = "image/jpeg";
    public const string TipoSvg = "image/svg+xml";

    public MarcaVisible Ejecutar()
    {
        var marca = proveedor.Obtener();
        return new MarcaVisible(marca.ColorPrimario, marca.ColorFondo, marca.EsEjemplo);
    }

    public LogoVisible ObtenerLogo()
    {
        var logo = proveedor.Obtener().Logo;
        var tipo = logo.Formato switch
        {
            FormatoLogo.Png => TipoPng,
            FormatoLogo.Jpeg => TipoJpeg,
            FormatoLogo.Svg => TipoSvg,
            _ => throw new InvalidOperationException($"Formato de logo no previsto: {(int)logo.Formato}."),
        };
        return new LogoVisible(logo.Contenido.ToArray(), tipo, logo.Formato == FormatoLogo.Svg);
    }
}
