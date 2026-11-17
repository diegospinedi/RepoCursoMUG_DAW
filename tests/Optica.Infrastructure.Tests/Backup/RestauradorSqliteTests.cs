using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Optica.Application.Backup;
using Optica.Domain.Backup;
using Optica.Infrastructure.Backup;
using Optica.Infrastructure.Persistencia;
using Optica.Infrastructure.Tests.Persistencia;
using Optica.Pruebas.Compartido;

namespace Optica.Infrastructure.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 7 (FR-13, NFR-01; D-23, D-28, D-29; ADR-029): la restauración con SQLite real sobre una carpeta
/// de datos temporal propia de cada prueba, con el caso de uso <see cref="RestaurarBackup"/> y el restaurador real. La
/// carpeta de datos queda como la deja una Api cortada (con <c>-wal</c> y <c>-shm</c>), que es el caso más exigente: los
/// tres archivos se apartan juntos y vuelven juntos.
/// </summary>
public class RestauradorSqliteTests
{
    [Theory]
    [InlineData("basura")]
    [InlineData("truncada")]
    public async Task Restaurar_CopiaCorrupta_Rechazada(string dano)
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        if (dano == "basura")
        {
            File.WriteAllBytes(copia, RandomNumberGenerator.GetBytes(8192));
        }
        else
        {
            var bytes = File.ReadAllBytes(copia);
            File.WriteAllBytes(copia, bytes[..(bytes.Length / 2)]);
        }

        var hashCopia = EntornoRestauracion.Hash(copia);
        var antes = entorno.FotoDatos();
        var caso = entorno.CrearCasoDeUso();

        var verificacion = await caso.VerificarAsync(copia);
        var resultado = await caso.RestaurarAsync(copia);

