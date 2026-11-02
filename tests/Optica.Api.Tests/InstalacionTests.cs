using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Optica.Api.Tests;

/// <summary>
/// Lo que la configuración versionada deja listo para instalar en la PC del local: hosts permitidos restringidos, sin
/// certificados ni contraseñas en <c>appsettings.json</c> y sin registro HTTP de pedidos (traerían la contraseña).
/// </summary>
public sealed class InstalacionTests
{
    [Fact]
    public async Task AllowedHosts_PorDefecto_SoloLocalhost()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        var local = await PedirConHost(cliente, "localhost");
        var otro = await PedirConHost(cliente, "intruso.example");

        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otro.StatusCode);
    }

    [Fact]
    public async Task AllowedHosts_ConfiguradoAlInstalar_AceptaElNombreDeLaPc()
    {
        using var fabrica = new FabricaApi();
        using var instalada = fabrica.WithWebHostBuilder(constructor =>
            constructor.UseSetting("AllowedHosts", "localhost;optica-pc"));
        var cliente = instalada.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

        Assert.Equal(HttpStatusCode.OK, (await PedirConHost(cliente, "optica-pc")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PedirConHost(cliente, "intruso.example")).StatusCode);
    }

    [Fact]
    public void Appsettings_SinCertificadoNiContrasenasNiHostsAbiertos()
    {
        var archivo = Path.Combine(Repositorio.Raiz(), "src", "Optica.Api", "appsettings.json");
        using var json = JsonDocument.Parse(File.ReadAllText(archivo));
        var raiz = json.RootElement;

        Assert.NotEqual("*", raiz.GetProperty("AllowedHosts").GetString());
        if (raiz.TryGetProperty("Kestrel", out var kestrel))
        {
            Assert.False(kestrel.TryGetProperty("Certificates", out _), "El certificado se configura al instalar.");
        }

        var texto = raiz.GetRawText();
        Assert.DoesNotContain("Password", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Contrasena", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HttpLogging_NoEstaActivado()
    {
        // AddHttpLogging registra su configuración; sin ella no hay registro de pedidos ni de cuerpos (definir e
        // ingresar traen la contraseña en claro).
        using var fabrica = new FabricaApi();
        _ = fabrica.Services;

        Assert.Empty(fabrica.Services.GetServices<IConfigureOptions<HttpLoggingOptions>>());
        Assert.Empty(fabrica.Services.GetServices<IConfigureOptions<W3CLoggerOptions>>());
    }

    private static Task<HttpResponseMessage> PedirConHost(HttpClient cliente, string host)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, "/api/acceso/estado");
        pedido.Headers.Host = host;
        return cliente.SendAsync(pedido);
    }
}

/// <summary>Ubicación del repositorio, a partir de la carpeta de las pruebas.</summary>
internal static class Repositorio
{
    public static string Raiz()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null && !File.Exists(Path.Combine(directorio.FullName, "Optica.sln")))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName ?? throw new InvalidOperationException("No se encontró Optica.sln.");
    }
}
