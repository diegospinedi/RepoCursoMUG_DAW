using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Backup;
using Optica.Application.Backup;
using Optica.Application.Comun;
using Optica.Infrastructure.Backup;
using Optica.Infrastructure.Persistencia;
using Optica.Pruebas.Compartido;

namespace Optica.Api.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 7 (FR-13, FR-14, NFR-01; AC-14; D-12, D-14, D-15, D-27 a D-29; R-07, R-08, R-10): el comando
/// <c>Optica.Api restaurar-backup &lt;copia&gt;</c> con la misma raíz de composición que la Api, por
/// <see cref="Program.EjecutarComandoAsync"/>, sobre carpetas de datos propias de cada prueba. Las copias se hacen con el
/// copiador real del backup (Block 4) en una carpeta cuya ruta lleva un nombre testigo que nunca tiene que aparecer en la
/// salida (D-12).
/// </summary>
public sealed class ComandoRestaurarBackupTests
{
    /// <summary>Un nombre testigo dentro de la ruta de las copias: si aparece en la salida, la ruta se filtró.</summary>
    private const string Testigo = "Juana Testigo Quimera";

    private const string MensajeEnMarcha = "La aplicación está en marcha. Detenela antes de restaurar un backup.";

    private const string MigracionVieja = "20261116120000_DatosEmisor";

    private static readonly DateOnly FechaCopia = new(2026, 10, 30);

    [Fact]
    public async Task Restaurar_CopiaValida_AbrePresupuestoDeLaCopia()
    {
        using var escenario = new Escenario();
        string copia;
        using (var origen = new FabricaApi(directorioDatos: escenario.Origen))
        {
            await Http.DefinirAsync(origen.CrearClienteSinCookies());
            await SiembraFacturas.SembrarAsync(origen.Services, [Factura(7, 1)]);
            // La copia se hace con la Api en marcha, como el backup diario.
            copia = await escenario.HacerCopiaAsync(escenario.Origen, FechaCopia);
        }

        // Una instalación nueva: la Api se inició una vez (crea la base vacía), alguien definió una contraseña y quedó con
        // una sesión abierta, y la Api se detuvo.
        string cookieDeAntes;
        using (var instalada = new FabricaApi(directorioDatos: escenario.Datos))
        {
            var cliente = instalada.CrearClienteSinCookies();
            var estado = await Http.JsonAsync(await Http.GetAsync(cliente, "/api/acceso/estado"));
            Assert.False(estado.GetProperty("contrasenaDefinida").GetBoolean());
            cookieDeAntes = await Http.DefinirAsync(cliente, "otra-clave-de-la-instalacion");
            Assert.Equal(HttpStatusCode.NoContent, (await Http.GetAsync(cliente, "/api/sesion", cookieDeAntes)).StatusCode);
        }

        FabricaApi.LiberarConexiones(escenario.Datos);
        var hashCopia = Hash(copia);
        var inicio = TimeProvider.System.GetTimestamp();

        var (codigo, salida) = await EjecutarAsync(escenario.Datos, "RESTAURAR\n", copia);

        Assert.True(codigo == 0, salida);
        // NFR-01: minutos, no horas (la medición en la PC del local es manual, D-14).
        Assert.True(TimeProvider.System.GetElapsedTime(inicio) < TimeSpan.FromMinutes(5));
        Assert.Contains("optica-2026-10-30.db", salida, StringComparison.Ordinal);
        Assert.Contains("30/10/2026", salida, StringComparison.Ordinal);
        Assert.Contains("RESTAURAR", salida, StringComparison.Ordinal);
        // R-07 y D-15: recuerda revisar en ARCA y conservar anterior/ bajo llave.
        Assert.Contains("ARCA", salida, StringComparison.Ordinal);
        Assert.Contains("bajo llave", salida, StringComparison.Ordinal);
        Assert.Contains("anterior" + Path.DirectorySeparatorChar, salida, StringComparison.Ordinal);
        AfirmarSinRutas(salida, escenario);

        // AC-14: la Api arranca sobre la base restaurada; se ingresa con la contraseña de la copia y se abre su presupuesto.
        using (var restaurada = new FabricaApi(directorioDatos: escenario.Datos))
        {
            var cliente = restaurada.CrearClienteSinCookies();
            // La sesión abierta antes de restaurar ya no vale: la restauración renovó las claves de sesión.
            Assert.Equal(HttpStatusCode.Unauthorized, (await Http.GetAsync(cliente, "/api/sesion", cookieDeAntes)).StatusCode);
            var ingreso = await Http.PostJsonAsync(cliente, "/api/acceso/ingresar", Http.CuerpoContrasena(Http.Contrasena));
            Assert.Equal(HttpStatusCode.NoContent, ingreso.StatusCode);
            var presupuesto = await Http.GetAsync(cliente, "/api/presupuestos/7", Http.Cookie(ingreso));
            Assert.Equal(HttpStatusCode.OK, presupuesto.StatusCode);
            var json = await Http.JsonAsync(presupuesto);
            Assert.Equal("Restaurada", json.GetProperty("cliente").GetProperty("nombre").GetString());
        }

        FabricaApi.LiberarConexiones(escenario.Datos);
        // La base de la instalación quedó apartada; la copia no cambió y no quedó el temporal.
        var anterior = Assert.Single(Directory.GetDirectories(Path.Combine(escenario.Datos, "anterior")));
        Assert.True(File.Exists(Path.Combine(anterior, FabricaConexion.NombreArchivo)));
        Assert.False(File.Exists(Path.Combine(escenario.Datos, FabricaConexion.NombreArchivo + ".restaurando")));
        Assert.Equal(hashCopia, Hash(copia));
    }

