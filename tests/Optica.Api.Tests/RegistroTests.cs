using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Optica.Domain.Acceso;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests;

/// <summary>Qué llega al log: ni contraseñas ni rutas con el nombre de usuario, en ningún nivel.</summary>
public sealed class RegistroTests
{
    private const string Definida = "Zq7-definida-secreta";
    private const string Erronea = "Zq7-erronea-secreta";
    private const string Corta = "Zq7-cor";
    private const string Rota = "Zq7-rota-secreta";
    private const string ConError = "Zq7-con-error-secreta";

    private static readonly string Larga = "Zq7-" + new string('L', 125);

    [Fact]
    public async Task Log_DefinirEIngresar_NoContieneLaContrasena()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();

        await Http.DefinirAsync(cliente, Definida);
        await Http.PostJsonAsync(cliente, "/api/acceso/definir", Http.CuerpoContrasena(Erronea));
        await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Definida));
        await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Erronea));
        await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Corta));
        await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Larga));
        await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", "{\"contrasena\":\"" + Rota);

        AfirmarSinContrasenas(fabrica);
    }

    [Theory]
    [InlineData("/api/acceso/definir")]
    [InlineData("/api/acceso/ingresar")]
    public async Task Log_ContrasenaQueNoEsTexto_NoContieneElValor(string ruta)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cuerpos = new[]
        {
            ("987654321987", "{\"contrasena\":987654321987}"),
            ("Zq7-objeto-secreto", "{\"contrasena\":{\"valor\":\"Zq7-objeto-secreto\"}}"),
            ("Zq7-arreglo-secreto", "{\"contrasena\":[\"Zq7-arreglo-secreto\"]}"),
            ("424242424242", "{\"contrasena\":[424242424242]}"),
        };

        foreach (var (_, cuerpo) in cuerpos)
        {
            var respuesta = await Http.PostJsonAsync(cliente, ruta, cuerpo);
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }

        // El registro de captura ve todos los niveles, Trace incluido.
        var lineas = fabrica.Registro.Lineas;
        Assert.Contains(lineas, l => l.Nivel < LogLevel.Information);
        foreach (var (valor, _) in cuerpos)
        {
            Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(valor, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Log_ConLosNivelesDeAppsettings_SinRutaDelContenidoNiDeLasClaves()
    {
        using var fabrica = new FabricaApi(nivelesDeConfiguracion: true);
        var cliente = fabrica.CrearClienteSinCookies();

        // Arranca la Api y emite la cookie, lo que crea la primera clave de Data Protection en el directorio de datos.
        await Http.DefinirAsync(cliente);
        var registros = fabrica.Services.GetRequiredService<ILoggerFactory>();
        registros.CreateLogger("Optica.Prueba").LogInformation("control-de-captura");
        registros.CreateLogger("Microsoft.Hosting.Lifetime").LogInformation("control-de-lifetime");

        var lineas = fabrica.Registro.Lineas;
        // Control: la captura recibe Information de las categorías propias…
        Assert.Contains(lineas, l => l.Mensaje == "control-de-captura");
        // …pero Microsoft.Hosting.Lifetime queda en Warning: no se registra "Content root path" (la carpeta de la
        // aplicación, que en una PC puede estar dentro de la del usuario).
        Assert.DoesNotContain(lineas, l => l.Mensaje == "control-de-lifetime");
        Assert.DoesNotContain(lineas, l => l.Mensaje.Contains("Content root path", StringComparison.Ordinal));
        var claves = Path.Combine(fabrica.DirectorioDatos, "claves");
        Assert.True(Directory.Exists(claves), "La prueba necesita que se haya creado la clave de Data Protection.");
        Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(fabrica.DirectorioDatos, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Log_IngresarConErrorDelSistema_NoContieneLaContrasena()
    {
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<ICredencialAccesoRepository>();
            servicios.AddScoped<ICredencialAccesoRepository>(_ => new RepositorioQueFalla(new InvalidOperationException("falla")));
        });
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(ConError));

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        AfirmarSinContrasenas(fabrica);
    }

    [Fact]
    public async Task Log_ErrorQueNoEsDeLaBase_SinRutaDelUsuarioNiExcepcionCruda()
    {
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(perfil), "La prueba necesita la carpeta del usuario.");
        var archivo = Path.Combine(perfil, "AppData", "Local", "OpticaSistema", "marca", "logo.png");
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<ICredencialAccesoRepository>();
            servicios.AddScoped<ICredencialAccesoRepository>(_ =>
                new RepositorioQueFalla(new FileNotFoundException($"No se encontró el archivo '{archivo}'.", archivo)));
        });
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.GetAsync(cliente, "/api/acceso/estado");

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.DoesNotContain(perfil, await respuesta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var lineas = fabrica.Registro.Lineas;
        // Queda un error propio que nombra el tipo de la falla y el pedido…
        Assert.Contains(
            lineas,
            l => l.Nivel == LogLevel.Error
                && l.Mensaje.Contains(nameof(FileNotFoundException), StringComparison.Ordinal)
                && l.Mensaje.Contains("/api/acceso/estado", StringComparison.Ordinal));
        // …sin la carpeta del usuario en ningún nivel y sin la excepción original adjunta (su texto trae la ruta).
        Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(perfil, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lineas, l => l.Excepcion is not null);
    }

    [Fact]
    public async Task Log_ErrorDeConexionConLaBase_EfLoRegistraSoloEnDebug()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync(cliente, "/api/acceso/estado")).StatusCode);

        // La base deja de poder abrirse: en su lugar queda una carpeta con el mismo nombre.
        SqliteConnection.ClearAllPools();
        var baseDeDatos = Path.Combine(fabrica.DirectorioDatos, FabricaConexion.NombreArchivo);
        foreach (var archivo in new[] { baseDeDatos, baseDeDatos + "-wal", baseDeDatos + "-shm" })
        {
            File.Delete(archivo);
        }

        Directory.CreateDirectory(baseDeDatos);

        var respuesta = await Http.GetAsync(cliente, "/api/acceso/estado");

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        var lineas = fabrica.Registro.Lineas;
        // EF Core registró el error de conexión (con el DataSource completo), pero en Debug.
        Assert.Contains(
            lineas,
            l => l.Evento.Id == RelationalEventId.ConnectionError.Id && l.Nivel == LogLevel.Debug);
        // EF Core no registra por encima de Debug ni errores ni la ruta de la base, y no queda ninguna excepción cruda
        // adjunta en el log.
        var deEf = lineas.Where(l => l.Categoria.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(deEf, l => l.Nivel >= LogLevel.Warning);
        Assert.DoesNotContain(
            deEf, l => l.Nivel > LogLevel.Debug && l.TextoCompleto.Contains(fabrica.DirectorioDatos, StringComparison.Ordinal));
        Assert.DoesNotContain(lineas, l => l.Nivel > LogLevel.Debug && l.Excepcion is not null);
        // El error propio nombra el directorio visible y la clave a revisar.
        Assert.Contains(
            lineas,
            l => l.Nivel == LogLevel.Error && l.Mensaje.Contains(FabricaConexion.ClaveDirectorio, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Log_ErrorNoControlado_ElMiddlewareDeErroresNoRegistraSuDiagnostico()
    {
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<ICredencialAccesoRepository>();
            servicios.AddScoped<ICredencialAccesoRepository>(_ => new RepositorioQueFalla(new InvalidOperationException("falla")));
        });
        var cliente = fabrica.CrearClienteSinCookies();

        var respuesta = await Http.GetAsync(cliente, "/api/acceso/estado");

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        // El único registro del error es el propio del manejador; el middleware no agrega el suyo con la excepción.
        Assert.DoesNotContain(
            fabrica.Registro.Lineas,
            l => l.Categoria.StartsWith("Microsoft.AspNetCore.Diagnostics", StringComparison.Ordinal) && l.Nivel >= LogLevel.Warning);
    }

    private static void AfirmarSinContrasenas(FabricaApi fabrica)
    {
        var lineas = fabrica.Registro.Lineas;
        Assert.NotEmpty(lineas);
        foreach (var secreto in new[] { Definida, Erronea, Corta, Larga, Rota, ConError })
        {
            Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal));
        }
    }

    /// <summary>Repositorio que falla con la excepción indicada.</summary>
    private sealed class RepositorioQueFalla(Exception falla) : ICredencialAccesoRepository
    {
        public Task<bool> ExisteAsync(CancellationToken cancelacion = default) => throw falla;

        public Task<CredencialAcceso?> ObtenerAsync(CancellationToken cancelacion = default) => throw falla;

        public void Agregar(CredencialAcceso credencial) => throw falla;

        public void Reemplazar(CredencialAcceso anterior, CredencialAcceso nueva) => throw falla;
    }
}
