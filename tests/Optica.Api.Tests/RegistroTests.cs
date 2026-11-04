using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// Pares (actual, nueva) y el código esperado. Llevan sustitutos sueltos: xUnit los cambiaría por U+FFFD al descubrir
    /// las pruebas, por eso <c>DisableDiscoveryEnumeration</c>.
    /// </summary>
    public static TheoryData<string, string, HttpStatusCode> CambiosConSecretos => new()
    {
        // Correcto: la actual es la definida y la nueva se acepta.
        { Definida, "Zq7-nueva-secreta", HttpStatusCode.NoContent },
        // La actual no coincide.
        { Erronea, "Zq7-nueva-secreta-2", HttpStatusCode.BadRequest },
        // La nueva fuera de política: corta, larga o con un sustituto suelto.
        { Definida, Corta, HttpStatusCode.BadRequest },
        { Definida, Larga, HttpStatusCode.BadRequest },
        { Definida, "Zq7-nueva-sustituto\uD800-secreta", HttpStatusCode.BadRequest },
        // La actual con un sustituto suelto.
        { "Zq7-actual-sustituto\uDC00-secreta", "Zq7-nueva-secreta-3", HttpStatusCode.BadRequest },
    };

    [Theory]
    [MemberData(nameof(CambiosConSecretos), DisableDiscoveryEnumeration = true)]
    public async Task Log_Cambiar_NoContieneNingunaContrasena(string actual, string nueva, HttpStatusCode esperado)
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente, Definida);

        var respuesta = await Http.CambiarAsync(cliente, actual, nueva, cookie);
        // Un cuerpo ilegible con las dos contraseñas: el lector de JSON tampoco las registra.
        var ilegible = await Http.PostJsonAsync(
            cliente, "/api/acceso/cambiar", Http.CuerpoCambio(actual, nueva).TrimEnd('}', '"'), cookie);

        // Control: el pedido llegó a la acción; si no, la prueba no estaría mirando nada.
        Assert.Equal(esperado, respuesta.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ilegible.StatusCode);
        var lineas = fabrica.Registro.Lineas;
        // La captura ve todos los niveles, Trace incluido (donde MVC registra los argumentos de la acción).
        Assert.Contains(lineas, l => l.Nivel == LogLevel.Trace);
        foreach (var secreto in new[] { actual, nueva })
        {
            Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(secreto, StringComparison.Ordinal));
            // Un sustituto suelto puede llegar al log cambiado por U+FFFD o escapado: se busca cada tramo a su lado.
            foreach (var tramo in secreto.Split(new[] { '\uD800', '\uDC00' }).Where(t => t.Length >= 10))
            {
                Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(tramo, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public async Task Log_Cambiar_NoContieneNiElHashAnteriorNiElNuevo()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente, Definida);
        var anterior = HashGuardado(fabrica);

        var respuesta = await Http.CambiarAsync(cliente, Definida, "Zq7-nueva-secreta", cookie);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        var nuevo = HashGuardado(fabrica);
        Assert.NotEqual(anterior, nuevo);
        var lineas = fabrica.Registro.Lineas;
        // La captura ve todos los niveles, Trace incluido (donde EF Core registra los comandos).
        Assert.Contains(lineas, l => l.Nivel == LogLevel.Trace);
        foreach (var hash in new[] { anterior, nuevo })
        {
            Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(hash, StringComparison.Ordinal));
            // Tampoco la sal ni el derivado por separado (formato algoritmo$iteraciones$sal$derivado).
            foreach (var parte in hash.Split('$').Skip(2))
            {
                Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(parte, StringComparison.Ordinal));
            }
        }
    }

    private static string HashGuardado(FabricaApi fabrica)
    {
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
        return contexto.CredencialesAcceso.AsNoTracking().Single().Hash;
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
        FabricaApi.LiberarConexiones(fabrica.DirectorioDatos);
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

    [Fact]
    public async Task Log_SegundaInstancia_SinRutaDelUsuarioNiExcepcion()
    {
        // En la carpeta del usuario, para que la ruta visible y la expandida sean distintas.
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(perfil), "La prueba necesita la carpeta del usuario.");
        var directorio = FabricaApi.NuevoDirectorioEnElPerfil();
        try
        {
            using var primera = new FabricaApi(directorioDatos: directorio);
            Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync(primera.CrearClienteSinCookies(), "/api/acceso/estado")).StatusCode);
            using var segunda = new FabricaApi(directorioDatos: directorio);

            Assert.ThrowsAny<Exception>(() => segunda.CreateClient());

            var lineas = segunda.Registro.Lineas;
            // Un crítico propio con la ruta visible…
            Assert.Contains(
                lineas,
                l => l.Nivel == LogLevel.Critical
                    && l.Mensaje.Contains(Optica.Application.Comun.RutasPrivadas.Ocultar(directorio), StringComparison.Ordinal));
            // …sin la carpeta del usuario en ningún nivel y sin excepción adjunta (su texto trae la ruta expandida).
            Assert.DoesNotContain(lineas, l => l.TextoCompleto.Contains(perfil, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(lineas, l => l.Excepcion is not null);
        }
        finally
        {
            FabricaApi.LiberarConexiones(directorio);
            Directory.Delete(directorio, recursive: true);
        }
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

        public void Eliminar() => throw falla;
    }
}