        Assert.Equal(RechazoCopia.Danada, verificacion.Rechazo);
        Assert.Equal(FallaRestauracion.CopiaRechazada, resultado.Falla);
        // Sin tocar la base: los mismos archivos con los mismos bytes, sin anterior/ ni temporal.
        Assert.Equal(antes, entorno.FotoDatos());
        // La copia se abrió en solo lectura: no cambió ni dejó auxiliares junto a ella.
        Assert.Equal(hashCopia, EntornoRestauracion.Hash(copia));
        Assert.Equal([Path.GetFileName(copia)], entorno.ArchivosCopias());
    }

    [Theory]
    [InlineData(FabricaConexion.NombreArchivo)]
    [InlineData(FabricaConexion.NombreArchivo + "-shm")]
    [InlineData(RestauradorSqlite.NombreTemporal)]
    public async Task Restaurar_FallaAMitad_BaseActualIntacta(string pasoQueFalla)
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        var antes = entorno.FotoDatos();
        Assert.Contains(FabricaConexion.NombreArchivo + "-wal", antes.Keys);
        var restaurador = entorno.CrearRestaurador(antesDeMover: archivo =>
        {
            if (archivo == pasoQueFalla)
            {
                throw new IOException("/home/usuario-privado/datos: el disco se desconectó");
            }
        });

        var resultado = await entorno.CrearCasoDeUso(restaurador).RestaurarAsync(copia);

        // La falla llega con su tipo, nunca con su texto.
        Assert.Equal(FallaRestauracion.Intercambio, resultado.Falla);
        Assert.Equal(nameof(IOException), resultado.TipoError);
        Assert.Null(resultado.CarpetaAnterior);
        // La base actual volvió sola: los mismos archivos (con su -wal y su -shm), con los mismos bytes; el temporal se
        // descartó y no quedó una carpeta anterior vacía.
        Assert.Equal(antes, entorno.FotoDatos());
        Assert.False(Directory.Exists(Path.Combine(entorno.Datos, RestauradorSqlite.CarpetaAnterior)));
        // Y se abre con todo lo que tenía, también lo que estaba solo en el -wal.
        Assert.Equal([1L, 2L], EntornoRestauracion.Presupuestos(Path.Combine(entorno.Datos, FabricaConexion.NombreArchivo)));
    }

    [Fact]
    public async Task Restaurar_CopiaValida_ReemplazaYApartaLaBaseConSusAuxiliares()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        var hashCopia = EntornoRestauracion.Hash(copia);
        var antes = entorno.FotoDatos();

        var resultado = await entorno.CrearCasoDeUso().RestaurarAsync(copia);

        Assert.True(resultado.Hecha, resultado.Falla?.ToString());
        var esperada = Path.Combine(RestauradorSqlite.CarpetaAnterior, "20261117-101500");
        Assert.Equal(esperada, resultado.CarpetaAnterior);

        // En la carpeta de datos queda solo la base restaurada (sin -wal, -shm ni temporal), con lo que tenía la copia, en
        // modo WAL como toda base de la Api (bytes 18 y 19 del encabezado: 2 = WAL): la Api arranca sobre ella.
        Assert.Equal([FabricaConexion.NombreArchivo], entorno.ArchivosDatos());
        var encabezado = File.ReadAllBytes(Path.Combine(entorno.Datos, FabricaConexion.NombreArchivo))[18..20];
        Assert.Equal(new byte[] { 2, 2 }, encabezado);
        Assert.Equal([1L], EntornoRestauracion.Presupuestos(Path.Combine(entorno.Datos, FabricaConexion.NombreArchivo)));

        // La base anterior se apartó entera, con sus auxiliares y los mismos bytes: tiene también lo que estaba en el -wal.
        var anterior = Path.Combine(entorno.Datos, esperada);
        var apartada = Directory.EnumerateFiles(anterior).ToDictionary(archivo => Path.GetFileName(archivo), EntornoRestauracion.HashSalvoShm);
        Assert.Equal(antes, apartada);
        Assert.Equal([1L, 2L], EntornoRestauracion.Presupuestos(Path.Combine(anterior, FabricaConexion.NombreArchivo)));

        // La copia no cambió.
        Assert.Equal(hashCopia, EntornoRestauracion.Hash(copia));
        Assert.Equal([Path.GetFileName(copia)], entorno.ArchivosCopias());
    }

    [Fact]
    public async Task Restaurar_DosVecesEnElMismoSegundo_NoPisaLaAnterior()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        var caso = entorno.CrearCasoDeUso();

        var primera = await caso.RestaurarAsync(copia);
        var segunda = await caso.RestaurarAsync(copia);

        Assert.Equal(Path.Combine(RestauradorSqlite.CarpetaAnterior, "20261117-101500"), primera.CarpetaAnterior);
        Assert.Equal(Path.Combine(RestauradorSqlite.CarpetaAnterior, "20261117-101500-2"), segunda.CarpetaAnterior);
        Assert.Equal(
            [1L, 2L],
            EntornoRestauracion.Presupuestos(Path.Combine(entorno.Datos, primera.CarpetaAnterior!, FabricaConexion.NombreArchivo)));
    }

    [Fact]
    public async Task Restaurar_VueltaAtrasFalla_InformaDondeQuedoLaBase()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        var restaurador = entorno.CrearRestaurador(antesDeMover: archivo =>
        {
            if (archivo is RestauradorSqlite.NombreTemporal or "volver:" + FabricaConexion.NombreArchivo)
            {
                throw new IOException("/home/usuario-privado/datos");
            }
        });

        var resultado = await entorno.CrearCasoDeUso(restaurador).RestaurarAsync(copia);

        Assert.Equal(FallaRestauracion.VueltaAtras, resultado.Falla);
        Assert.Equal(nameof(IOException), resultado.TipoError);
        Assert.Equal(Path.Combine(RestauradorSqlite.CarpetaAnterior, "20261117-101500"), resultado.CarpetaAnterior);
        // La base actual quedó en anterior/ y la restaurada sigue como temporal: ninguna de las dos se borró.
        Assert.True(File.Exists(Path.Combine(entorno.Datos, resultado.CarpetaAnterior!, FabricaConexion.NombreArchivo)));
        Assert.True(File.Exists(Path.Combine(entorno.Datos, RestauradorSqlite.NombreTemporal)));
    }

    [Fact]
    public async Task Inspeccionar_CopiaReal_MigracionesYUltimaFactura()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        await SiembraFacturas.SembrarAsync(entorno.Base.Servicios, [Factura(10, 1), Factura(11, 2)]);
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        var ultima = entorno.UltimaFacturaId();
        await SiembraFacturas.SembrarAsync(entorno.Base.Servicios, [Factura(12, 3), Factura(13, 4), Factura(14, 5)]);
        entorno.DejarDatosComoApiCortada();
        var restaurador = entorno.CrearRestaurador();

        var inspeccion = await restaurador.InspeccionarCopiaAsync(copia);
        var posteriores = await restaurador.ContarFacturasPosterioresAsync(inspeccion.UltimaFactura);

        Assert.True(inspeccion.Existe);
        Assert.True(inspeccion.Integra);
        Assert.Equal(restaurador.MigracionesConocidas(), inspeccion.Migraciones);
        Assert.True(inspeccion.TriggersCompletos);
        Assert.Equal(entorno.Base.CrearContexto().Database.GetMigrations(), restaurador.MigracionesConocidas());
        Assert.Equal(ultima, inspeccion.UltimaFactura);
        Assert.Equal(3, posteriores);
        Assert.Equal([Path.GetFileName(copia)], entorno.ArchivosCopias());
    }

    [Fact]
    public async Task Verificar_CopiaConMigracionDesconocida_VersionMasNueva()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        EntornoRestauracion.Ejecutar(
            copia,
            "INSERT INTO \"__EFMigrationsHistory\" (MigrationId, ProductVersion) VALUES ('20991231000000_Futura', '99.0.0')");
        var antes = entorno.FotoDatos();

        var resultado = await entorno.CrearCasoDeUso().RestaurarAsync(copia);

        Assert.Equal(FallaRestauracion.CopiaRechazada, resultado.Falla);
        Assert.Equal(RechazoCopia.VersionMasNueva, resultado.Copia.Rechazo);
        Assert.Equal(antes, entorno.FotoDatos());
    }

    [Fact]
    public async Task Verificar_BaseSinHistorialDeMigraciones_Ajena()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = Path.Combine(entorno.Copias, CopiaBackup.Nombre(EntornoRestauracion.FechaCopia));
        EntornoRestauracion.Ejecutar(copia, "CREATE TABLE Otra (Id INTEGER PRIMARY KEY); PRAGMA journal_mode=DELETE;", crear: true);

        var verificacion = await entorno.CrearCasoDeUso().VerificarAsync(copia);

        Assert.Equal(RechazoCopia.Ajena, verificacion.Rechazo);
    }

    [Fact]
    public async Task Contar_BaseActualDanada_SinCantidad()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        foreach (var archivo in Directory.EnumerateFiles(entorno.Datos))
        {
            File.Delete(archivo);
        }

        File.WriteAllBytes(Path.Combine(entorno.Datos, FabricaConexion.NombreArchivo), RandomNumberGenerator.GetBytes(4096));

        Assert.Null(await entorno.CrearRestaurador().ContarFacturasPosterioresAsync(0));
    }

    [Fact]
    public async Task Contar_SinApiNiAuxiliares_NoDejaArchivosNuevos()
    {
        // Una Api detenida bien: la base sin -wal ni -shm. Contar las facturas no deja archivos nuevos en la carpeta de
        // datos (la cancelación del comando dice "no se cambió nada").
        using var entorno = await EntornoRestauracion.CrearAsync();
        await SiembraFacturas.SembrarAsync(entorno.Base.Servicios, [Factura(10, 1)]);
        entorno.DetenerApi();
        var antes = entorno.FotoDatos();

        var posteriores = await entorno.CrearRestaurador().ContarFacturasPosterioresAsync(0);

        Assert.Equal(1, posteriores);
        Assert.Equal(antes, entorno.FotoDatos());
    }

    [Theory]
    [InlineData("TR_Presupuesto_FinalInmutable")]
    [InlineData("TR_Factura_NoSeBorra")]
    [InlineData("TR_DescarteEmision_SinBorrar")]
    [InlineData("TR_BackupRealizado_Inmutable")]
    public async Task Verificar_CopiaSinUnTriggerDeProteccion_AjenaSinTocarLaBase(string trigger)
    {
        // Historial completo, pero sin un trigger de registros cerrados o de solo agregar: restaurarla dejaría que se
        // modifique un presupuesto en Final o una factura con CAE (AGENTS.md).
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        EntornoRestauracion.Ejecutar(copia, $"DROP TRIGGER \"{trigger}\"");
        var antes = entorno.FotoDatos();
        var caso = entorno.CrearCasoDeUso();

        var verificacion = await caso.VerificarAsync(copia);
        var resultado = await caso.RestaurarAsync(copia);

        Assert.Equal(RechazoCopia.Ajena, verificacion.Rechazo);
        Assert.Equal(FallaRestauracion.CopiaRechazada, resultado.Falla);
        Assert.Equal(RechazoCopia.Ajena, resultado.Copia.Rechazo);
        Assert.Equal(antes, entorno.FotoDatos());
        Assert.False(Directory.Exists(Path.Combine(entorno.Datos, RestauradorSqlite.CarpetaAnterior)));
    }

    [Fact]
    public async Task Restaurar_TemporalSinUnTrigger_RechazadoAunqueLaCopiaHayaPasado()
    {
        // La copia cambió después de verificarla (o la verificación de la copia se saltea): el temporal se juzga con la
        // misma regla y se rechaza sin tocar la base actual.
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        EntornoRestauracion.Ejecutar(copia, "DROP TRIGGER \"TR_LineaFactura_Cerrada\"");
        var antes = entorno.FotoDatos();

        var resultado = await entorno.CrearCasoDeUso(new CopiaAceptada(entorno.CrearRestaurador())).RestaurarAsync(copia);

        Assert.Equal(FallaRestauracion.CopiaRechazada, resultado.Falla);
        Assert.Equal(RechazoCopia.Ajena, resultado.Copia.Rechazo);
        Assert.Equal(antes, entorno.FotoDatos());
        Assert.False(Directory.Exists(Path.Combine(entorno.Datos, RestauradorSqlite.CarpetaAnterior)));
    }

    [Fact]
    public async Task Restaurar_TemporalDanado_RechazadoSinTocarLaBase()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        var bytes = File.ReadAllBytes(copia);
        File.WriteAllBytes(copia, bytes[..(bytes.Length / 2)]);
        var antes = entorno.FotoDatos();

        var resultado = await entorno.CrearCasoDeUso(new CopiaAceptada(entorno.CrearRestaurador())).RestaurarAsync(copia);

        // Según dónde lo note SQLite, la copia al temporal falla o el temporal no pasa la integridad: nunca se aparta.
        Assert.False(resultado.Hecha);
        Assert.Contains(resultado.Falla, new FallaRestauracion?[] { FallaRestauracion.CopiaRechazada, FallaRestauracion.Temporal });
        Assert.Equal(antes, entorno.FotoDatos());
        Assert.False(Directory.Exists(Path.Combine(entorno.Datos, RestauradorSqlite.CarpetaAnterior)));
    }

    [Fact]
    public async Task Verificar_CopiaEnModoWal_DanadaSinDejarAuxiliares()
    {
        // Una copia del backup queda siempre en modo de diario de reversión (ADR-029). Una en WAL no salió del backup (por
        // ejemplo, una copia de archivo común de optica.db) y, abierta en solo lectura, dejaría -wal y -shm junto a ella.
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        EntornoRestauracion.Ejecutar(copia, "PRAGMA journal_mode=WAL;");
        Assert.Equal(2, File.ReadAllBytes(copia)[18]);

        var verificacion = await entorno.CrearCasoDeUso().VerificarAsync(copia);

        Assert.Equal(RechazoCopia.Danada, verificacion.Rechazo);
        Assert.Equal([Path.GetFileName(copia)], entorno.ArchivosCopias());
    }

    [Fact]
    public async Task Restaurar_RenuevaLasClavesDeSesion()
    {
        using var entorno = await EntornoRestauracion.CrearAsync();
        var copia = await entorno.HacerCopiaAsync(EntornoRestauracion.FechaCopia);
        var claves = Path.Combine(entorno.Datos, DatosLocales.CarpetaClaves);
        Directory.CreateDirectory(claves);
        File.WriteAllText(Path.Combine(claves, "key-de-antes.xml"), "<key />");

        var resultado = await entorno.CrearCasoDeUso().RestaurarAsync(copia);

        // Las cookies emitidas con esas claves dejan de valer; la Api crea claves nuevas al arrancar.
        Assert.True(resultado.Hecha);
        Assert.Null(resultado.TipoErrorClaves);
        Assert.True(Directory.Exists(claves));
        Assert.Empty(Directory.EnumerateFileSystemEntries(claves));
    }

    [Fact]
    public void TriggersDeProteccion_CadaMigracionCreaExactamenteLosDeLaTabla()
    {
        // La guarda de la tabla: se migra una base nueva de a una migración y, después de cada una, los triggers de la base
        // tienen que ser exactamente los de la tabla para las migraciones aplicadas. Una migración nueva que crea, borra o
        // pierde un trigger (por ejemplo, al reconstruir una tabla) sin actualizar la tabla hace fallar esta prueba.
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using var servicios = BaseTemporal.CrearServicios(directorio);
            using var alcance = servicios.CreateScope();
            var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
            var migrador = contexto.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            var todas = contexto.Database.GetMigrations().ToList();
            Assert.All(TriggersDeProteccion.PorMigracion.Keys, migracion => Assert.Contains(migracion, todas));

            var aplicadas = new List<string>();
            foreach (var migracion in todas)
            {
                migrador.Migrate(migracion);
                aplicadas.Add(migracion);
                var esperados = aplicadas
                    .SelectMany(m => TriggersDeProteccion.PorMigracion.TryGetValue(m, out var t) ? t : [])
                    .Order(StringComparer.Ordinal);
                var enLaBase = contexto.Database
                    .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'trigger'")
                    .AsEnumerable()
                    .Order(StringComparer.Ordinal);
                Assert.True(esperados.SequenceEqual(enLaBase), $"Triggers después de {migracion}.");
            }

            // CierrePresupuestos 6, Facturas 6, RecuperacionEmisiones 3 y Backup 2.
            Assert.Equal(17, TriggersDeProteccion.PorMigracion.Values.Sum(t => t.Length));
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    private static FacturaSembrada Factura(long presupuesto, long numero) =>
        new(presupuesto, new DateOnly(2026, 10, 20), "Ficticio", "Restaurada", "00222333", 3, numero);
}

