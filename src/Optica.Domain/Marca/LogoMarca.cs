using System.Xml;

namespace Optica.Domain.Marca;

/// <summary>Los formatos de logo permitidos.</summary>
public enum FormatoLogo
{
    Png,
    Jpeg,
    Svg,
}

/// <summary>
/// El logo de la marca: un archivo <c>.png</c>, <c>.jpg</c> o <c>.svg</c> de 1 MB como máximo cuyo contenido
/// corresponde a su extensión (firma PNG o JPEG; para SVG, XML bien formado sin DTD con raíz <c>svg</c> del espacio de
/// nombres de SVG). Guarda una copia propia del contenido.
/// </summary>
public sealed class LogoMarca
{
    /// <summary>Tamaño máximo del logo: 1 MB (1.048.576 bytes). Exactamente 1 MB es válido.</summary>
    public const int TamanoMaximo = 1024 * 1024;

    private const string EspacioDeNombresSvg = "http://www.w3.org/2000/svg";

    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] FirmaJpeg = [0xFF, 0xD8, 0xFF];

    private readonly byte[] _contenido;

    private LogoMarca(FormatoLogo formato, byte[] contenido)
    {
        Formato = formato;
        _contenido = contenido;
    }

    public FormatoLogo Formato { get; }

    /// <summary>Los bytes del archivo, de solo lectura.</summary>
    public ReadOnlyMemory<byte> Contenido => _contenido;

    /// <summary>
    /// El formato según la extensión: exactamente <c>.png</c>, <c>.jpg</c> o <c>.svg</c>, en minúsculas. Cualquier
    /// otra (incluidas <c>.jpeg</c>, mayúsculas o un punto o espacio final) no tiene formato.
    /// </summary>
    public static FormatoLogo? FormatoDe(string? nombreArchivo)
    {
        if (string.IsNullOrEmpty(nombreArchivo))
        {
            return null;
        }

        var punto = nombreArchivo.LastIndexOf('.');
        if (punto < 0)
        {
            return null;
        }

        return nombreArchivo[punto..] switch
        {
            ".png" => FormatoLogo.Png,
            ".jpg" => FormatoLogo.Jpeg,
            ".svg" => FormatoLogo.Svg,
            _ => null,
        };
    }

    /// <summary>
    /// Crea el logo a partir del nombre del archivo y su contenido. Extensión no permitida, más de 1 MB o contenido que
    /// no corresponde al formato lanzan <see cref="MarcaInvalidaException"/>.
    /// </summary>
    public static LogoMarca Crear(string nombreArchivo, byte[] contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var formato = FormatoDe(nombreArchivo)
            ?? throw new MarcaInvalidaException("El logo no tiene una extensión permitida (.png, .jpg o .svg).");
        if (contenido.Length > TamanoMaximo)
        {
            throw new MarcaInvalidaException("El logo supera el tamaño máximo de 1 MB.");
        }

        if (!CorrespondeAlFormato(contenido, formato))
        {
            throw new MarcaInvalidaException("El contenido del logo no corresponde a una imagen del formato de su extensión.");
        }

        return new LogoMarca(formato, (byte[])contenido.Clone());
    }

    private static bool CorrespondeAlFormato(byte[] contenido, FormatoLogo formato) =>
        formato switch
        {
            FormatoLogo.Png => contenido.AsSpan().StartsWith(FirmaPng),
            FormatoLogo.Jpeg => contenido.AsSpan().StartsWith(FirmaJpeg),
            _ => EsSvg(contenido),
        };

    /// <summary>
    /// XML bien formado, sin DTD (no se procesan entidades ni recursos externos), cuya raíz es <c>svg</c> del espacio
    /// de nombres de SVG.
    /// </summary>
    private static bool EsSvg(byte[] contenido)
    {
        var opciones = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };
        try
        {
            using var lector = XmlReader.Create(new MemoryStream(contenido, writable: false), opciones);
            if (lector.MoveToContent() != XmlNodeType.Element
                || lector.LocalName != "svg"
                || lector.NamespaceURI != EspacioDeNombresSvg)
            {
                return false;
            }

            while (lector.Read())
            {
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
