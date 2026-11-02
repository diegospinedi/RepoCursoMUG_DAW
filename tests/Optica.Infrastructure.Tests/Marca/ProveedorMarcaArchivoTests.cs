using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Optica.Domain.Marca;
using Optica.Infrastructure.Marca;

namespace Optica.Infrastructure.Tests.Marca;

/// <summary>
/// El proveedor lee la marca instalada de su carpeta y, si falta o es inválida, la de ejemplo; nunca lee fuera de la
/// carpeta y nunca escribe rutas en el log ni en los mensajes. Cada prueba usa carpetas temporales propias.
/// </summary>
public sealed class ProveedorMarcaArchivoTests : IDisposable
{
    private static readonly byte[] PngInstalado = Png(0x11, 64);
    private static readonly byte[] SvgEjemplo = Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\"/></svg>");

    private static readonly byte[] SvgInstalado = Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\"><circle r=\"5\"/></svg>");

    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "optica-pruebas-marca", Guid.NewGuid().ToString("N"));
    private readonly RegistroCapturado _registro = new();

    /// <summary>Carpeta creada bajo el directorio actual del proceso por la prueba de carpeta relativa.</summary>
    private string? _carpetaRelativa;

    public ProveedorMarcaArchivoTests()
    {
        Directory.CreateDirectory(Ejemplo);
        File.WriteAllText(
            Path.Combine(Ejemplo, "branding.json"),
            "{ \"logo\": \"logo.svg\", \"colores\": { \"primario\": \"#5A5A5A\", \"fondo\": \"#F5F5F5\" } }");
        File.WriteAllBytes(Path.Combine(Ejemplo, "logo.svg"), SvgEjemplo);
        Directory.CreateDirectory(Instalada);
    }

    private string Ejemplo => Path.Combine(_raiz, "marca-ejemplo");

    private string Instalada => Path.Combine(_raiz, "instalada", "Marca");

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }

        if (_carpetaRelativa is not null && Directory.Exists(_carpetaRelativa))
        {
            Directory.Delete(_carpetaRelativa, recursive: true);
        }
    }

    [Fact]
    public void Instalada_Valida_DevuelveColoresYLogoDeLaOptica()
    {
        Instalar("logo.png", PngInstalado);

        var marca = Crear(Instalada).Obtener();

        Assert.False(marca.EsEjemplo);
        Assert.Equal("#0903A0", marca.ColorPrimario);
        Assert.Equal("#FFFFFF", marca.ColorFondo);
        Assert.Equal(FormatoLogo.Png, marca.Logo.Formato);
        Assert.Equal(PngInstalado, marca.Logo.Contenido.ToArray());
        Assert.DoesNotContain(_registro.Entradas, e => e.Nivel >= LogLevel.Warning);
    }

    [Fact]
    public void Instalada_ConElEsquemaReal_IgnoraLosCamposExtraYTomaSoloElNombreDelLogo()
    {
        // El branding.json de la marca real trae "logo": "Marca/logo.png" y campos que no se usan.
        EscribirBranding(
            "{\n  \"logo\": \"Marca/logo.png\",\n  \"colores\": { \"primario\": \"#0903A0\", \"fondo\": \"#FFFFFF\" },\n" +
            "  \"fuente\": \"texto libre\",\n  \"logoOriginal\": \"Marca/Marca.jpg\"\n}");
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        var marca = Crear(Instalada).Obtener();

        Assert.False(marca.EsEjemplo);
        Assert.Equal(PngInstalado, marca.Logo.Contenido.ToArray());
    }

    [Theory]
    [InlineData("Marca\\\\logo.png")]
    [InlineData("C:\\\\otra\\\\carpeta\\\\logo.png")]
    [InlineData("/otra/carpeta/logo.png")]
    public void Instalada_LogoConCarpeta_SeBuscaSoloElNombreDentroDeLaCarpeta(string campoLogo)
    {
        EscribirBranding(Branding(campoLogo));
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        var marca = Crear(Instalada).Obtener();

        Assert.False(marca.EsEjemplo);
        Assert.Equal(PngInstalado, marca.Logo.Contenido.ToArray());
    }

    [Fact]
    public void Instalada_BrandingConBom_SeLee()
    {
        var conBom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Branding("logo.png"))).ToArray();
        File.WriteAllBytes(Path.Combine(Instalada, "branding.json"), conBom);
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        Assert.False(Crear(Instalada).Obtener().EsEjemplo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinCarpetaConfigurada_UsaEjemploYAdvierte(string? directorio)
    {
        var marca = Crear(directorio).Obtener();

        AfirmarEjemplo(marca);
        Assert.Contains(
            _registro.Entradas,
            e => e.Nivel == LogLevel.Warning && e.Mensaje.Contains(ProveedorMarcaArchivo.ClaveDirectorio, StringComparison.Ordinal));
    }

    [Fact]
    public void CarpetaRelativa_AunqueExistaConMarcaValida_UsaEjemplo()
    {
        // La carpeta relativa existe respecto del directorio actual del proceso y tiene una marca válida: igual se
        // rechaza, porque una ruta relativa depende de desde dónde se arrancó la Api.
        var relativa = "optica-prueba-marca-relativa-" + Guid.NewGuid().ToString("N");
        _carpetaRelativa = Path.GetFullPath(relativa);
        Directory.CreateDirectory(_carpetaRelativa);
        File.WriteAllText(Path.Combine(_carpetaRelativa, "branding.json"), Branding("logo.png"));
        File.WriteAllBytes(Path.Combine(_carpetaRelativa, "logo.png"), PngInstalado);
        Assert.False(Crear(_carpetaRelativa).Obtener().EsEjemplo);

        AfirmarEjemploConAdvertencia(Crear(relativa));
    }

    [Fact]
    public void CarpetaConCaracterNulo_UsaEjemploYAdvierteSinMensajeNiRuta()
    {
        // Un valor anómalo en Optica:Marca:Directorio hace fallar a la resolución de la ruta: no puede ser un 500.
        var anomala = Path.Combine(_raiz, "carpeta-anomala\0x");
        var mensajeOriginal = Assert.ThrowsAny<Exception>(() => Path.GetFullPath(anomala)).Message;

        AfirmarEjemploConAdvertencia(Crear(anomala));

        var advertencia = Assert.Single(_registro.Entradas, e => e.Nivel == LogLevel.Warning);
        Assert.Contains(nameof(ArgumentException), advertencia.Mensaje, StringComparison.Ordinal);
        Assert.Contains(ProveedorMarcaArchivo.ClaveDirectorio, advertencia.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain(mensajeOriginal, advertencia.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain("carpeta-anomala", advertencia.Mensaje, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("secreto.png", true)]
    [InlineData("../secreto.png", false)]
    [InlineData("../../secreto.png", false)]
    [InlineData("sub/secreto.png", false)]
    [InlineData("../MarcaOtra/secreto.png", false)]
    [InlineData("../Marca/secreto.png", true)]
    public void EstaEnLaCarpeta_SoloAceptaArchivosDirectamenteDentro(string relativo, bool esperado)
    {
        var carpeta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Instalada));
        var ruta = Path.Combine(carpeta, relativo.Replace('/', Path.DirectorySeparatorChar));

        Assert.Equal(esperado, ProveedorMarcaArchivo.EstaEnLaCarpeta(carpeta, ruta));
    }

    [Fact]
    public void CarpetaInexistente_UsaEjemplo()
    {
        AfirmarEjemploConAdvertencia(Crear(Path.Combine(_raiz, "no-existe")));
    }

    [Fact]
    public void SinBranding_UsaEjemplo()
    {
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Theory]
    [InlineData("esto no es JSON")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{ \"logo\": 5, \"colores\": { \"primario\": \"#0903A0\", \"fondo\": \"#FFFFFF\" } }")]
    [InlineData("{ \"logo\": \"logo.png\", \"colores\": \"#0903A0\" }")]
    [InlineData("{ \"logo\": \"logo.png\" }")]
    [InlineData("{ \"logo\": \"logo.png\", \"colores\": { \"fondo\": \"#FFFFFF\" } }")]
    [InlineData("{ \"logo\": \"logo.png\", \"colores\": { \"primario\": 903, \"fondo\": \"#FFFFFF\" } }")]
    [InlineData("{ \"logo\": \"logo.png\", \"colores\": { \"primario\": \"#0903A0\", \"fondo\": \"#FFFFFF\" }, }")]
    [InlineData("{ /* comentario */ \"logo\": \"logo.png\", \"colores\": { \"primario\": \"#0903A0\", \"fondo\": \"#FFFFFF\" } }")]
    [InlineData("{ \"Logo\": \"logo.png\", \"Colores\": { \"Primario\": \"#0903A0\", \"Fondo\": \"#FFFFFF\" } }")]
    [InlineData("{ \"logo\": \"\\ud800.png\", \"colores\": { \"primario\": \"#0903A0\", \"fondo\": \"#FFFFFF\" } }")]
    // Propiedad repetida: no se elige una en silencio.
    [InlineData("{ \"logo\": \"logo.png\", \"colores\": { \"primario\": \"#0903A0\", \"primario\": \"#000000\", \"fondo\": \"#FFFFFF\" } }")]
    public void BrandingInvalido_UsaEjemplo(string branding)
    {
        EscribirBranding(branding);
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Fact]
    public void BrandingConUtf8Invalido_UsaEjemplo()
    {
        var bytes = Encoding.UTF8.GetBytes(Branding("logo.png")).ToList();
        bytes.InsertRange(bytes.IndexOf((byte)'l'), [0xC3, 0x28]);
        File.WriteAllBytes(Path.Combine(Instalada, "branding.json"), [.. bytes]);
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Fact]
    public void BrandingDeMasDe64KB_UsaEjemplo()
    {
        Assert.Equal(64 * 1024, ProveedorMarcaArchivo.TamanoMaximoBranding);
        var relleno = new string(' ', ProveedorMarcaArchivo.TamanoMaximoBranding);
        EscribirBranding(Branding("logo.png") + relleno);
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Theory]
    [InlineData("azul", "#FFFFFF")]
    [InlineData("#0903A0", "blanco")]
    [InlineData("#0903A", "#FFFFFF")]
    public void ColoresInvalidos_UsaEjemplo(string primario, string fondo)
    {
        EscribirBranding(Branding("logo.png", primario, fondo));
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
        // La advertencia no repite el valor recibido.
        Assert.DoesNotContain(_registro.Entradas, e => e.Mensaje.Contains(primario == "azul" ? "azul" : "blanco", StringComparison.Ordinal));
    }

    [Fact]
    public void LogoFaltante_UsaEjemplo()
    {
        EscribirBranding(Branding("logo.png"));

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Fact]
    public void LogoDe2MB_UsaEjemplo()
    {
        Instalar("logo.png", Png(0x22, 2 * 1024 * 1024));

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Fact]
    public void LogoDeExactamente1MB_SeUsa()
    {
        Instalar("logo.png", Png(0x22, LogoMarca.TamanoMaximo));

        Assert.False(Crear(Instalada).Obtener().EsEjemplo);
    }

    [Theory]
    [InlineData("logo.gif")]
    [InlineData("logo.jpeg")]
    [InlineData("logo.PNG")]
    [InlineData("optica.db")]
    public void LogoConExtensionNoPermitida_UsaEjemplo(string nombre)
    {
        Instalar(nombre, PngInstalado);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Fact]
    public void LogoQueNoEsImagen_UsaEjemplo()
    {
        Instalar("logo.png", Encoding.UTF8.GetBytes("no soy una imagen"));

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("Marca/")]
    [InlineData("lo\\u0001go.png")]
    public void LogoConNombreInvalido_UsaEjemplo(string campoLogo)
    {
        EscribirBranding(Branding(campoLogo));
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    /// <summary>
    /// El archivo con ese nombre existe de verdad (el sistema de archivos lo permite), así que solo la lista de
    /// caracteres prohibidos y la de nombres reservados lo dejan afuera: en Windows abrirían otra unidad, un flujo
    /// alternativo o un dispositivo.
    /// </summary>
    [TheoryConNombresNoValidosEnWindows]
    [InlineData("x:logo.png")]
    [InlineData("logo?.png")]
    [InlineData("logo<.png")]
    [InlineData("logo>.png")]
    [InlineData("logo|.png")]
    [InlineData("logo*.png")]
    [InlineData("logo\".png")]
    [InlineData("CON.png")]
    [InlineData("prn.png")]
    [InlineData("Aux.svg")]
    [InlineData("NUL.jpg")]
    [InlineData("nul.tar.png")]
    [InlineData("COM1.png")]
    [InlineData("com9.png")]
    [InlineData("LPT1.png")]
    [InlineData("lpt9.png")]
    [InlineData("CON .png")]
    public void LogoConNombreNoValidoEnWindows_AunqueExista_UsaEjemplo(string nombre)
    {
        var contenido = nombre.EndsWith(".svg", StringComparison.Ordinal) ? SvgInstalado
            : nombre.EndsWith(".jpg", StringComparison.Ordinal) ? Jpg
            : PngInstalado;
        EscribirBranding(Branding(JsonEncodedText.Encode(nombre).ToString()));
        File.WriteAllBytes(Path.Combine(Instalada, nombre), contenido);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Theory]
    [InlineData("CONSOLA.png")]
    [InlineData("COM10.png")]
    [InlineData("LPT0x.png")]
    [InlineData("auxiliar.png")]
    [InlineData("logo.con.png")]
    public void LogoConNombreParecidoAUnReservado_SeUsa(string nombre)
    {
        Instalar(nombre, PngInstalado);

        Assert.False(Crear(Instalada).Obtener().EsEjemplo);
    }

    /// <summary>
    /// Los nombres de dispositivo de Windows según "Naming Files, Paths, and Namespaces" de Microsoft: CON, PRN, AUX,
    /// NUL, COM0–COM9, LPT0–LPT9, COM¹–COM³, LPT¹–LPT³, CONIN$ y CONOUT$.
    /// </summary>
    private static readonly string[] NombresReservadosDeWindows =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³",
        "LPT¹", "LPT²", "LPT³",
        "CONIN$", "CONOUT$",
    ];

    /// <summary>Cada nombre reservado sin extensión, con extensión, en minúsculas y con espacio final.</summary>
    public static TheoryData<string> NombresDeLogoNoAceptables()
    {
        var datos = new TheoryData<string>();
        foreach (var reservado in NombresReservadosDeWindows)
        {
            datos.Add(reservado);
            datos.Add(reservado + ".png");
            datos.Add(reservado.ToLowerInvariant() + ".svg");
            datos.Add(reservado + " .jpg");
        }

        foreach (var prohibido in new[] { ':', '?', '<', '>', '|', '*', '"' })
        {
            datos.Add($"logo{prohibido}.png");
        }

        datos.Add("lo\u0001go.png");
        datos.Add("lo\u001fgo.png");
        datos.Add("lo\tgo.png");
        return datos;
    }

    /// <summary>
    /// La decisión pura sobre el nombre, sin tocar el sistema de archivos: corre igual en Windows, donde estos archivos
    /// no se pueden crear.
    /// </summary>
    [Theory]
    [MemberData(nameof(NombresDeLogoNoAceptables))]
    public void NombreDeArchivo_ReservadoProhibidoODeControl_NoSeAcepta(string nombre)
    {
        Assert.Null(ProveedorMarcaArchivo.NombreDeArchivo(nombre));
    }

    [Theory]
    [InlineData("CONSOLA.png")]
    [InlineData("COM10.png")]
    [InlineData("LPT10.png")]
    [InlineData("COM.png")]
    [InlineData("CONIN.png")]
    [InlineData("auxiliar.png")]
    [InlineData("logo.con.png")]
    [InlineData("logo.png")]
    public void NombreDeArchivo_ParecidoAUnReservado_SeAcepta(string nombre)
    {
        Assert.Equal(nombre, ProveedorMarcaArchivo.NombreDeArchivo(nombre));
    }

    [Theory]
    [InlineData("../secreto.png")]
    [InlineData("../../secreto.png")]
    [InlineData("..\\\\secreto.png")]
    public void LogoConPuntosPuntos_NuncaLeeFueraDeLaCarpeta(string campoLogo)
    {
        // El archivo existe un nivel y dos niveles arriba de la carpeta de marca, no dentro.
        var secreto = Png(0x5E, 128);
        File.WriteAllBytes(Path.Combine(Instalada, "..", "secreto.png"), secreto);
        File.WriteAllBytes(Path.Combine(Instalada, "..", "..", "secreto.png"), secreto);
        EscribirBranding(Branding(campoLogo));

        var marca = Crear(Instalada).Obtener();

        AfirmarEjemplo(marca);
        Assert.NotEqual(secreto, marca.Logo.Contenido.ToArray());
    }

    [Fact]
    public void LogoConPuntosPuntos_SoloUsaElArchivoDeLaCarpeta()
    {
        // Con el mismo nombre dentro de la carpeta, se usa ese: nunca el de afuera.
        File.WriteAllBytes(Path.Combine(Instalada, "..", "secreto.png"), Png(0x5E, 128));
        Instalar("secreto.png", PngInstalado, campoLogo: "../secreto.png");

        var marca = Crear(Instalada).Obtener();

        Assert.False(marca.EsEjemplo);
        Assert.Equal(PngInstalado, marca.Logo.Contenido.ToArray());
    }

    [Fact]
    public void LogoQueEsUnaCarpeta_UsaEjemplo()
    {
        EscribirBranding(Branding("logo.png"));
        Directory.CreateDirectory(Path.Combine(Instalada, "logo.png"));

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [FactConEnlacesSimbolicos]
    public void LogoQueEsEnlaceSimbolico_UsaEjemplo()
    {
        var afuera = Path.Combine(_raiz, "afuera.png");
        File.WriteAllBytes(afuera, Png(0x5E, 128));
        EscribirBranding(Branding("logo.png"));
        File.CreateSymbolicLink(Path.Combine(Instalada, "logo.png"), afuera);

        var marca = Crear(Instalada).Obtener();

        AfirmarEjemplo(marca);
        Assert.NotEqual(File.ReadAllBytes(afuera), marca.Logo.Contenido.ToArray());
    }

    [FactConEnlacesSimbolicos]
    public void BrandingQueEsEnlaceSimbolico_UsaEjemplo()
    {
        var afuera = Path.Combine(_raiz, "branding-afuera.json");
        File.WriteAllText(afuera, Branding("logo.png"));
        File.WriteAllBytes(Path.Combine(Instalada, "logo.png"), PngInstalado);
        File.CreateSymbolicLink(Path.Combine(Instalada, "branding.json"), afuera);

        AfirmarEjemploConAdvertencia(Crear(Instalada));
    }

    [Fact]
    public void EjemploIlegible_ConInstaladaInvalida_LanzaSinRutas()
    {
        Directory.Delete(Ejemplo, recursive: true);

        var error = Assert.Throws<MarcaEjemploIlegibleException>(() => Crear(Path.Combine(_raiz, "no-existe")).Obtener());

        Assert.Contains("marca de ejemplo", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_raiz, error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
        AfirmarSinRutas();
    }

    [Fact]
    public void EjemploInvalido_ConInstaladaInvalida_Lanza()
    {
        File.WriteAllText(
            Path.Combine(Ejemplo, "branding.json"),
            "{ \"logo\": \"logo.svg\", \"colores\": { \"primario\": \"gris\", \"fondo\": \"#F5F5F5\" } }");

        Assert.Throws<MarcaEjemploIlegibleException>(() => Crear(null).Obtener());
    }

    [Fact]
    public void EjemploIlegible_ConInstaladaValida_NoLoNecesita()
    {
        Directory.Delete(Ejemplo, recursive: true);
        Instalar("logo.png", PngInstalado);

        Assert.False(Crear(Instalada).Obtener().EsEjemplo);
    }

    [Fact]
    public void Obtener_LeeUnaSolaVezYAdvierteUnaSolaVez()
    {
        var proveedor = Crear(Path.Combine(_raiz, "no-existe"));

        var primera = proveedor.Obtener();
        // Lo que cambie en disco después no se lee hasta reiniciar la Api.
        File.WriteAllText(
            Path.Combine(Ejemplo, "branding.json"),
            "{ \"logo\": \"logo.svg\", \"colores\": { \"primario\": \"#000000\", \"fondo\": \"#000000\" } }");
        var segunda = proveedor.Obtener();

        Assert.Same(primera, segunda);
        Assert.Single(_registro.Entradas, e => e.Nivel == LogLevel.Warning);
    }

    [Fact]
    public void Obtener_ConcurrenteDevuelveLaMismaMarca()
    {
        Instalar("logo.png", PngInstalado);
        var proveedor = Crear(Instalada);

        var marcas = Enumerable.Range(0, 16).AsParallel().Select(_ => proveedor.Obtener()).ToList();

        Assert.All(marcas, m => Assert.Same(marcas[0], m));
    }

    [Fact]
    public void Obtener_UnaFallaNoQuedaGuardada()
    {
        var respaldo = Path.Combine(_raiz, "respaldo");
        Directory.Move(Ejemplo, respaldo);
        var proveedor = Crear(null);
        Assert.Throws<MarcaEjemploIlegibleException>(() => proveedor.Obtener());

        Directory.Move(respaldo, Ejemplo);

        AfirmarEjemplo(proveedor.Obtener());
    }

    private ProveedorMarcaArchivo Crear(string? directorio) =>
        new(directorio, Ejemplo, new Registrador<ProveedorMarcaArchivo>(_registro.CreateLogger(nameof(ProveedorMarcaArchivo))));

    private void Instalar(string nombreLogo, byte[] contenido, string? campoLogo = null)
    {
        EscribirBranding(Branding(campoLogo ?? nombreLogo));
        File.WriteAllBytes(Path.Combine(Instalada, nombreLogo), contenido);
    }

    private void EscribirBranding(string json) => File.WriteAllText(Path.Combine(Instalada, "branding.json"), json);

    private static string Branding(string logo, string primario = "#0903A0", string fondo = "#FFFFFF") =>
        $"{{ \"logo\": \"{logo}\", \"colores\": {{ \"primario\": \"{primario}\", \"fondo\": \"{fondo}\" }} }}";

    private static byte[] Png(byte relleno, int largo)
    {
        var contenido = Enumerable.Repeat(relleno, largo).ToArray();
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(contenido, 0);
        return contenido;
    }

    private static void AfirmarEjemplo(IdentidadVisual marca)
    {
        Assert.True(marca.EsEjemplo);
        Assert.Equal("#5A5A5A", marca.ColorPrimario);
        Assert.Equal("#F5F5F5", marca.ColorFondo);
        Assert.Equal(FormatoLogo.Svg, marca.Logo.Formato);
        Assert.Equal(SvgEjemplo, marca.Logo.Contenido.ToArray());
    }

    private void AfirmarEjemploConAdvertencia(ProveedorMarcaArchivo proveedor)
    {
        AfirmarEjemplo(proveedor.Obtener());
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Warning);
        AfirmarSinRutas();
    }

    /// <summary>Ni la carpeta temporal (que en una PC estaría bajo el perfil del usuario) ni la del usuario.</summary>
    private void AfirmarSinRutas()
    {
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var entrada in _registro.Entradas)
        {
            Assert.DoesNotContain(_raiz, entrada.Mensaje, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Path.GetTempPath().TrimEnd('/', '\\'), entrada.Mensaje, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(perfil))
            {
                Assert.DoesNotContain(perfil, entrada.Mensaje, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Null(entrada.Excepcion);
        }
    }

    private sealed record EntradaRegistro(LogLevel Nivel, string Mensaje, Exception? Excepcion);

    private sealed class RegistroCapturado : ILoggerProvider
    {
        private readonly ConcurrentQueue<EntradaRegistro> _entradas = new();

        public IReadOnlyList<EntradaRegistro> Entradas => _entradas.ToArray();

        public ILogger CreateLogger(string categoryName) => new Registrador(_entradas);

        public void Dispose()
        {
        }

        private sealed class Registrador(ConcurrentQueue<EntradaRegistro> entradas) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var propiedades = state is IEnumerable<KeyValuePair<string, object?>> pares
                    ? string.Join("; ", pares.Select(p => $"{p.Key}={p.Value}"))
                    : string.Empty;
                entradas.Enqueue(new EntradaRegistro(logLevel, formatter(state, exception) + "\n" + propiedades, exception));
            }
        }
    }

    /// <summary>Adapta el registrador capturado a <see cref="ILogger{T}"/>.</summary>
    private sealed class Registrador<T>(ILogger interno) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => interno.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => interno.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            interno.Log(logLevel, eventId, state, exception, formatter);
    }
}