/// <summary>
/// Una base de la óptica con dos presupuestos (el 2, grabado después de la copia), una carpeta de copias y una carpeta de
/// datos con la base como la deja una Api cortada: <c>optica.db</c>, <c>-wal</c> con cambios sin pasar al archivo de la
/// base, y <c>-shm</c>.
/// </summary>
internal sealed class EntornoRestauracion : IDisposable
{
    public static readonly DateOnly FechaCopia = new(2026, 10, 30);

    /// <summary>El 17/11/2026 a las 10:15 en la óptica (UTC-3).</summary>
    public static readonly DateTimeOffset Ahora = new(2026, 11, 17, 13, 15, 0, TimeSpan.Zero);

    private bool _copiaHecha;

    private EntornoRestauracion()
    {
        Base = new BaseTemporal();
        Copias = BaseTemporal.CrearDirectorioTemporal();
        Datos = BaseTemporal.CrearDirectorioTemporal();
        Reloj = new FakeTimeProvider(Ahora);
        Reloj.SetLocalTimeZone(EntornoBackup.ZonaLocal);
    }

    /// <summary>La base de origen: la Api "en marcha", con su pool de conexiones.</summary>
    public BaseTemporal Base { get; }

    /// <summary>La carpeta donde están las copias de backup.</summary>
    public string Copias { get; }