    [Fact]
    public async Task Restaurar_ApiEnMarcha_Rechazado()
    {
        using var escenario = new Escenario();
        using var api = new FabricaApi(directorioDatos: escenario.Datos);
        var cliente = api.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        await SiembraFacturas.SembrarAsync(api.Services, [Factura(7, 1)]);
        var copia = await escenario.HacerCopiaAsync(escenario.Datos, FechaCopia);
        await SiembraFacturas.SembrarAsync(api.Services, [Factura(8, 2)]);

        var (codigo, salida) = await EjecutarAsync(escenario.Datos, "RESTAURAR\n", copia, liberar: false);

        Assert.Equal(2, codigo);
        Assert.Equal(MensajeEnMarcha, salida.Trim());
        // Nada cambió: la Api sigue con su base (el presupuesto posterior a la copia sigue ahí) y sin anterior/.
        Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync(cliente, "/api/presupuestos/8", cookie)).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(escenario.Datos, "anterior")));
        Assert.False(File.Exists(Path.Combine(escenario.Datos, FabricaConexion.NombreArchivo + ".restaurando")));
    }

    [Theory]
    [InlineData("restaurar\n")]
    [InlineData("RESTAURARLA\n")]
    [InlineData("RES TAURAR\n")]
    [InlineData("RESTABLECER\n")]
    [InlineData("\n")]
    [InlineData("")]
    public async Task Restaurar_SinConfirmacion_Rechazado(string entrada)
    {
        using var escenario = new Escenario();
        var copia = await escenario.PrepararCopiaYDatosAsync();
        var antes = Foto(escenario.Datos);

        var (codigo, salida) = await EjecutarAsync(escenario.Datos, entrada, copia);

        Assert.Equal(1, codigo);
        Assert.Contains("cancel", salida, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No se cambió nada", salida, StringComparison.Ordinal);
        Assert.Equal(antes, Foto(escenario.Datos));
        AfirmarSinRutas(salida, escenario);
    }

    [Fact]
    public async Task Restaurar_InformaFacturasPosteriores()
    {
        using var escenario = new Escenario();
        string copiaVieja;
        string copiaAlDia;
        using (var api = new FabricaApi(directorioDatos: escenario.Datos))
        {
            await Http.DefinirAsync(api.CrearClienteSinCookies());
            await SiembraFacturas.SembrarAsync(api.Services, [Factura(1, 1), Factura(2, 2)]);
            copiaVieja = await escenario.HacerCopiaAsync(escenario.Datos, FechaCopia);
            await SiembraFacturas.SembrarAsync(api.Services, [Factura(3, 3), Factura(4, 4), Factura(5, 5)]);
            copiaAlDia = await escenario.HacerCopiaAsync(escenario.Datos, FechaCopia.AddDays(1));
        }

        FabricaApi.LiberarConexiones(escenario.Datos);

        var (codigo, salida) = await EjecutarAsync(escenario.Datos, "no\n", copiaVieja);
        var (_, salidaAlDia) = await EjecutarAsync(escenario.Datos, "no\n", copiaAlDia);

        // R-10, D-29: antes de pedir RESTAURAR, cuántas facturas con CAE quedarían solo en anterior/.
        Assert.Equal(1, codigo);
        var aviso = salida.IndexOf("3 facturas con CAE posteriores a la última de la copia", StringComparison.Ordinal);
        Assert.True(aviso >= 0, salida);
        Assert.True(aviso < salida.IndexOf("Escribí RESTAURAR", StringComparison.Ordinal), salida);
        Assert.Contains("no tiene facturas con CAE posteriores a la última de la copia", salidaAlDia, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restaurar_CopiaVieja_LaApiMigraAlArrancar()
    {
        using var escenario = new Escenario();
        // Una base de una versión anterior: sin las migraciones del backup (FEAT-010).
        await using (var app = ConstruirComando(escenario.Origen))
        {
            using var alcance = app.Services.CreateScope();
            alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().GetService<IMigrator>().Migrate(MigracionVieja);
        }

        FabricaApi.LiberarConexiones(escenario.Origen);
        var copia = await escenario.HacerCopiaAsync(escenario.Origen, FechaCopia);
        var prefijo = Consultar(copia, "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId");
        Assert.Equal(MigracionVieja, prefijo[^1]);
        await escenario.InstalarAsync();

        var (codigo, salida) = await EjecutarAsync(escenario.Datos, "RESTAURAR\n", copia);

        Assert.True(codigo == 0, salida);
        Assert.Contains("versión anterior", salida, StringComparison.Ordinal);
        // D-28: el comando no migra.
        var restaurada = Path.Combine(escenario.Datos, FabricaConexion.NombreArchivo);
        Assert.Equal(prefijo, Consultar(restaurada, "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId"));
        Assert.Empty(Consultar(restaurada, "SELECT name FROM sqlite_master WHERE name = 'ConfiguracionBackup'"));

        // La Api migra al arrancar y queda al día.
        List<string> todas;
        using (var api = new FabricaApi(directorioDatos: escenario.Datos))
        {
            Assert.Equal(
                HttpStatusCode.OK, (await Http.GetAsync(api.CrearClienteSinCookies(), "/api/acceso/estado")).StatusCode);
            using var alcance = api.Services.CreateScope();
            todas = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>().Database.GetMigrations().ToList();
        }

        FabricaApi.LiberarConexiones(escenario.Datos);
        Assert.Equal(todas, Consultar(restaurada, "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId"));
        Assert.Equal(["ConfiguracionBackup"], Consultar(restaurada, "SELECT name FROM sqlite_master WHERE name = 'ConfiguracionBackup'"));
    }

    [Theory]
    [InlineData("inexistente", "No se encontró la copia optica-2026-10-30.db")]
    [InlineData("nombre", "no es una copia de backup del sistema")]
    [InlineData("danada", "La copia optica-2026-10-30.db está dañada")]
    [InlineData("ajena", "no es una base del sistema de la óptica")]
    public async Task Comando_CopiaInvalida_FallaSinTocarLaBase(string caso, string mensaje)
    {
        using var escenario = new Escenario();
        var copia = await escenario.PrepararCopiaYDatosAsync();
        switch (caso)
        {
            case "inexistente":
                File.Delete(copia);
                break;
            case "nombre":
                var otra = Path.Combine(escenario.Copias, $"backup de {Testigo}.db");
                File.Move(copia, otra);
                copia = otra;
                break;
            case "danada":
                File.WriteAllBytes(copia, RandomNumberGenerator.GetBytes(8192));
                break;
            default:
                File.Delete(copia);
                Ejecutar(copia, "CREATE TABLE Otra (Id INTEGER PRIMARY KEY); PRAGMA journal_mode=DELETE;");
                break;
        }

        var antes = Foto(escenario.Datos);

        var (codigo, salida) = await EjecutarAsync(escenario.Datos, "RESTAURAR\n", copia);

        Assert.Equal(2, codigo);
        Assert.Contains(mensaje, salida, StringComparison.Ordinal);
        Assert.Contains("No se cambió nada", salida, StringComparison.Ordinal);
        // No llegó a pedir la confirmación.
        Assert.DoesNotContain("Escribí RESTAURAR", salida, StringComparison.Ordinal);
        Assert.Equal(antes, Foto(escenario.Datos));
        AfirmarSinRutas(salida, escenario);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Comando_ArgumentosIncorrectos_FallaSinTocarLaBase(int extras)
    {
        using var escenario = new Escenario();
        var copia = await escenario.PrepararCopiaYDatosAsync();
        var antes = Foto(escenario.Datos);
        string[] argumentos = extras == 0
            ? [ComandoRestaurarBackup.Nombre]
            : [ComandoRestaurarBackup.Nombre, copia, "--forzar"];

        var (codigo, salida) = await EjecutarAsync(escenario.Datos, "RESTAURAR\n", argumentos: argumentos);

        Assert.Equal(2, codigo);
        Assert.Contains("Uso: Optica.Api restaurar-backup", salida, StringComparison.Ordinal);
        Assert.Equal(antes, Foto(escenario.Datos));
        AfirmarSinRutas(salida, escenario);
    }

    [Fact]
    public async Task Comando_BaseInexistente_FallaSinCrearNada()
    {
        using var escenario = new Escenario();
        var copia = await escenario.PrepararCopiaYDatosAsync();
        var vacia = Path.Combine(escenario.Raiz, "vacia");
        Directory.CreateDirectory(vacia);

        var (codigo, salida) = await EjecutarAsync(vacia, "RESTAURAR\n", copia);

        Assert.Equal(2, codigo);
        Assert.Contains("No se encontró la base de datos", salida, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(vacia));
        AfirmarSinRutas(salida, escenario);
    }

    [Theory]
    [InlineData("temporal", "la base actual no se tocó")]
    [InlineData("intercambio", "la base actual volvió a su lugar")]
    [InlineData("vuelta", "no inicies la aplicación")]
    public async Task Comando_FallaLaRestauracion_InformaSinRutasNiTexto(string falla, string mensaje)
    {
        var candado = new CandadoFalso();

        var (codigo, texto) = await EjecutarConDoblesAsync(new RestauradorFalso(falla), candado: candado);

        Assert.Equal(2, codigo);
        Assert.Contains(mensaje, texto, StringComparison.Ordinal);
        Assert.Contains(nameof(IOException), texto, StringComparison.Ordinal);
        Assert.DoesNotContain("usuario-privado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(Testigo, texto, StringComparison.Ordinal);
        if (falla == "vuelta")
        {
            Assert.Contains(Path.Combine("anterior", "20261117-101500"), texto, StringComparison.Ordinal);
        }

        // El candado se mantiene hasta terminar.
        Assert.True(candado.Liberado);
    }

    [Fact]
    public async Task Comando_FallaAlRenovarClaves_AvisaQueSeRestauroSinRutas()
    {
        var datos = new DatosFalsos { FallaAlRenovar = new IOException("/home/usuario-privado/datos/claves/key-1.xml") };

        var (codigo, texto) = await EjecutarConDoblesAsync(new RestauradorFalso(), datos: datos);

        // Como restablecer: la base ya está restaurada (no se deshace), pero las sesiones podrían seguir valiendo.
        Assert.Equal(2, codigo);
        Assert.Equal(1, datos.Renovaciones);
        Assert.Contains("Base restaurada desde la copia optica-2026-10-30.db", texto, StringComparison.Ordinal);
        Assert.Contains("ARCA", texto, StringComparison.Ordinal);
        Assert.Contains("no se pudieron renovar las claves de sesión (IOException)", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("usuario-privado", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(Testigo, texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comando_FacturasSinContar_AvisaQuePuedeHaberFacturas()
    {
        var (codigo, texto) = await EjecutarConDoblesAsync(new RestauradorFalso { FacturasPosteriores = null }, "no\n");

        Assert.Equal(1, codigo);
        var aviso = texto.IndexOf("no se pudo leer la base actual para contar sus facturas con CAE", StringComparison.Ordinal);
        Assert.True(aviso >= 0, texto);
        Assert.True(aviso < texto.IndexOf("Escribí RESTAURAR", StringComparison.Ordinal), texto);
        Assert.Contains("quedan solo en la carpeta anterior", texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sinDirectorio", "No se encontró la base de datos en la carpeta de datos.")]
    [InlineData("otraFalla", "No se pudo comprobar si la aplicación está en marcha (IOException). No se cambió nada.")]
    [InlineData("tomado", MensajeEnMarcha)]
    public async Task Comando_CandadoNoDisponible_FallaSinTocarNada(string caso, string mensaje)
    {
        var restaurador = new RestauradorFalso();
        var candado = new CandadoFalso(() => caso switch
        {
            "sinDirectorio" => throw new DirectoryNotFoundException("/home/usuario-privado/datos"),
            "otraFalla" => throw new IOException("/home/usuario-privado/datos/optica.instancia"),
            _ => null,
        });

        var (codigo, texto) = await EjecutarConDoblesAsync(restaurador, candado: candado);

        Assert.Equal(2, codigo);
        Assert.StartsWith(mensaje, texto.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain("usuario-privado", texto, StringComparison.Ordinal);
        Assert.Empty(restaurador.Pasos);
    }

    [Fact]
    public async Task Comando_FallaAlVerificarAntesDeConfirmar_NoPideConfirmacion()
    {
        var restaurador = new RestauradorFalso { FallaDesdeLaInspeccion = 1 };

        var (codigo, texto) = await EjecutarConDoblesAsync(restaurador);

        Assert.Equal(2, codigo);
        Assert.Equal("No se pudo verificar la copia (IOException). No se cambió nada.", texto.Trim());
        Assert.Equal(["inspeccionar"], restaurador.Pasos);
    }

    [Fact]
    public async Task Comando_FallaAlVerificarDespuesDeConfirmar_DiceQueLaBaseNoSeModifico()
    {
        // La copia se verificó, se confirmó, y al volver a verificarla (el pendrive se desconectó) falla: todavía no se
        // tocó nada, y el mensaje lo dice.
        var restaurador = new RestauradorFalso { FallaDesdeLaInspeccion = 2 };

        var (codigo, texto) = await EjecutarConDoblesAsync(restaurador);

        Assert.Equal(2, codigo);
        Assert.Contains("No se pudo verificar la copia (IOException): la base actual no se modificó.", texto, StringComparison.Ordinal);
        Assert.Equal(["inspeccionar", "inspeccionar"], restaurador.Pasos);
        Assert.DoesNotContain("usuario-privado", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comando_CtrlC_BloqueadoSoloMientrasRestaura()
    {
        var bloqueo = new BloqueoFalso();
        var restaurador = new RestauradorFalso { Bloqueo = bloqueo };

        var (codigo, _) = await EjecutarConDoblesAsync(restaurador, bloqueo: bloqueo);

        // Mientras se espera la confirmación, Ctrl+C cancela; desde la confirmación hasta el final de la restauración,
        // no se puede cortar.
        Assert.Equal(0, codigo);
        Assert.Equal(1, bloqueo.Veces);
        Assert.False(bloqueo.Puesto);
        Assert.Equal(
            ["inspeccionar", "inspeccionar (bloqueado)", "temporal (bloqueado)", "intercambiar (bloqueado)"], restaurador.Pasos);
    }

    [Fact]
    public async Task Comando_Cancelado_NoBloqueaCtrlC()
    {
        var bloqueo = new BloqueoFalso();

        var (codigo, _) = await EjecutarConDoblesAsync(new RestauradorFalso { Bloqueo = bloqueo }, "no\n", bloqueo: bloqueo);

        Assert.Equal(1, codigo);
        Assert.Equal(0, bloqueo.Veces);
    }

    /// <summary>
    /// WARN-3: si falta <c>optica.db</c> y la carpeta de datos tiene un temporal de restauración o una carpeta
    /// <c>anterior</c>, la Api no arranca ni crea una base vacía (la base real puede estar en <c>anterior</c>). El crítico
    /// remite a la guía, sin rutas.
    /// </summary>
    [Theory]
    [InlineData("restaurando")]
    [InlineData("anterior")]
    public void Arranque_RestauracionSinTerminar_NoCreaUnaBaseNueva(string resto)
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            if (resto == "restaurando")
            {
                File.WriteAllBytes(Path.Combine(directorio, FabricaConexion.NombreArchivo + ".restaurando"), [1, 2, 3]);
            }
            else
            {
                var anterior = Path.Combine(directorio, "anterior", "20261117-101500");
                Directory.CreateDirectory(anterior);
                File.WriteAllBytes(Path.Combine(anterior, FabricaConexion.NombreArchivo), [1, 2, 3]);
            }

            using var api = new FabricaApi(directorioDatos: directorio);

            Assert.ThrowsAny<Exception>(() => api.CreateClient());
            Assert.False(File.Exists(Path.Combine(directorio, FabricaConexion.NombreArchivo)));
            var critico = Assert.Single(api.Registro.Lineas, l => l.Nivel == Microsoft.Extensions.Logging.LogLevel.Critical);
            Assert.Contains("restauración", critico.Mensaje, StringComparison.Ordinal);
            Assert.Contains("sección 10", critico.Mensaje, StringComparison.Ordinal);
            Assert.DoesNotContain(directorio, critico.TextoCompleto, StringComparison.Ordinal);
        }
        finally
        {
            FabricaApi.BorrarDirectorioDePrueba(directorio);
        }
    }

    [Fact]
    public async Task Arranque_InstalacionLimpia_CreaLaBase()
    {
        var directorio = FabricaApi.NuevoDirectorioTemporal();
        try
        {
            using (var api = new FabricaApi(directorioDatos: directorio))
            {
                Assert.Equal(
                    HttpStatusCode.OK, (await Http.GetAsync(api.CrearClienteSinCookies(), "/api/acceso/estado")).StatusCode);
            }

            Assert.True(File.Exists(Path.Combine(directorio, FabricaConexion.NombreArchivo)));
        }
        finally
        {
            FabricaApi.BorrarDirectorioDePrueba(directorio);
        }
    }

    private static FacturaSembrada Factura(long presupuesto, long numero) =>
        new(presupuesto, new DateOnly(2026, 10, 20), "Ficticio", "Restaurada", "00222333", 3, numero);

    private static void AfirmarSinRutas(string salida, Escenario escenario)
    {
        Assert.DoesNotContain(Testigo, salida, StringComparison.Ordinal);
        Assert.DoesNotContain(escenario.Raiz, salida, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), salida, StringComparison.Ordinal);
    }

    /// <summary>Los archivos y carpetas de primer nivel de la carpeta de datos, con el hash de cada archivo.</summary>
    private static Dictionary<string, string> Foto(string directorio) =>
        Directory.EnumerateFileSystemEntries(directorio).ToDictionary(
            entrada => Path.GetFileName(entrada),
            entrada => File.Exists(entrada) ? Hash(entrada) : "carpeta");

    private static string Hash(string archivo) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivo)));

    /// <summary>Arma la aplicación como la rama del comando de <c>Program</c>, sobre el directorio de datos indicado.</summary>
    private static WebApplication ConstruirComando(string directorio)
    {
        var builder = WebApplication.CreateBuilder(Program.OpcionesDelComando());
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [FabricaConexion.ClaveDirectorio] = directorio });
        Program.ConfigurarServicios(builder);
        return builder.Build();
    }

    /// <summary>El comando completo, por el selector de <c>Program</c>, con la copia como único argumento.</summary>
    private static async Task<(int Codigo, string Salida)> EjecutarAsync(
        string directorio, string entrada, string? copia = null, string[]? argumentos = null, bool liberar = true)
    {
        using var salida = new StringWriter();
        var codigo = await Program.EjecutarComandoAsync(
            () => ConstruirComando(directorio),
            argumentos ?? [ComandoRestaurarBackup.Nombre, copia!],
            new StringReader(entrada),
            salida);
        if (liberar)
        {
            FabricaApi.LiberarConexiones(directorio);
        }

        return (codigo, salida.ToString());
    }

    private static List<string> Consultar(string baseDeDatos, string sql)
    {
        using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = baseDeDatos,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        conexion.Open();
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

    private static void Ejecutar(string baseDeDatos, string sql)
    {
        using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = baseDeDatos,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    /// <summary>
    /// Una raíz temporal con la carpeta de datos de origen, la de la restauración y la de las copias (con el nombre
    /// testigo en su ruta). Se borra entera al terminar.
    /// </summary>
    private sealed class Escenario : IDisposable
    {
        public Escenario()
        {
            Raiz = FabricaApi.NuevoDirectorioTemporal();
            Origen = Path.Combine(Raiz, "origen");
            Datos = Path.Combine(Raiz, "datos");
            Copias = Path.Combine(Raiz, "Pendrive de " + Testigo);
            Directory.CreateDirectory(Origen);
            Directory.CreateDirectory(Datos);
            Directory.CreateDirectory(Copias);
        }

        public string Raiz { get; }

        public string Origen { get; }

        public string Datos { get; }

        public string Copias { get; }

        /// <summary>Hace la copia de <paramref name="fecha"/> de la base de <paramref name="directorio"/> con el copiador del backup.</summary>
        public async Task<string> HacerCopiaAsync(string directorio, DateOnly fecha)
        {
            var copia = Path.Combine(Copias, $"optica-{fecha:yyyy-MM-dd}.db");
            var copiador = new CopiadorBackupSqlite(new FabricaConexion(directorio));
            await copiador.CopiarAsync(copia + ".parcial");
            Assert.True(await copiador.VerificarAsync(copia + ".parcial"));
            File.Move(copia + ".parcial", copia);
            return copia;
        }

        /// <summary>Inicia y detiene la Api una vez sobre la carpeta de datos: la instalación nueva.</summary>
        public async Task InstalarAsync()
        {
            using (var instalada = new FabricaApi(directorioDatos: Datos))
            {
                await Http.GetAsync(instalada.CrearClienteSinCookies(), "/api/acceso/estado");
            }

            FabricaApi.LiberarConexiones(Datos);
        }

        /// <summary>Una copia de la base de origen con un presupuesto facturado, y la carpeta de datos instalada.</summary>
        public async Task<string> PrepararCopiaYDatosAsync()
        {
            string copia;
            using (var origen = new FabricaApi(directorioDatos: Origen))
            {
                await Http.DefinirAsync(origen.CrearClienteSinCookies());
                await SiembraFacturas.SembrarAsync(origen.Services, [Factura(7, 1)]);
                copia = await HacerCopiaAsync(Origen, FechaCopia);
            }

            FabricaApi.LiberarConexiones(Origen);
            using (var datos = new FabricaApi(directorioDatos: Datos))
            {
                var cliente = datos.CrearClienteSinCookies();
                await Http.DefinirAsync(cliente);
                await SiembraFacturas.SembrarAsync(datos.Services, [Factura(7, 1), Factura(8, 2)]);
            }

            FabricaApi.LiberarConexiones(Datos);
            return copia;
        }

        public void Dispose() => FabricaApi.BorrarDirectorioDePrueba(Raiz);
    }

    /// <summary>
    /// Arma el comando con dobles: el restaurador, los datos locales y el candado que reciba, y un bloqueo de Ctrl+C que
    /// anota cuándo estuvo puesto. La ruta de la copia lleva el nombre testigo.
    /// </summary>
    private static async Task<(int Codigo, string Salida)> EjecutarConDoblesAsync(
        RestauradorFalso restaurador,
        string entrada = "RESTAURAR\n",
        DatosFalsos? datos = null,
        CandadoFalso? candado = null,
        BloqueoFalso? bloqueo = null)
    {
        datos ??= new DatosFalsos();
        using var salida = new StringWriter();
        var comando = new ComandoRestaurarBackup(
            new RestaurarBackup(restaurador, datos),
            datos,
            candado ?? new CandadoFalso(),
            Path.Combine(Path.GetTempPath(), Testigo, "optica-2026-10-30.db"),
            new StringReader(entrada),
            salida,
            bloqueo is null ? null : bloqueo.Poner);
        var codigo = await comando.EjecutarAsync();
        return (codigo, salida.ToString());
    }

    /// <summary>
    /// Un restaurador que acepta la copia y falla en el paso pedido, con un texto que lleva una ruta privada. Anota sus
    /// pasos y, si recibe un bloqueo, si estaba puesto en cada uno.
    /// </summary>
    private sealed class RestauradorFalso(string falla = "ninguna") : IRestauradorBase
    {
        private static readonly string[] Conocidas = ["20261030220627_Inicial"];

        private int _inspecciones;

        public int? FacturasPosteriores { get; init; }

        /// <summary>Desde qué inspección (1 = la primera) lanza.</summary>
        public int? FallaDesdeLaInspeccion { get; init; }

        public BloqueoFalso? Bloqueo { get; init; }

        public List<string> Pasos { get; } = [];

        public IReadOnlyList<string> MigracionesConocidas() => Conocidas;

        public Task<InspeccionCopia> InspeccionarCopiaAsync(string rutaCopia, CancellationToken cancelacion = default)
        {
            Anotar("inspeccionar");
            return ++_inspecciones >= FallaDesdeLaInspeccion
                ? Task.FromException<InspeccionCopia>(new IOException("/home/usuario-privado/pendrive"))
                : Task.FromResult(new InspeccionCopia(true, true, Conocidas, 0));
        }

        public Task<int?> ContarFacturasPosterioresAsync(long ultimaFacturaDeLaCopia, CancellationToken cancelacion = default) =>
            Task.FromResult(FacturasPosteriores);

        public Task<InspeccionCopia> RestaurarATemporalAsync(string rutaCopia, CancellationToken cancelacion = default)
        {
            Anotar("temporal");
            return falla == "temporal"
                ? Task.FromException<InspeccionCopia>(new IOException("/home/usuario-privado/datos"))
                : Task.FromResult(new InspeccionCopia(true, true, Conocidas, 0));
        }

        public string ApartarYPonerRestaurada()
        {
            Anotar("intercambiar");
            return falla switch
            {
                "intercambio" => throw new IOException("/home/usuario-privado/datos"),
                "vuelta" => throw new VueltaAtrasFallidaException(
                    Path.Combine("anterior", "20261117-101500"), new IOException("/home/usuario-privado/datos")),
                _ => Path.Combine("anterior", "20261117-101500"),
            };
        }

        public void DescartarTemporal()
        {
        }

        private void Anotar(string paso) => Pasos.Add(Bloqueo?.Puesto == true ? paso + " (bloqueado)" : paso);
    }

    /// <summary>Datos locales de prueba: la base existe y está al día; renovar las claves puede fallar.</summary>
    private sealed class DatosFalsos : IDatosLocales
    {
        public Exception? FallaAlRenovar { get; init; }

        public int Renovaciones { get; private set; }

        public string Directorio => "/home/usuario-privado/datos";

        public string DirectorioClaves => "/home/usuario-privado/datos/claves";

        public string RutaVisible => "~/datos";

        public bool BaseExiste => true;

        public bool MigracionesPendientes() => false;

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

    /// <summary>Candado de prueba: se toma (o falla como se pida) y anota si se liberó.</summary>
    private sealed class CandadoFalso(Func<IDisposable?>? adquirir = null) : ICandadoInstancia
    {
        public bool Liberado { get; private set; }

        public IDisposable? IntentarAdquirir() => adquirir is null ? new Liberacion(this) : adquirir();

        private sealed class Liberacion(CandadoFalso candado) : IDisposable
        {
            public void Dispose() => candado.Liberado = true;
        }
    }

    /// <summary>El bloqueo de Ctrl+C de prueba: anota cuántas veces se puso y si está puesto.</summary>
    private sealed class BloqueoFalso
    {
        public bool Puesto { get; private set; }

        public int Veces { get; private set; }

        public IDisposable Poner()
        {
            Assert.False(Puesto, "El bloqueo ya estaba puesto.");
            Puesto = true;
            Veces++;
            return new Quitar(this);
        }

        private sealed class Quitar(BloqueoFalso bloqueo) : IDisposable
        {
            public void Dispose() => bloqueo.Puesto = false;
        }
    }
}
