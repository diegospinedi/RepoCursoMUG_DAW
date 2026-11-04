using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optica.Api.Acceso;
using Optica.Application.Comun;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests.Acceso;

/// <summary>
/// El restablecimiento por comando en la PC (FR-04, FR-05): se ejecuta con la misma raíz de composición que la Api,
/// por <see cref="Program.EjecutarRestablecimientoAsync"/>, sobre un directorio de datos propio de cada prueba.
/// </summary>
public sealed class ComandoRestablecerContrasenaTests
{
    private const string MensajeEnMarcha = "La aplicación está en marcha. Detenela antes de restablecer la contraseña.";
    private const string MensajeMigraciones = "Iniciá la aplicación una vez antes de restablecer.";
    private const string PrimeraMigracion = "20261030220627_Inicial";

    [Fact]
    public async Task Comando_ConfirmacionCorrecta_SiguienteIngresoPideDefinir()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            List<EventoAccesoFila> antes;
            using (var api = new FabricaApi(directorioDatos: directorio))
            {
                var cliente = api.CrearClienteSinCookies();
                await Http.DefinirAsync(cliente);
                await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena("otra-clave-mala"));
                antes = api.Eventos();
            }

            var (codigo, salida) = await EjecutarComandoAsync(directorio, "  RESTABLECER  \n");

            Assert.Equal(0, codigo);
            Assert.Contains("RESTABLECER", salida, StringComparison.Ordinal);
            Assert.Contains(RutasPrivadas.Ocultar(directorio), salida, StringComparison.Ordinal);
            using var despues = new FabricaApi(directorioDatos: directorio);
            var estado = await Http.JsonAsync(await Http.GetAsync(despues.CrearClienteSinCookies(), "/api/acceso/estado"));
            Assert.False(estado.GetProperty("contrasenaDefinida").GetBoolean());
            // Los eventos anteriores siguen en la base, y se agregó uno solo.
            var eventos = despues.Eventos();
            Assert.Equal(antes.Count + 1, eventos.Count);
            Assert.Equal(
                antes.Select(e => (e.Id, e.Tipo, e.Resultado, e.FechaHora)),
                eventos.Take(antes.Count).Select(e => (e.Id, e.Tipo, e.Resultado, e.FechaHora)));
            Assert.Equal(("restablecimiento", "exito"), (eventos[^1].Tipo, eventos[^1].Resultado));
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Theory]
    [InlineData("restablecer\n")]
    [InlineData("RESTABLECERLA\n")]
    [InlineData("RE STABLECER\n")]
    [InlineData("\n")]
    [InlineData("")]
    public async Task Comando_ConfirmacionInvalida_CanceladoSinCambios(string entrada)
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            var antes = await PrepararBaseConContrasenaAsync(directorio);

            var (codigo, salida) = await EjecutarComandoAsync(directorio, entrada);

            Assert.Equal(1, codigo);
            Assert.Contains("cancel", salida, StringComparison.OrdinalIgnoreCase);
            AfirmarSinCambios(directorio, antes);
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task Comando_ConArgumentosDeMas_FallaSinCambios()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            var antes = await PrepararBaseConContrasenaAsync(directorio);

            var (codigo, salida) = await EjecutarComandoAsync(
                directorio, "RESTABLECER\n", [ComandoRestablecerContrasena.Nombre, "--forzar"]);

            Assert.Equal(2, codigo);
            Assert.Contains(ComandoRestablecerContrasena.Nombre, salida, StringComparison.Ordinal);
            AfirmarSinCambios(directorio, antes);
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task Comando_ApiEnMarcha_ErrorSinCambios()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            using var api = new FabricaApi(directorioDatos: directorio);
            var cliente = api.CrearClienteSinCookies();
            var cookie = await Http.DefinirAsync(cliente);
            var eventos = api.Eventos().Count;
            var claves = Claves(directorio);

            var (codigo, salida) = await EjecutarComandoAsync(directorio, "RESTABLECER\n");

            Assert.Equal(2, codigo);
            Assert.Equal(MensajeEnMarcha, salida.Trim());
            // Nada cambió: la contraseña sigue definida, la sesión sigue valiendo, sin eventos nuevos ni claves tocadas.
            var estado = await Http.JsonAsync(await Http.GetAsync(cliente, "/api/acceso/estado"));
            Assert.True(estado.GetProperty("contrasenaDefinida").GetBoolean());
            Assert.Equal(HttpStatusCode.NoContent, (await Http.GetAsync(cliente, "/api/sesion", cookie)).StatusCode);
            Assert.Equal(eventos, api.Eventos().Count);
            Assert.Equal(claves, Claves(directorio));
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task Comando_BaseInexistente_ErrorSinCrearla()
    {
        var raiz = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            // Las dos formas: el directorio de datos no existe (otra cuenta), o existe sin optica.db.
            var inexistente = Path.Combine(raiz, "otra-cuenta");
            var vacio = Path.Combine(raiz, "vacio");
            Directory.CreateDirectory(vacio);

            foreach (var directorio in new[] { inexistente, vacio })
            {
                var (codigo, salida) = await EjecutarComandoAsync(directorio, "RESTABLECER\n");

                Assert.Equal(2, codigo);
                Assert.Equal(
                    $"No se encontró la base de datos en {RutasPrivadas.Ocultar(directorio)}. Ejecutá el comando con la cuenta de la Api.",
                    salida.Trim());
            }

            // No se creó nada: ni el directorio, ni la base, ni -wal, ni -shm, ni el candado.
            Assert.False(Directory.Exists(inexistente));
            Assert.Empty(Directory.EnumerateFileSystemEntries(vacio));
        }
        finally
        {
            BorrarDirectorio(raiz);
        }
    }

    [Fact]
    public async Task Comando_MigracionesPendientes_ErrorSinMigrar()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            // Una base que quedó en la primera migración (por ejemplo, la versión nueva instalada sin arrancarla).
            await using (var app = ConstruirComando(directorio))
            {
                using var alcance = app.Services.CreateScope();
                alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().GetService<IMigrator>().Migrate(PrimeraMigracion);
            }

            var (codigo, salida) = await EjecutarComandoAsync(directorio, "RESTABLECER\n");

            Assert.Equal(2, codigo);
            Assert.Equal(MensajeMigraciones, salida.Trim());
            Assert.Equal([PrimeraMigracion], Consultar(directorio, "SELECT MigrationId FROM __EFMigrationsHistory"));
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task Comando_ErrorDeSqlite_ErrorSinRutaNiExcepcion()
    {
        // En la carpeta del usuario, para que la ruta visible y la expandida sean distintas.
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(perfil), "La prueba necesita la carpeta del usuario.");
        var directorio = FabricaApi.NuevoDirectorioEnElPerfil();
        try
        {
            var antes = await PrepararBaseConContrasenaAsync(directorio);
            // La base rechaza borrar la credencial; el texto del error trae una ruta con un nombre de usuario.
            Ejecutar(
                directorio,
                "CREATE TRIGGER PruebaFalla BEFORE DELETE ON CredencialAcceso BEGIN " +
                $"SELECT RAISE(ABORT, 'zq7 falla en {directorio}/usuario-privado'); END;");

            var (codigo, salida) = await EjecutarComandoAsync(directorio, "RESTABLECER\n");

            Assert.Equal(2, codigo);
            var ultima = salida.Trim().Split('\n')[^1];
            Assert.Contains(RutasPrivadas.Ocultar(directorio), ultima, StringComparison.Ordinal);
            Assert.Contains(nameof(SqliteException), ultima, StringComparison.Ordinal);
            Assert.Contains("código de error de SQLite 19", ultima, StringComparison.Ordinal);
            Assert.DoesNotContain(perfil, salida, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("zq7", salida, StringComparison.Ordinal);
            Assert.DoesNotContain("usuario-privado", salida, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", salida, StringComparison.Ordinal);
            Ejecutar(directorio, "DROP TRIGGER PruebaFalla;");
            AfirmarSinCambios(directorio, antes);
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task Comando_RenuevaLasClaves_SesionAnteriorNoAutorizada()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            string cookie;
            using (var primera = new FabricaApi(directorioDatos: directorio))
            {
                cookie = await Http.DefinirAsync(primera.CrearClienteSinCookies());
            }

            // Control: sin restablecer, la cookie sigue valiendo después de reiniciar la Api (las claves están en disco).
            using (var reiniciada = new FabricaApi(directorioDatos: directorio))
            {
                var sondeo = await Http.GetAsync(reiniciada.CrearClienteSinCookies(), "/api/sesion", cookie);
                Assert.Equal(HttpStatusCode.NoContent, sondeo.StatusCode);
            }

            Assert.Equal(0, (await EjecutarComandoAsync(directorio, "RESTABLECER\n")).Codigo);

            using var despues = new FabricaApi(directorioDatos: directorio);
            var respuesta = await Http.GetAsync(despues.CrearClienteSinCookies(), "/api/sesion", cookie);
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task Restablecimiento_NoExisteEnLaApi_NingunEndpointLoOfrece()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var rutas = new[]
        {
            "/api/acceso/restablecer",
            "/api/acceso/" + ComandoRestablecerContrasena.Nombre,
            "/api/acceso/restablecimiento",
            "/api/" + ComandoRestablecerContrasena.Nombre,
        };

        foreach (var ruta in rutas)
        {
            foreach (var metodo in new[] { HttpMethod.Get, HttpMethod.Post })
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await EnviarAsync(cliente, metodo, ruta, null)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await EnviarAsync(cliente, metodo, ruta, cookie)).StatusCode);
            }
        }

        // Ningún endpoint registrado ni ruta anónima habla de restablecer.
        var endpoints = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);
        Assert.DoesNotContain(
            endpoints, e => e.RoutePattern.RawText?.Contains("restablec", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(RutasAnonimas.Todas, r => r.Ruta.Contains("restablec", StringComparison.OrdinalIgnoreCase));
        // La credencial sigue: ningún pedido la borró.
        var estado = await Http.JsonAsync(await Http.GetAsync(cliente, "/api/acceso/estado"));
        Assert.True(estado.GetProperty("contrasenaDefinida").GetBoolean());
    }

    [Fact]
    public async Task Api_SegundaInstanciaSobreElMismoDirectorio_RechazadaAlArrancar()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            using var primera = new FabricaApi(directorioDatos: directorio);
            var cliente = primera.CrearClienteSinCookies();
            Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync(cliente, "/api/acceso/estado")).StatusCode);

            using (var segunda = new FabricaApi(directorioDatos: directorio))
            {
                var error = Assert.ThrowsAny<Exception>(() => segunda.CreateClient());

                var enMarcha = Assert.Single(Cadena(error).OfType<InstanciaEnMarchaException>());
                // El mensaje y el crítico nombran también la otra causa posible: los permisos del archivo del candado.
                foreach (var mensaje in new[]
                {
                    enMarcha.Message,
                    Assert.Single(segunda.Registro.Lineas, l => l.Nivel == LogLevel.Critical).Mensaje,
                })
                {
                    Assert.Contains(RutasPrivadas.Ocultar(directorio), mensaje, StringComparison.Ordinal);
                    Assert.Contains(CandadoInstanciaArchivo.NombreArchivo, mensaje, StringComparison.Ordinal);
                    Assert.Contains("permisos", mensaje, StringComparison.Ordinal);
                }
            }

            // La primera sigue.
            Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync(cliente, "/api/acceso/estado")).StatusCode);
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task FabricaApi_PruebasEnParalelo_ArrancanCadaUnaConSuCandado()
    {
        var fabricas = Enumerable.Range(0, 4).Select(_ => new FabricaApi()).ToList();
        try
        {
            var respuestas = await Task.WhenAll(fabricas.Select(f => Http.GetAsync(f.CrearClienteSinCookies(), "/api/acceso/estado")));

            Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            Assert.Equal(fabricas.Count, fabricas.Select(f => f.DirectorioDatos).Distinct().Count());
            foreach (var fabrica in fabricas)
            {
                // Nunca bajo /mnt/c: ahí FileShare.None no es un candado confiable.
                Assert.False(
                    fabrica.DirectorioDatos.StartsWith("/mnt/", StringComparison.Ordinal),
                    "El directorio de datos de prueba no puede estar en una unidad de Windows montada.");
                // Cada Api tiene tomado su propio candado.
                Assert.True(File.Exists(Path.Combine(fabrica.DirectorioDatos, CandadoInstanciaArchivo.NombreArchivo)));
                Assert.Null(fabrica.Services.GetRequiredService<ICandadoInstancia>().IntentarAdquirir());
            }
        }
        finally
        {
            foreach (var fabrica in fabricas)
            {
                fabrica.Dispose();
            }
        }
    }

    [Fact]
    public void FabricaApi_DirectorioEnElPerfil_BajoUnaRaizFijaDentroDelPerfil()
    {
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directorio = FabricaApi.NuevoDirectorioEnElPerfil();
        try
        {
            Assert.True(Directory.Exists(directorio));
            Assert.Equal(FabricaApi.RaizEnElPerfil, Path.GetDirectoryName(directorio));
            // Dentro del perfil (la ruta visible y la expandida difieren), pero nunca en su raíz.
            Assert.NotEqual(RutasPrivadas.Ocultar(directorio), directorio);
            Assert.NotEqual(perfil, Path.GetDirectoryName(directorio));
            Assert.NotEqual(perfil, Path.GetDirectoryName(FabricaApi.RaizEnElPerfil));
            Assert.False(directorio.StartsWith("/mnt/", StringComparison.Ordinal));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(Path.Combine(perfil, ".cache", "optica-pruebas"), FabricaApi.RaizEnElPerfil);
            }
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void FabricaApi_LimpiarDirectoriosViejos_BorraSoloLosViejos()
    {
        var raiz = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            var viejo = Path.Combine(raiz, "viejo");
            Directory.CreateDirectory(Path.Combine(viejo, "claves"));
            File.WriteAllText(Path.Combine(viejo, FabricaConexion.NombreArchivo), "x");
            Directory.SetLastWriteTimeUtc(viejo, DateTime.UtcNow.AddDays(-2));
            var reciente = Path.Combine(raiz, "reciente");
            Directory.CreateDirectory(reciente);

            FabricaApi.LimpiarDirectoriosViejos(raiz, TimeSpan.FromHours(1));

            Assert.False(Directory.Exists(viejo));
            Assert.True(Directory.Exists(reciente));
        }
        finally
        {
            BorrarDirectorio(raiz);
        }
    }

    [Theory]
    [InlineData(new[] { "restablecer-contrasena" }, true)]
    [InlineData(new[] { "restablecer-contrasena", "--forzar" }, true)]
    [InlineData(new[] { "--urls=https://localhost:5001" }, false)]
    [InlineData(new[] { "RESTABLECER-CONTRASENA" }, false)]
    [InlineData(new string[0], false)]
    public void Program_PrimerArgumento_EligeLaRamaDelComando(string[] argumentos, bool esComando)
    {
        Assert.Equal(esComando, Program.EsComandoRestablecer(argumentos));
        Assert.Equal(AppContext.BaseDirectory, Program.OpcionesDelComando().ContentRootPath);
    }

    [Fact]
    public async Task Comando_CandadoSinDirectorio_BaseInexistente()
    {
        var datos = new DatosFalsos();
        var candado = new CandadoFalso(() => throw new DirectoryNotFoundException("/home/usuario-privado/datos"));

        var (codigo, salida) = await EjecutarConDoblesAsync(datos, candado, "RESTABLECER\n");

        Assert.Equal(2, codigo);
        Assert.Equal(
            $"No se encontró la base de datos en {datos.RutaVisible}. Ejecutá el comando con la cuenta de la Api.", salida.Trim());
        Assert.Equal(0, datos.Renovaciones);
    }

    [Fact]
    public async Task Comando_CandadoConOtraFalla_FallaSoloConElTipo()
    {
        var datos = new DatosFalsos();
        var candado = new CandadoFalso(() => throw new PathTooLongException("/home/usuario-privado/datos/optica.instancia"));

        var (codigo, salida) = await EjecutarConDoblesAsync(datos, candado, "RESTABLECER\n");

        Assert.Equal(2, codigo);
        Assert.Contains(nameof(PathTooLongException), salida, StringComparison.Ordinal);
        Assert.DoesNotContain("usuario-privado", salida, StringComparison.Ordinal);
        Assert.False(candado.Liberado);
    }

    [Fact]
    public async Task Comando_MigracionesIlegibles_FallaSinTextoNiCambios()
    {
        var datos = new DatosFalsos { FallaMigraciones = new InvalidDataException("/home/usuario-privado/optica.db dañada") };
        var candado = new CandadoFalso();

        var (codigo, salida) = await EjecutarConDoblesAsync(datos, candado, "RESTABLECER\n");

        Assert.Equal(2, codigo);
        Assert.Equal($"No se pudo restablecer la contraseña ({nameof(InvalidDataException)}). No se cambió nada.", salida.Trim());
        Assert.Equal(0, datos.Renovaciones);
        Assert.True(candado.Liberado);
    }

    [Fact]
    public async Task Comando_FallaAlRenovarClaves_PideRepetirElComando()
    {
        var datos = new DatosFalsos { FallaAlRenovar = new UnauthorizedAccessException("/home/usuario-privado/claves") };
        var candado = new CandadoFalso();

        var (codigo, salida, almacen) = await EjecutarConDoblesAsync(datos, candado, "RESTABLECER\n", conCredencial: true);

        Assert.Equal(2, codigo);
        var ultima = salida.Trim().Split('\n')[^1];
        Assert.Contains(nameof(UnauthorizedAccessException), ultima, StringComparison.Ordinal);
        Assert.Contains("Repetí el comando", ultima, StringComparison.Ordinal);
        Assert.DoesNotContain("usuario-privado", salida, StringComparison.Ordinal);
        // Lo confirmado queda; el candado se mantuvo hasta el final.
        Assert.False(almacen.HayCredencial);
        Assert.Equal(1, almacen.Eventos);
        Assert.True(candado.Liberado);
    }

    [Fact]
    public async Task Comando_FallaInesperadaAlRenovarClaves_PideRepetirSinDecirQueNoCambioNada()
    {
        var datos = new DatosFalsos { FallaAlRenovar = new InvalidOperationException("/home/usuario-privado/claves") };
        var candado = new CandadoFalso();

        var (codigo, salida, almacen) = await EjecutarConDoblesAsync(datos, candado, "RESTABLECER\n", conCredencial: true);

        Assert.Equal(2, codigo);
        var ultima = salida.Trim().Split('\n')[^1];
        Assert.Contains(nameof(InvalidOperationException), ultima, StringComparison.Ordinal);
        Assert.Contains("Repetí el comando", ultima, StringComparison.Ordinal);
        Assert.DoesNotContain("No se cambió nada", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("usuario-privado", salida, StringComparison.Ordinal);
        Assert.False(almacen.HayCredencial);
        Assert.Equal(1, almacen.Eventos);
    }

    [Fact]
    public async Task Comando_DirectorioRelativo_FallaConLaClaveYLaRutaSinTraza()
    {
        const string relativo = "datos-relativos-zq7";

        var (codigo, salida) = await EjecutarComandoAsync(relativo, "RESTABLECER\n");

        Assert.Equal(2, codigo);
        var texto = salida.Trim();
        Assert.Contains(FabricaConexion.ClaveDirectorio, texto, StringComparison.Ordinal);
        Assert.Contains(relativo, texto, StringComparison.Ordinal);
        Assert.Contains("No se cambió nada", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", salida, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DirectorioDatosInvalidoException), salida, StringComparison.Ordinal);
        // No se creó nada en la carpeta actual.
        Assert.False(Directory.Exists(relativo));
    }

    [Fact]
    public async Task Comando_FallaAlArmarElHost_FallaSoloConElTipoYLaClave()
    {
        using var salida = new StringWriter();

        var codigo = await Program.EjecutarComandoAsync(
            () => throw new InvalidDataException("/home/usuario-privado/appsettings.json está dañado"),
            [ComandoRestablecerContrasena.Nombre],
            new StringReader("RESTABLECER\n"),
            salida);

        Assert.Equal(2, codigo);
        var texto = salida.ToString();
        Assert.Contains(nameof(InvalidDataException), texto, StringComparison.Ordinal);
        Assert.Contains(FabricaConexion.ClaveDirectorio, texto, StringComparison.Ordinal);
        Assert.Contains("No se cambió nada", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("usuario-privado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comando_HostArmadoConLaComposicion_RestableceYDescartaElHost()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            await PrepararBaseConContrasenaAsync(directorio);
            using var salida = new StringWriter();

            var codigo = await Program.EjecutarComandoAsync(
                () => ConstruirComando(directorio), [ComandoRestablecerContrasena.Nombre], new StringReader("RESTABLECER\n"), salida);

            Assert.Equal(0, codigo);
            // El host se descartó: el candado quedó libre.
            using var candado = new CandadoInstanciaArchivo(directorio).IntentarAdquirir();
            Assert.NotNull(candado);
        }
        finally
        {
            BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task Comando_SinCredencial_RegistraYRenuevaCodigo0()
    {
        var datos = new DatosFalsos();
        var candado = new CandadoFalso();

        var (codigo, salida, almacen) = await EjecutarConDoblesAsync(datos, candado, "RESTABLECER\n", conCredencial: false);

        Assert.Equal(0, codigo);
        Assert.Contains("No había una contraseña definida", salida, StringComparison.Ordinal);
        Assert.Equal(1, almacen.Eventos);
        Assert.Equal(1, datos.Renovaciones);
        // El candado se libera al terminar, no antes de confirmar.
        Assert.Equal(1, candado.LiberadoConEventos(almacen));
    }

    private static async Task<(int Codigo, string Salida)> EjecutarConDoblesAsync(
        DatosFalsos datos, CandadoFalso candado, string entrada)
    {
        var (codigo, salida, _) = await EjecutarConDoblesAsync(datos, candado, entrada, conCredencial: true);
        return (codigo, salida);
    }

    private static async Task<(int Codigo, string Salida, AlmacenSimple Almacen)> EjecutarConDoblesAsync(
        DatosFalsos datos, CandadoFalso candado, string entrada, bool conCredencial)
    {
        var almacen = new AlmacenSimple { HayCredencial = conCredencial };
        candado.Almacen = almacen;
        var caso = new Optica.Application.Acceso.RestablecerContrasena(almacen, almacen, almacen, datos, TimeProvider.System);
        using var salida = new StringWriter();
        var codigo = await new ComandoRestablecerContrasena(caso, datos, candado, new StringReader(entrada), salida).EjecutarAsync();
        return (codigo, salida.ToString(), almacen);
    }

    /// <summary>Credencial y eventos en memoria; confirma lo pendiente de una vez.</summary>
    private sealed class AlmacenSimple
        : Optica.Domain.Acceso.ICredencialAccesoRepository, Optica.Domain.Acceso.IRegistroEventosAcceso, IUnidadDeTrabajo
    {
        private bool _eliminar;
        private int _eventosPendientes;

        public bool HayCredencial { get; set; }

        public int Eventos { get; private set; }

        public Task<bool> ExisteAsync(CancellationToken cancelacion = default) => Task.FromResult(HayCredencial);

        public Task<Optica.Domain.Acceso.CredencialAcceso?> ObtenerAsync(CancellationToken cancelacion = default) =>
            throw new NotSupportedException();

        public void Agregar(Optica.Domain.Acceso.CredencialAcceso credencial) => throw new NotSupportedException();

        public void Reemplazar(Optica.Domain.Acceso.CredencialAcceso anterior, Optica.Domain.Acceso.CredencialAcceso nueva) =>
            throw new NotSupportedException();

        public void Eliminar() => _eliminar = true;

        public void Registrar(Optica.Domain.Acceso.EventoAcceso evento) => _eventosPendientes++;

        public Task<IReadOnlyList<DateTime>> ObtenerRechazosIngresoDesdeUltimoReinicioAsync(CancellationToken cancelacion = default) =>
            throw new NotSupportedException();

        public Task ConfirmarAsync(CancellationToken cancelacion = default)
        {
            HayCredencial &= !_eliminar;
            Eventos += _eventosPendientes;
            _eventosPendientes = 0;
            return Task.CompletedTask;
        }
    }

    /// <summary>Datos locales de prueba: la base existe y está al día, salvo que se pida una falla.</summary>
    private sealed class DatosFalsos : IDatosLocales
    {
        public Exception? FallaMigraciones { get; init; }

        public Exception? FallaAlRenovar { get; init; }

        public int Renovaciones { get; private set; }

        public string Directorio => "/datos-de-prueba";

        public string DirectorioClaves => "/datos-de-prueba/claves";

        public string RutaVisible => "~/datos-de-prueba";

        public bool BaseExiste => true;

        public bool MigracionesPendientes() => FallaMigraciones is null ? false : throw FallaMigraciones;

        public void RenovarClaves()
        {
            Renovaciones++;
            if (FallaAlRenovar is not null)
            {
                throw FallaAlRenovar;
            }
        }

        public FallaDatos? DescribirFalla(Exception error) => null;
    }

    /// <summary>Candado de prueba: se toma (o falla como se pida) y anota cuándo se liberó.</summary>
    private sealed class CandadoFalso(Func<IDisposable?>? adquirir = null) : ICandadoInstancia
    {
        private int? _eventosAlLiberar;

        public AlmacenSimple? Almacen { get; set; }

        public bool Liberado => _eventosAlLiberar is not null;

        public int? LiberadoConEventos(AlmacenSimple almacen) => ReferenceEquals(almacen, Almacen) ? _eventosAlLiberar : null;

        public IDisposable? IntentarAdquirir() => adquirir is null ? new Liberacion(this) : adquirir();

        private sealed class Liberacion(CandadoFalso candado) : IDisposable
        {
            public void Dispose() => candado._eventosAlLiberar = candado.Almacen?.Eventos ?? 0;
        }
    }

    /// <summary>Arma la aplicación como la rama del comando de <c>Program</c>, sobre el directorio de datos indicado.</summary>
    private static WebApplication ConstruirComando(string directorio)
    {
        var builder = WebApplication.CreateBuilder(Program.OpcionesDelComando());
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [FabricaConexion.ClaveDirectorio] = directorio });
        Program.ConfigurarServicios(builder);
        return builder.Build();
    }

    private static async Task<(int Codigo, string Salida)> EjecutarComandoAsync(
        string directorio, string entrada, string[]? argumentos = null)
    {
        await using var app = ConstruirComando(directorio);
        using var salida = new StringWriter();
        var codigo = await Program.EjecutarRestablecimientoAsync(
            app.Services, argumentos ?? [ComandoRestablecerContrasena.Nombre], new StringReader(entrada), salida);
        if (Path.IsPathFullyQualified(directorio))
        {
            FabricaApi.LiberarConexiones(directorio);
        }

        return (codigo, salida.ToString());
    }

    /// <summary>Lo que se compara para decir "nada cambió".</summary>
    private sealed record Estado(string Hash, List<(long Id, string Tipo, string Resultado)> Eventos, List<string> Claves);

    /// <summary>Define la contraseña con una Api que se descarta enseguida, y devuelve cómo quedó la base.</summary>
    private static async Task<Estado> PrepararBaseConContrasenaAsync(string directorio)
    {
        using (var api = new FabricaApi(directorioDatos: directorio))
        {
            await Http.DefinirAsync(api.CrearClienteSinCookies());
        }

        FabricaApi.LiberarConexiones(directorio);
        return LeerEstado(directorio);
    }

    private static void AfirmarSinCambios(string directorio, Estado antes)
    {
        var despues = LeerEstado(directorio);
        Assert.Equal(antes.Hash, despues.Hash);
        Assert.Equal(antes.Eventos, despues.Eventos);
        Assert.Equal(antes.Claves, despues.Claves);
    }

    private static Estado LeerEstado(string directorio) =>
        new(
            Assert.Single(Consultar(directorio, "SELECT Hash FROM CredencialAcceso")),
            ConsultarEventos(directorio),
            Claves(directorio));

    private static List<string> Claves(string directorio)
    {
        var claves = Path.Combine(directorio, "claves");
        return Directory.Exists(claves)
            ? Directory.GetFiles(claves).Select(archivo => Path.GetFileName(archivo)).Order(StringComparer.Ordinal).ToList()
            : [];
    }

    private static List<(long Id, string Tipo, string Resultado)> ConsultarEventos(string directorio)
    {
        using var conexion = Abrir(directorio);
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT Id, Tipo, Resultado FROM EventoAcceso ORDER BY Id";
        using var lector = comando.ExecuteReader();
        var eventos = new List<(long, string, string)>();
        while (lector.Read())
        {
            eventos.Add((lector.GetInt64(0), lector.GetString(1), lector.GetString(2)));
        }

        return eventos;
    }

    private static List<string> Consultar(string directorio, string sql)
    {
        using var conexion = Abrir(directorio);
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        using var lector = comando.ExecuteReader();
        var valores = new List<string>();
        while (lector.Read())
        {
            valores.Add(lector.GetString(0));
        }

        return valores;
    }

    private static void Ejecutar(string directorio, string sql)
    {
        using var conexion = Abrir(directorio);
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    private static SqliteConnection Abrir(string directorio)
    {
        var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directorio, FabricaConexion.NombreArchivo),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        conexion.Open();
        return conexion;
    }

    private static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, HttpMethod metodo, string ruta, string? cookie) =>
        metodo == HttpMethod.Post
            ? Http.PostJsonAsync(cliente, ruta, "{}", cookie)
            : Http.GetAsync(cliente, ruta, cookie);

    private static void BorrarDirectorio(string directorio)
    {
        FabricaApi.LiberarConexiones(directorio);
        if (Directory.Exists(directorio))
        {
            Directory.Delete(directorio, recursive: true);
        }
    }

    private static IEnumerable<Exception> Cadena(Exception error)
    {
        for (Exception? actual = error; actual is not null; actual = actual.InnerException)
        {
            yield return actual;
            if (actual is AggregateException agregada)
            {
                foreach (var interna in agregada.InnerExceptions.SelectMany(Cadena))
                {
                    yield return interna;
                }
            }
        }
    }
}
