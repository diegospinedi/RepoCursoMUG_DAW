using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Optica.Domain.Acceso;
using Optica.Domain.Presupuestos;
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

    /// <summary>
    /// Piso del log en código (D-12, R-06): el registro de pedidos de ASP.NET Core (la URL con la query string) y el de
    /// MVC (los argumentos de las acciones) quedan en Warning aunque la configuración baje <c>Microsoft.AspNetCore</c> a
    /// Trace (en appsettings o con una variable de entorno). Con la configuración real, y con ese cambio encima.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("Logging:LogLevel:Microsoft.AspNetCore")]
    // La misma categoría que la regla del código: gana la que se agrega después, la del código.
    [InlineData("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics")]
    public async Task Arranque_RegistroDePedidosYDeMvc_NoHabilitadoParaInformation(string? claveEnTrace)
    {
        var bajarMicrosoftAspNetCore = claveEnTrace == "Logging:LogLevel:Microsoft.AspNetCore";
        using var fabrica = new FabricaApi(nivelesDeConfiguracion: true);
        using var configurada = claveEnTrace is null
            ? fabrica
            : fabrica.WithWebHostBuilder(web => web.UseSetting(claveEnTrace, "Trace"));
        // Arranca la Api con esa configuración.
        await configurada.CreateClient().GetAsync("/api/acceso/estado");
        var registros = configurada.Services.GetRequiredService<ILoggerFactory>();

        foreach (var categoria in new[] { "Microsoft.AspNetCore.Hosting.Diagnostics", "Microsoft.AspNetCore.Mvc.Infrastructure" })
        {
            var registro = registros.CreateLogger(categoria);
            Assert.False(registro.IsEnabled(LogLevel.Information), $"{categoria} registra Information.");
            Assert.True(registro.IsEnabled(LogLevel.Warning), $"{categoria} no registra Warning.");
        }

        // Control: el cambio de configuración llegó (otra categoría de ASP.NET Core sí baja).
        Assert.Equal(
            bajarMicrosoftAspNetCore,
            registros.CreateLogger("Microsoft.AspNetCore.Routing").IsEnabled(LogLevel.Information));
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

    /// <summary>
    /// Alerta de datos (Ley 25.326, D-12 de FEAT-003c): el trigger <c>presupuesto_fecha_numero</c> rechaza cambiar la
    /// fecha y sale como una <c>DbUpdateException</c> sin traducir, cuyas <c>Entries</c> llevan los datos del cliente. Se
    /// fuerza con un repositorio que, además de la modificación, cambia la fecha; la Api responde el 500 genérico, y
    /// ningún dato del cliente (los grabados ni los enviados) llega a la respuesta ni al log, en ningún nivel, ni queda
    /// una excepción adjunta por encima de Debug. Datos ficticios con marcadores.
    /// </summary>
    [Fact]
    public async Task Log_TriggerDeFechaDelPresupuesto_SinDatosDelCliente()
    {
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IPresupuestoRepository>();
            servicios.AddScoped<IPresupuestoRepository, RepositorioQueCambiaLaFecha>();
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var articulo = await Http.PostJsonAsync(
            cliente,
            "/api/articulos",
            "{\"codigoProveedor\":\"ABC-1\",\"descripcion\":\"Armazón\",\"precioCosto\":1210,\"margenUtilidad\":50}",
            cookie);
        Assert.Equal(HttpStatusCode.Created, articulo.StatusCode);
        var codigo = (await Http.JsonAsync(articulo)).GetProperty("codigo").GetInt64();
        var alta = await Http.PostJsonAsync(
            cliente,
            "/api/presupuestos",
            "{\"cliente\":" + ClienteMarcado("Zq7Alta") + ",\"lineas\":[{\"codigoArticulo\":" + codigo
            + ",\"cantidad\":1,\"porcentajeDescuento\":0}]}",
            cookie);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var respuesta = await Http.EnviarAsync(
            cliente,
            HttpMethod.Put,
            "/api/presupuestos/1",
            new StringContent(
                "{\"version\":0,\"cliente\":" + ClienteMarcado("Zq7Modif") + ",\"lineas\":[{\"orden\":1,\"codigoArticulo\":"
                + codigo + ",\"cantidad\":2,\"porcentajeDescuento\":0}]}",
                System.Text.Encoding.UTF8,
                "application/json"),
            cookie);

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        var lineas = fabrica.Registro.Lineas;
        // Control: el rechazo fue el del trigger, y el error propio quedó registrado sin el texto de la excepción.
        Assert.Contains(lineas, l => l.Excepcion?.ToString().Contains("presupuesto_fecha_numero", StringComparison.Ordinal) == true);
        Assert.Contains(lineas, l => l.TextoCompleto.Contains(nameof(SqliteException), StringComparison.Ordinal));
        Assert.Contains(
            lineas,
            l => l.Nivel == LogLevel.Error && l.Mensaje.Contains("/api/presupuestos/1", StringComparison.Ordinal));
        var textoRespuesta = await respuesta.Content.ReadAsStringAsync();
        foreach (var dato in new[] { "Zq7", "99999999", "99.999.999", "example.com", "5555-0199" })
        {
            Assert.DoesNotContain(dato, textoRespuesta, StringComparison.Ordinal);
            var conElDato = lineas.Where(l => l.TextoCompleto.Contains(dato, StringComparison.Ordinal))
                .Select(l => $"{l.Categoria} ({l.Nivel})")
                .ToList();
            Assert.True(conElDato.Count == 0, $"\"{dato}\" llegó al log en: {string.Join(", ", conElDato)}");
        }

        Assert.DoesNotContain(lineas, l => l.Nivel > LogLevel.Debug && l.Excepcion is not null);
    }

    private static string ClienteMarcado(string marca) =>
        "{\"apellido\":\"" + marca + "Apellido\",\"nombre\":\"" + marca + "Nombre\",\"dni\":\"99.999.999\","
        + "\"domicilio\":\"" + marca + " Calle Ficticia 123\",\"email\":\"" + marca.ToLowerInvariant()
        + "@example.com\",\"telefono\":\"011-5555-0199\"}";

    /// <summary>
    /// El repositorio real, salvo que la modificación también cambia la fecha del encabezado: así el trigger
    /// <c>presupuesto_fecha_numero</c> rechaza el UPDATE al confirmar.
    /// </summary>
    private sealed class RepositorioQueCambiaLaFecha(OpticaDbContext contexto) : IPresupuestoRepository
    {
        private readonly PresupuestoRepository _real = new(contexto);

        public Task<long?> ObtenerUltimoNumeroAsync(CancellationToken cancelacion = default) =>
            _real.ObtenerUltimoNumeroAsync(cancelacion);

        public void Agregar(Presupuesto presupuesto) => _real.Agregar(presupuesto);

        public Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default) =>
            _real.ObtenerAsync(numero, cancelacion);

        public void Actualizar(Presupuesto original, Presupuesto modificado, long versionOriginal)
        {
            _real.Actualizar(original, modificado, versionOriginal);
            var fila = contexto.ChangeTracker.Entries<PresupuestoFila>().Single(e => e.Entity.Numero == modificado.Numero);
            fila.Property(p => p.Fecha).CurrentValue = fila.Entity.Fecha.AddDays(1);
            fila.Property(p => p.Fecha).IsModified = true;
        }

        public void CambiarEstado(Presupuesto presupuesto, long versionOriginal) =>
            _real.CambiarEstado(presupuesto, versionOriginal);

        public Task<PaginaPresupuestos> BuscarAsync(
            FiltroPresupuestos filtro, int pagina, int tamanoPagina, CancellationToken cancelacion = default) =>
            _real.BuscarAsync(filtro, pagina, tamanoPagina, cancelacion);
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