    /// <summary>La carpeta de datos de la restauración.</summary>
    public string Datos { get; }

    public FakeTimeProvider Reloj { get; }

    public static async Task<EntornoRestauracion> CrearAsync()
    {
        var entorno = new EntornoRestauracion();
        await FacturasPrueba.GrabarPresupuestoFinal(entorno.Base, 1);
        return entorno;
    }

    /// <summary>
    /// Hace la copia de <paramref name="fecha"/> con el copiador del backup (Block 4) y la renombra como el backup. Después
    /// graba el presupuesto 2, que la copia no tiene, y deja la carpeta de datos como una Api cortada.
    /// </summary>
    public async Task<string> HacerCopiaAsync(DateOnly fecha)
    {
        var copia = Path.Combine(Copias, CopiaBackup.Nombre(fecha));
        var copiador = new CopiadorBackupSqlite(Base.Servicios.GetRequiredService<FabricaConexion>());
        await copiador.CopiarAsync(copia + CopiaBackup.SufijoParcial);
        Assert.True(await copiador.VerificarAsync(copia + CopiaBackup.SufijoParcial));
        File.Move(copia + CopiaBackup.SufijoParcial, copia);
        if (!_copiaHecha)
        {
            _copiaHecha = true;
            await FacturasPrueba.GrabarPresupuestoFinal(Base, 2);
            DejarDatosComoApiCortada();
        }

        return copia;
    }

