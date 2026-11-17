using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Application.Backup;
using Optica.Application.Comun;
using Optica.Domain.Backup;

namespace Optica.Api.Tests.Backup;

/// <summary>
/// <c>GET /api/backup/estado</c> (FEAT-010, Block 6, FR-09): con sesión, el estado del backup para el aviso al ingresar,
/// con la base real de las pruebas. El reloj de <see cref="FabricaApi"/> está en el 31/10/2026 (UTC, la zona del reloj
/// falso). Las carpetas son temporales y llevan un valor testigo que no puede salir en la respuesta ni en el log (D-12).
/// </summary>
public sealed class EstadoBackupApiTests : IDisposable
{
    private const string Ruta = "/api/backup/estado";

    /// <summary>Valor testigo dentro de la ruta: no puede aparecer en la respuesta ni en el log.</summary>
    private const string Testigo = "Zq7TestigoEstado";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "optica-pruebas-estado-backup", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (!Directory.Exists(_raiz))
        {
            return;
        }

        // Una carpeta que la prueba dejó sin permisos vuelve a tenerlos para poder borrarla.
        if (!OperatingSystem.IsWindows())
        {
            foreach (var carpeta in Directory.EnumerateDirectories(_raiz, "*", SearchOption.TopDirectoryOnly))
            {
                File.SetUnixFileMode(carpeta, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(_raiz, recursive: true);
    }

    [Fact]
    public async Task GetEstado_SinRutas()
    {
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var carpeta = Path.Combine(_raiz, Testigo, "Backups");
        Directory.CreateDirectory(carpeta);
        await SembrarAsync(fabrica, carpeta, new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 29));

        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);

        // AC-09: falta el del 30/10 y el último es el del 29/10; la carpeta existe y se puede leer.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var texto = await respuesta.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(texto).RootElement;
        Assert.Equal(
            ["faltaAyer", "ultimo", "ubicacion", "accesible"],
            json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.True(json.GetProperty("faltaAyer").GetBoolean());
        Assert.Equal("2026-10-29", json.GetProperty("ultimo").GetString());
        Assert.Equal("elegida", json.GetProperty("ubicacion").GetString());
        Assert.True(json.GetProperty("accesible").GetBoolean());

        // Sin rutas (D-12): ni en la respuesta ni en el log, de ningún nivel.
        Assert.DoesNotContain(Testigo, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(_raiz, texto, StringComparison.Ordinal);
        foreach (var linea in fabrica.Registro.Lineas)
        {
            Assert.DoesNotContain(Testigo, linea.TextoCompleto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetEstado_BaseNueva_SinBackupNiUbicacion()
    {
        // AC-10: una base nueva no tiene backups ni ubicación elegida.
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.True(json.GetProperty("faltaAyer").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("ultimo").ValueKind);
        Assert.Equal("sin_elegir", json.GetProperty("ubicacion").GetString());
        Assert.False(json.GetProperty("accesible").GetBoolean());
    }

    [Fact]
    public async Task GetEstado_ConLaDeHoy_NoFalta()
    {
        // D-11: la copia hecha hoy al arrancar cubre la de ayer.
        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var carpeta = Path.Combine(_raiz, "Backups");
        Directory.CreateDirectory(carpeta);
        await SembrarAsync(fabrica, carpeta, new DateOnly(2026, 10, 31));

        var json = await Http.JsonAsync(await Http.GetAsync(cliente, Ruta, cookie));

        Assert.False(json.GetProperty("faltaAyer").GetBoolean());
        Assert.Equal("2026-10-31", json.GetProperty("ultimo").GetString());
    }

    [Theory]
    [InlineData("inexistente")]
    [InlineData("sin permiso")]
    public async Task GetEstado_CarpetaIlegible_AccesibleFalseSin500(string caso)
    {
        if (caso == "sin permiso" && OperatingSystem.IsWindows())
        {
            // En Windows quitar la lectura exige ACL; el caso lo cubre la prueba con el almacén que no puede leer
            // (GetEstado_SinPermisoDeLectura_AccesibleFalseSinRuta).
            return;
        }

        var carpeta = Path.Combine(_raiz, Testigo + "-" + caso.Replace(' ', '-'));
        if (caso == "sin permiso" && !QuitarLectura(carpeta))
        {
            // Como root el modo 000 no impide listar: la carpeta seguiría legible y el caso no probaría nada.
            return;
        }

        using var fabrica = new FabricaApi();
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);

        // Pendrive desconectado (la carpeta no existe) o carpeta sin permiso de lectura; el historial tiene el 29/10.
        await SembrarAsync(fabrica, carpeta, new DateOnly(2026, 10, 29));

        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);

        // AC-18: 200 con la fecha del historial y la ubicación elegida pero no accesible; nunca 500.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(texto).RootElement;
        Assert.True(json.GetProperty("faltaAyer").GetBoolean());
        Assert.Equal("2026-10-29", json.GetProperty("ultimo").GetString());
        Assert.Equal("elegida", json.GetProperty("ubicacion").GetString());
        Assert.False(json.GetProperty("accesible").GetBoolean());
        Assert.DoesNotContain(Testigo, texto, StringComparison.Ordinal);
        foreach (var linea in fabrica.Registro.Lineas)
        {
            Assert.DoesNotContain(Testigo, linea.TextoCompleto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetEstado_SinPermisoDeLectura_AccesibleFalseSinRuta()
    {
        // El almacén no puede leer la carpeta (como en Windows sin permiso de lectura): ni 500 ni la ruta en el log.
        using var fabrica = new FabricaApi(reemplazos: servicios =>
        {
            servicios.RemoveAll<IAlmacenCopias>();
            servicios.AddSingleton<IAlmacenCopias, AlmacenSinPermiso>();
        });
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        await SembrarAsync(fabrica, Path.Combine(_raiz, Testigo), new DateOnly(2026, 10, 29));

        var respuesta = await Http.GetAsync(cliente, Ruta, cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False((await Http.JsonAsync(respuesta)).GetProperty("accesible").GetBoolean());
        foreach (var linea in fabrica.Registro.Lineas)
        {
            Assert.DoesNotContain(Testigo, linea.TextoCompleto, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Crea la carpeta con modo 000 (solo fuera de Windows) y comprueba que de verdad no se pueda listar; falso si se puede
    /// (por ejemplo, corriendo como root).
    /// </summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static bool QuitarLectura(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        File.SetUnixFileMode(carpeta, UnixFileMode.None);
        try
        {
            using var entradas = Directory.EnumerateFileSystemEntries(carpeta).GetEnumerator();
            entradas.MoveNext();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Graba la ubicación y las fechas del historial en la base de la Api, como lo harían la configuración y el backup.</summary>
    private static async Task SembrarAsync(FabricaApi fabrica, string carpeta, params DateOnly[] fechas)
    {
        using var alcance = fabrica.Services.CreateScope();
        var servicios = alcance.ServiceProvider;
        servicios.GetRequiredService<IUbicacionBackupRepository>()
            .Reemplazar(UbicacionBackup.ParaGrabar(carpeta).Ubicacion!);
        var historial = servicios.GetRequiredService<IHistorialBackupRepository>();
        foreach (var fecha in fechas)
        {
            historial.Agregar(fecha, fabrica.Reloj.GetUtcNow());
        }

        await servicios.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
    }

    /// <summary>Un almacén que no puede leer la carpeta; si alguien la listara, lanzaría con la ruta en el texto.</summary>
    private sealed class AlmacenSinPermiso : IAlmacenCopias
    {
        public bool SePuedeLeer(string carpeta) => false;

        public IReadOnlyList<DateOnly> ListarCopias(string carpeta) =>
            throw new UnauthorizedAccessException($"Access to the path '{carpeta}' is denied.");

        public string RutaTemporal(string carpeta, DateOnly fecha) => throw new NotSupportedException();

        public IReadOnlyList<DateOnly> ListarTemporalesSinCopia(string carpeta) => throw new NotSupportedException();

        public void RenombrarTemporal(string carpeta, DateOnly fecha) => throw new NotSupportedException();

        public int BorrarCopias(string carpeta, IReadOnlyCollection<DateOnly> fechas) => throw new NotSupportedException();

        public void BorrarTemporales(string carpeta) => throw new NotSupportedException();
    }
}
