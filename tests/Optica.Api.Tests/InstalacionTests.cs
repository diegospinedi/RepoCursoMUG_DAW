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

        // D-26 de FEAT-007: ninguna clave (la del certificado de ARCA, Arca:Certificado:Clave) con valor en el archivo
        // versionado; se carga con una variable de entorno o la configuración local.
        Assert.Empty(ClavesConValor(raiz, "$"));
    }

    [Fact]
    public void ClavesConValor_DetectaClaveConValorEnCualquierNivel()
    {
        using var json = JsonDocument.Parse(
            """{"Arca":{"Certificado":{"Ruta":"","Clave":"secreta"}},"Otra":{"ClaveApi":"x","Clave":""},"Nula":{"Clave":null},"Lista":[{"Clave":"en-arreglo"},{"Clave":""},[{"Clave":"anidada"}]]}""");

        Assert.Equal(
            ["$.Arca.Certificado.Clave", "$.Otra.ClaveApi", "$.Lista[0].Clave", "$.Lista[2][0].Clave"],
            ClavesConValor(json.RootElement, "$"));
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

    /// <summary>
    /// Los caminos de las propiedades cuyo nombre contiene <c>Clave</c> (sin distinguir mayúsculas) y tienen un valor
    /// que no es un texto vacío ni null, a cualquier profundidad, dentro de objetos y de arreglos. Solo se informa el
    /// camino, nunca el valor.
    /// </summary>
    private static List<string> ClavesConValor(JsonElement elemento, string camino)
    {
        var encontradas = new List<string>();
        if (elemento.ValueKind == JsonValueKind.Array)
        {
            var indice = 0;
            foreach (var item in elemento.EnumerateArray())
            {
                encontradas.AddRange(ClavesConValor(item, $"{camino}[{indice}]"));
                indice++;
            }

            return encontradas;
        }

        if (elemento.ValueKind != JsonValueKind.Object)
        {
            return encontradas;
        }

        foreach (var propiedad in elemento.EnumerateObject())
        {
            var hijo = $"{camino}.{propiedad.Name}";
            var valor = propiedad.Value;
            var vacio = valor.ValueKind == JsonValueKind.Null
                || (valor.ValueKind == JsonValueKind.String && valor.GetString()!.Length == 0);
            if (propiedad.Name.Contains("Clave", StringComparison.OrdinalIgnoreCase) && !vacio)
            {
                encontradas.Add(hijo);
            }

            encontradas.AddRange(ClavesConValor(valor, hijo));
        }

        return encontradas;
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