    /// <summary>
    /// Copia <c>optica.db</c>, <c>-wal</c> y <c>-shm</c> de la base de origen, con su pool abierto (la Api en marcha), a la
    /// carpeta de datos: lo que queda en el disco si la Api se corta. Solo en una prueba: es la copia de archivo común que
    /// AGENTS.md prohíbe para un backup.
    /// </summary>
    public void DejarDatosComoApiCortada()
    {
        foreach (var archivo in Directory.EnumerateFiles(Datos))
        {
            File.Delete(archivo);
        }

        foreach (var sufijo in new[] { string.Empty, "-wal", "-shm" })
        {
            var origen = Path.Combine(Base.Directorio, FabricaConexion.NombreArchivo + sufijo);
            Assert.True(File.Exists(origen), "La base de origen tiene que estar en WAL con su pool abierto.");
            File.Copy(origen, Path.Combine(Datos, FabricaConexion.NombreArchivo + sufijo));
        }
    }

    /// <summary>Copia la base de origen a la carpeta de datos con la Api detenida bien: sin <c>-wal</c> ni <c>-shm</c>.</summary>
    public void DetenerApi()
    {
        foreach (var archivo in Directory.EnumerateFiles(Datos))
        {
            File.Delete(archivo);
        }

        using var origen = new SqliteConnection(Base.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion);
        origen.Open();
        using var destino = new SqliteConnection(Cadena(Path.Combine(Datos, FabricaConexion.NombreArchivo), SqliteOpenMode.ReadWriteCreate));
        destino.Open();
        origen.BackupDatabase(destino);
        destino.Close();
        Assert.Equal([FabricaConexion.NombreArchivo], ArchivosDatos());
    }

