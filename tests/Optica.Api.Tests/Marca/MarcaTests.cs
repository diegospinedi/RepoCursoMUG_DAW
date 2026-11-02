using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Optica.Application.Marca;
using Optica.Infrastructure.Marca;

namespace Optica.Api.Tests.Marca;

/// <summary>
/// <c>GET /api/marca</c> y <c>GET /api/marca/logo</c>: la marca instalada en <c>Optica:Marca:Directorio</c> o, si
/// falta o es inválida, la de ejemplo que se copia junto a la Api desde <c>marca-ejemplo/</c>.
/// </summary>
public sealed class MarcaTests : IDisposable
{
    /// <summary>Colores de <c>marca-ejemplo/branding.json</c>.</summary>
    private const string PrimarioEjemplo = "#5A5A5A";
    private const string FondoEjemplo = "#F5F5F5";

    private static readonly byte[] PngOptica = Png(0x11, 256);

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "optica-pruebas-api-marca", Guid.NewGuid().ToString("N"));

    public MarcaTests()
    {
        Directory.CreateDirectory(CarpetaMarca);
    }

    /// <summary>La carpeta de marca de la prueba, dentro de una carpeta propia para poder dejar archivos "afuera".</summary>
    private string CarpetaMarca => Path.Combine(_raiz, "instalacion", "Marca");

    private static string LogoEjemplo => Path.Combine(AppContext.BaseDirectory, ProveedorMarcaArchivo.CarpetaEjemplo, "logo.svg");

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    [Fact]
    public async Task Marca_Instalada_DevuelveColoresYLogoDeLaOptica()
    {
        Instalar(Branding("logo.png", "#0903A0", "#FFFFFF"), ("logo.png", PngOptica));
        using var fabrica = new FabricaApi();
        var cliente = ClienteConMarca(fabrica, CarpetaMarca);

        var marca = await Http.JsonAsync(await Http.GetAsync(cliente, "/api/marca"));
        var logo = await Http.GetAsync(cliente, "/api/marca/logo");

        Assert.Equal("#0903A0", marca.GetProperty("primario").GetString());
        Assert.Equal("#FFFFFF", marca.GetProperty("fondo").GetString());
        Assert.False(marca.GetProperty("esEjemplo").GetBoolean());
        // Solo esos tres campos: ninguna ruta ni nombre de archivo en la respuesta.
        Assert.Equal(3, marca.EnumerateObject().Count());
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PngOptica, await logo.Content.ReadAsByteArrayAsync());
        Assert.Contains("nosniff", logo.Headers.GetValues("X-Content-Type-Options"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Marca_CarpetaFaltante_UsaEjemplo(bool configuradaPeroInexistente)
    {
        using var fabrica = new FabricaApi();
        var cliente = configuradaPeroInexistente
            ? ClienteConMarca(fabrica, Path.Combine(_raiz, "no-existe"))
            : fabrica.CrearClienteSinCookies();

        await AfirmarEjemploAsync(cliente);
        AfirmarAdvertenciaSinRutas(fabrica);
    }

    [Fact]
    public async Task Marca_ColoresInvalidos_UsaEjemplo()
    {
        Instalar(Branding("logo.png", "azul", "#FFFFFF"), ("logo.png", PngOptica));
        using var fabrica = new FabricaApi();

        await AfirmarEjemploAsync(ClienteConMarca(fabrica, CarpetaMarca));
        AfirmarAdvertenciaSinRutas(fabrica);
        Assert.DoesNotContain(fabrica.Registro.Lineas, l => l.TextoCompleto.Contains("azul", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Marca_LogoFaltanteOGrande_UsaEjemplo(bool grande)
    {
        if (grande)
        {
            Instalar(Branding("logo.png", "#0903A0", "#FFFFFF"), ("logo.png", Png(0x22, 2 * 1024 * 1024)));
        }
        else
        {
            Instalar(Branding("logo.png", "#0903A0", "#FFFFFF"));
        }

        using var fabrica = new FabricaApi();

        await AfirmarEjemploAsync(ClienteConMarca(fabrica, CarpetaMarca));
        AfirmarAdvertenciaSinRutas(fabrica);
    }

    [Fact]
    public async Task Marca_LogoConPuntosPuntos_RechazadoFueraDeCarpeta()
    {
        // secreto.png existe al lado de la carpeta de marca, no dentro.
        var secreto = Png(0x5E, 512);
        File.WriteAllBytes(Path.Combine(CarpetaMarca, "..", "secreto.png"), secreto);
        Instalar(Branding("../secreto.png", "#0903A0", "#FFFFFF"));
        using var fabrica = new FabricaApi();
        var cliente = ClienteConMarca(fabrica, CarpetaMarca);

        var logo = await Http.GetAsync(cliente, "/api/marca/logo");

        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        var servido = await logo.Content.ReadAsByteArrayAsync();
        Assert.NotEqual(secreto, servido);
        Assert.Equal(await File.ReadAllBytesAsync(LogoEjemplo), servido);
        await AfirmarEjemploAsync(cliente);
    }

    [Fact]
    public async Task Marca_EjemploIlegible_Error500()
    {
        var ejemploInexistente = Path.Combine(_raiz, "marca-ejemplo-borrada");
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IProveedorMarca>();
            servicios.AddSingleton<IProveedorMarca>(proveedor => new ProveedorMarcaArchivo(
                null, ejemploInexistente, proveedor.GetRequiredService<ILogger<ProveedorMarcaArchivo>>()));
        });
        var cliente = fabrica.CrearClienteSinCookies();

        var marca = await Http.GetAsync(cliente, "/api/marca");
        var logo = await Http.GetAsync(cliente, "/api/marca/logo");

        Assert.Equal(HttpStatusCode.InternalServerError, marca.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, logo.StatusCode);
        var cuerpo = await marca.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_raiz, cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(MarcaEjemploIlegibleException), cuerpo, StringComparison.Ordinal);
        var lineas = fabrica.Registro.Lineas;
        Assert.Contains(
            lineas,
            l => l.Nivel == LogLevel.Error
                && l.Mensaje.Contains(nameof(MarcaEjemploIlegibleException), StringComparison.Ordinal)
                && l.Mensaje.Contains("/api/marca", StringComparison.Ordinal));
        Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(_raiz, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lineas, l => l.Nivel > LogLevel.Debug && l.Excepcion is not null);
    }

    [Fact]
    public async Task MarcaLogo_Svg_ConCspSandbox()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var logo = await Http.GetAsync(cliente, "/api/marca/logo");

        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/svg+xml", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; sandbox",
            Assert.Single(logo.Headers.GetValues("Content-Security-Policy")));
        Assert.Contains("nosniff", logo.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(await File.ReadAllBytesAsync(LogoEjemplo), await logo.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task MarcaLogo_Png_SinCspSandbox()
    {
        Instalar(Branding("logo.png", "#0903A0", "#FFFFFF"), ("logo.png", PngOptica));
        using var fabrica = new FabricaApi();

        var logo = await Http.GetAsync(ClienteConMarca(fabrica, CarpetaMarca), "/api/marca/logo");

        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.False(logo.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Marca_Anonima_SinSesionResponde200()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var marca = await Http.GetAsync(cliente, "/api/marca");
        var logo = await Http.GetAsync(cliente, "/api/marca/logo");

        Assert.Equal(HttpStatusCode.OK, marca.StatusCode);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        // Sin sesión no se emite ninguna cookie.
        Assert.Null(Http.SetCookie(marca));
        Assert.Null(Http.SetCookie(logo));
    }

    [Fact]
    public void MarcaDeEjemplo_SeCopiaJuntoALaApi()
    {
        var carpeta = Path.Combine(AppContext.BaseDirectory, ProveedorMarcaArchivo.CarpetaEjemplo);

        Assert.True(File.Exists(Path.Combine(carpeta, ProveedorMarcaArchivo.ArchivoBranding)));
        Assert.True(File.Exists(Path.Combine(carpeta, "logo.svg")));
    }

    private static HttpClient ClienteConMarca(FabricaApi fabrica, string carpeta) =>
        fabrica
            .WithWebHostBuilder(constructor => constructor.UseSetting(ProveedorMarcaArchivo.ClaveDirectorio, carpeta))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = false,
                AllowAutoRedirect = false,
            });

    private void Instalar(string branding, params (string Nombre, byte[] Contenido)[] archivos)
    {
        File.WriteAllText(Path.Combine(CarpetaMarca, ProveedorMarcaArchivo.ArchivoBranding), branding);
        foreach (var (nombre, contenido) in archivos)
        {
            File.WriteAllBytes(Path.Combine(CarpetaMarca, nombre), contenido);
        }
    }

    private static string Branding(string logo, string primario, string fondo) =>
        $"{{ \"logo\": \"{logo}\", \"colores\": {{ \"primario\": \"{primario}\", \"fondo\": \"{fondo}\" }} }}";

    private static byte[] Png(byte relleno, int largo)
    {
        var contenido = Enumerable.Repeat(relleno, largo).ToArray();
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(contenido, 0);
        return contenido;
    }

    private static async Task AfirmarEjemploAsync(HttpClient cliente)
    {
        var respuesta = await Http.GetAsync(cliente, "/api/marca");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var marca = await Http.JsonAsync(respuesta);
        Assert.Equal(PrimarioEjemplo, marca.GetProperty("primario").GetString());
        Assert.Equal(FondoEjemplo, marca.GetProperty("fondo").GetString());
        Assert.True(marca.GetProperty("esEjemplo").GetBoolean());

        var logo = await Http.GetAsync(cliente, "/api/marca/logo");
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/svg+xml", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await File.ReadAllBytesAsync(LogoEjemplo), await logo.Content.ReadAsByteArrayAsync());
    }

    /// <summary>Hay una advertencia, y ni ella ni ninguna otra línea del log lleva la carpeta de la prueba ni la del usuario.</summary>
    private void AfirmarAdvertenciaSinRutas(FabricaApi fabrica)
    {
        var lineas = fabrica.Registro.Lineas;
        Assert.Contains(
            lineas,
            l => l.Nivel == LogLevel.Warning && l.Mensaje.Contains(ProveedorMarcaArchivo.ClaveDirectorio, StringComparison.Ordinal));
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(_raiz, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            lineas, l => l.TextoCompleto.Contains(AppContext.BaseDirectory.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(perfil))
        {
            Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(perfil, StringComparison.OrdinalIgnoreCase));
        }
    }
}