    /// <summary>El caso de uso con el restaurador real (o el que reciba) y los datos locales reales de la carpeta de datos.</summary>
    public RestaurarBackup CrearCasoDeUso(IRestauradorBase? restaurador = null) =>
        new(
            restaurador ?? CrearRestaurador(),
            new DatosLocales(new FabricaConexion(Datos), Base.Servicios.GetRequiredService<IServiceScopeFactory>()));

    public RestauradorSqlite CrearRestaurador(Action<string>? antesDeMover = null) =>
        new(
            new FabricaConexion(Datos),
            Base.Servicios.GetRequiredService<IServiceScopeFactory>(),
            Reloj,
            antesDeMover);

    public long UltimaFacturaId()
    {
        using var conexion = new SqliteConnection(Base.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT MAX(Id) FROM Factura";
        return (long)comando.ExecuteScalar()!;
    }

    /// <summary>
    /// Los archivos (no carpetas) de la carpeta de datos con su hash; el <c>-shm</c>, solo con su existencia: es el índice
    /// en memoria compartida del WAL, que SQLite reescribe cada vez que alguien lee la base (también al contar las
    /// facturas), y no guarda datos.
    /// </summary>
    public Dictionary<string, string> FotoDatos()
    {
        Assert.False(File.Exists(Path.Combine(Datos, RestauradorSqlite.NombreTemporal)));
        return Directory.EnumerateFiles(Datos).ToDictionary(archivo => Path.GetFileName(archivo), HashSalvoShm);
    }

    public static string HashSalvoShm(string archivo) => archivo.EndsWith("-shm", StringComparison.Ordinal) ? "existe" : Hash(archivo);

    public List<string> ArchivosDatos() =>
        [.. Directory.EnumerateFiles(Datos).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    public List<string> ArchivosCopias() =>
        [.. Directory.EnumerateFiles(Copias).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    public static string Hash(string archivo) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivo)));

    /// <summary>Los números de presupuesto de una base, abierta sin pool.</summary>
    public static List<long> Presupuestos(string baseDeDatos)
    {
        using var conexion = new SqliteConnection(Cadena(baseDeDatos, SqliteOpenMode.ReadWrite));
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT Numero FROM Presupuesto ORDER BY Numero";
        using var lector = comando.ExecuteReader();
        var numeros = new List<long>();
        while (lector.Read())
        {
            numeros.Add(lector.GetInt64(0));
        }

        return numeros;
    }

    public static void Ejecutar(string baseDeDatos, string sql, bool crear = false)
    {
        using var conexion = new SqliteConnection(
            Cadena(baseDeDatos, crear ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite));
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    private static string Cadena(string ruta, SqliteOpenMode modo) =>
        new SqliteConnectionStringBuilder { DataSource = ruta, Mode = modo, Pooling = false }.ToString();

    public void Dispose()
    {
        Base.Dispose();
        BaseTemporal.BorrarDirectorio(Copias);
        BaseTemporal.BorrarDirectorio(Datos);
    }
}

/// <summary>
/// El restaurador real, salvo que da por buena cualquier copia: simula una copia que cambió entre la verificación y la
/// restauración, para probar que el temporal se juzga por sí mismo.
/// </summary>
internal sealed class CopiaAceptada(RestauradorSqlite real) : IRestauradorBase
{
    public IReadOnlyList<string> MigracionesConocidas() => real.MigracionesConocidas();

    public Task<InspeccionCopia> InspeccionarCopiaAsync(string rutaCopia, CancellationToken cancelacion = default) =>
        Task.FromResult(new InspeccionCopia(true, true, real.MigracionesConocidas(), 0));

    public Task<int?> ContarFacturasPosterioresAsync(long ultimaFacturaDeLaCopia, CancellationToken cancelacion = default) =>
        real.ContarFacturasPosterioresAsync(ultimaFacturaDeLaCopia, cancelacion);

    public Task<InspeccionCopia> RestaurarATemporalAsync(string rutaCopia, CancellationToken cancelacion = default) =>
        real.RestaurarATemporalAsync(rutaCopia, cancelacion);

    public string ApartarYPonerRestaurada() => real.ApartarYPonerRestaurada();

    public void DescartarTemporal() => real.DescartarTemporal();
}
