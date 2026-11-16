using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Optica.Application.Backup;
using Optica.Application.Comun;
using Optica.Domain.Backup;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Backup;
using Optica.Infrastructure.Persistencia;
using Optica.Infrastructure.Tests.Persistencia;
using Optica.Pruebas.Compartido;

namespace Optica.Infrastructure.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 4 (FR-05 a FR-07, FR-12; AC-05, AC-06, AC-07, AC-13; D-09, D-20, D-22, D-23; ADR-029): la copia
/// con la API de backup de SQLite sobre la base real de cada prueba, hacia una carpeta temporal propia que se borra, y
/// el caso de uso completo con el almacén de archivos y el candado singleton.
/// </summary>
public class CopiadorBackupSqliteTests
{
    [Fact]
    public async Task Backup_IncluyePresupuestoGrabadoAntes()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();

        // 19:59: la operadora graba un presupuesto; queda en el WAL, todavía sin pasar al archivo de la base.
        entorno.Reloj.SetUtcNow(EntornoBackup.VeinteHoras.AddMinutes(-1));
        await FacturasPrueba.GrabarPresupuestoFinal(entorno.Base, 1);
        var wal = Path.Combine(entorno.Base.Directorio, FabricaConexion.NombreArchivo + "-wal");
        Assert.True(new FileInfo(wal).Length > 0);

        // Prueba de que el presupuesto está solo en el WAL: una copia de archivo común de optica.db (lo que AGENTS.md
        // prohíbe) no lo tiene.
        var comun = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var copiaComun = Path.Combine(comun, "copia-comun.db");
            File.Copy(Path.Combine(entorno.Base.Directorio, FabricaConexion.NombreArchivo), copiaComun);
            Assert.Empty(PresupuestosSinWal(copiaComun));
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(comun);
        }

        // 20:00: el backup.
        entorno.Reloj.SetUtcNow(EntornoBackup.VeinteHoras);
        var resultado = await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado);

        // AC-05: la copia del día incluye el presupuesto, de modo que se vería al restaurarla.
        Assert.Equal(EstadoBackup.Hecho, resultado.Estado);
        Assert.Equal([1L], EntornoBackup.PresupuestosDe(entorno.RutaCopia(EntornoBackup.Hoy)));
        Assert.Equal(["2026-10-30"], entorno.FechasHistorial());
    }

    [Fact]
    public async Task Backup_SinWalNiShmEnElDestino()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();
        await FacturasPrueba.GrabarPresupuestoFinal(entorno.Base, 1);
        var temporal = Path.Combine(entorno.Carpeta, CopiaBackup.NombreParcial(EntornoBackup.Hoy));

        // El copiador solo: la copia de una base en WAL pasa a journal_mode=DELETE y no deja auxiliares.
        await entorno.Copiador.CopiarAsync(temporal);
        Assert.Equal([CopiaBackup.NombreParcial(EntornoBackup.Hoy)], entorno.Archivos());
        Assert.Equal((1, 1), VersionesDeEscrituraYLectura(temporal)); // 1 = diario de reversión; 2 = WAL
        Assert.True(await entorno.Copiador.VerificarAsync(temporal));
        Assert.Equal([CopiaBackup.NombreParcial(EntornoBackup.Hoy)], entorno.Archivos());

        // El archivo queda libre: se puede renombrar y borrar enseguida (Windows no lo retiene).
        File.Move(temporal, temporal + ".libre");
        File.Delete(temporal + ".libre");

        // El caso de uso completo: en la carpeta queda solo la copia, sin -wal, -shm ni -journal.
        var resultado = await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado);
        Assert.Equal(EstadoBackup.Hecho, resultado.Estado);
        Assert.Equal([CopiaBackup.Nombre(EntornoBackup.Hoy)], entorno.Archivos());
        Assert.Equal((1, 1), VersionesDeEscrituraYLectura(entorno.RutaCopia(EntornoBackup.Hoy)));
        EntornoBackup.PresupuestosDe(entorno.RutaCopia(EntornoBackup.Hoy));
        Assert.Equal([CopiaBackup.Nombre(EntornoBackup.Hoy)], entorno.Archivos());
    }

    [Fact]
    public async Task Backup_IntegridadFallida_NoCuenta()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();
        await FacturasPrueba.GrabarPresupuestoFinal(entorno.Base, 1);
        var anterior = EntornoBackup.Hoy.AddDays(-1);
        File.WriteAllText(entorno.RutaCopia(anterior), "copia de ayer");

        // La verificación real sobre una copia dañada después de escribirla.
        var temporal = Path.Combine(entorno.Carpeta, CopiaBackup.NombreParcial(EntornoBackup.Hoy));
        await entorno.Copiador.CopiarAsync(temporal);
        CopiadorIntervenido.Corromper(temporal);
        Assert.False(await entorno.Copiador.VerificarAsync(temporal));
        Assert.False(await entorno.Copiador.VerificarAsync(Path.Combine(entorno.Carpeta, "no-existe.db")));
        File.Delete(temporal);

        var copiador = new CopiadorIntervenido(entorno.Copiador) { DespuesDeCopiar = CopiadorIntervenido.Corromper };
        var resultado = await entorno.CrearCasoDeUso(copiador).EjecutarAsync(MotivoBackup.Programado);

        // FR-07: falla del día; no se renombra, no se anota en el historial, se borra el temporal y la copia de ayer queda.
        Assert.Equal(EstadoBackup.Fallido, resultado.Estado);
        Assert.Equal(FallaBackup.Integridad, resultado.Falla);
        Assert.Equal([CopiaBackup.Nombre(anterior)], entorno.Archivos());
        Assert.Empty(entorno.FechasHistorial());
    }

    [Fact]
    public async Task Backup_CarpetaDesconectada_SinCopiaDelDia()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();

        // El pendrive se desconecta durante el backup, después de escribir el temporal y antes de verificarlo.
        var copiador = new CopiadorIntervenido(entorno.Copiador)
        {
            DespuesDeCopiar = _ => Directory.Delete(entorno.Carpeta, recursive: true),
        };
        var durante = await entorno.CrearCasoDeUso(copiador).EjecutarAsync(MotivoBackup.Programado);

        // AC-07: la copia incompleta no cuenta como backup del día.
        Assert.Equal(EstadoBackup.Fallido, durante.Estado);
        Assert.Contains(durante.Falla, new FallaBackup?[] { FallaBackup.Integridad, FallaBackup.Renombre });
        Assert.Empty(entorno.FechasHistorial());
        Assert.False(Directory.Exists(entorno.Carpeta));

        // Con la carpeta ya desconectada al empezar: carpeta inaccesible, sin copia ni historial, y no se crea la carpeta.
        var antes = await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado);
        Assert.Equal(EstadoBackup.Fallido, antes.Estado);
        Assert.Equal(FallaBackup.CarpetaInaccesible, antes.Falla);
        Assert.Empty(entorno.FechasHistorial());
        Assert.False(Directory.Exists(entorno.Carpeta));

        // Al volver a conectarla, el backup siguiente anda: el candado quedó libre.
        Directory.CreateDirectory(entorno.Carpeta);
        Assert.Equal(EstadoBackup.Hecho, (await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado)).Estado);
        Assert.Equal(["2026-10-30"], entorno.FechasHistorial());
    }

    [Fact]
    public async Task Backup_Programado_ReemplazaLaCopiaDeHoyDespuesDeVerificar()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();
        var primero = await FacturasPrueba.GrabarPresupuestoFinal(entorno.Base, 1);
        var copiaDeHoy = entorno.RutaCopia(EntornoBackup.Hoy);

        // 09:00: la copia al arrancar, con la fecha de hoy (D-11).
        entorno.Reloj.SetUtcNow(EntornoBackup.VeinteHoras.AddHours(-11));
        Assert.Equal(EstadoBackup.Hecho, (await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.AlIniciar)).Estado);
        Assert.Equal([1L], EntornoBackup.PresupuestosDe(copiaDeHoy));
        var huellaManana = Huella(copiaDeHoy);

        // Durante el día se graba otro presupuesto.
        var contexto = entorno.Base.CrearContexto();
        new PresupuestoRepository(contexto).Agregar(NuevoPresupuesto(2, primero.Lineas[0].CodigoArticulo));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        // 20:00, con una copia nueva que no pasa la verificación: la de la mañana queda intacta (D-20).
        entorno.Reloj.SetUtcNow(EntornoBackup.VeinteHoras);
        var danado = new CopiadorIntervenido(entorno.Copiador) { DespuesDeCopiar = CopiadorIntervenido.Corromper };
        var fallido = await entorno.CrearCasoDeUso(danado).EjecutarAsync(MotivoBackup.Programado);
        Assert.Equal(EstadoBackup.Fallido, fallido.Estado);
        Assert.Equal(huellaManana, Huella(copiaDeHoy));
        Assert.Equal([CopiaBackup.Nombre(EntornoBackup.Hoy)], entorno.Archivos());

        // 20:05, el reintento verificado reemplaza la copia de hoy con lo grabado durante el día.
        entorno.Reloj.Advance(TimeSpan.FromMinutes(5));
        var programado = await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado);
        Assert.Equal(EstadoBackup.Hecho, programado.Estado);
        Assert.Equal(EntornoBackup.Hoy, programado.Fecha);
        Assert.Equal([1L, 2L], EntornoBackup.PresupuestosDe(copiaDeHoy));
        Assert.Equal([CopiaBackup.Nombre(EntornoBackup.Hoy)], entorno.Archivos());
        Assert.Equal(["2026-10-30", "2026-10-30"], entorno.FechasHistorial());

        // El historial ya dice que el programado de hoy se hizo (lo que mira el Block 5).
        var historial = entorno.Base.Servicios.CreateScope().ServiceProvider.GetRequiredService<IHistorialBackupRepository>();
        Assert.True(await historial.HuboCopiaProgramadaAsync(EntornoBackup.Hoy));
    }

    [Fact]
    public async Task Backup_EnCurso_SegundoPedidoNoHaceNada()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();
        var empezo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seguir = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lento = new CopiadorIntervenido(entorno.Copiador)
        {
            AntesDeCopiar = async () =>
            {
                empezo.SetResult();
                await seguir.Task;
            },
        };
        var otro = new CopiadorIntervenido(entorno.Copiador);

        // Dos ejecuciones en alcances distintos (como el programador) comparten el candado singleton (D-22).
        var primera = Task.Run(() => entorno.CrearCasoDeUso(lento).EjecutarAsync(MotivoBackup.Programado));
        await empezo.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var segunda = await entorno.CrearCasoDeUso(otro).EjecutarAsync(MotivoBackup.Programado);

        // El segundo pedido no hace nada: ni copia ni toca la carpeta.
        Assert.Equal(EstadoBackup.EnCurso, segunda.Estado);
        Assert.Equal(0, otro.Copias);
        Assert.Empty(entorno.Archivos());

        seguir.SetResult();
        Assert.Equal(EstadoBackup.Hecho, (await primera.WaitAsync(TimeSpan.FromSeconds(30))).Estado);
        Assert.Equal(["2026-10-30"], entorno.FechasHistorial());

        // Terminado el primero, el candado queda libre; liberar un turno dos veces no libera el de otro.
        Assert.Equal(EstadoBackup.Hecho, (await entorno.CrearCasoDeUso(otro).EjecutarAsync(MotivoBackup.Programado)).Estado);
        var turno = entorno.Candado.IntentarTomar();
        Assert.NotNull(turno);
        turno.Dispose();
        var siguiente = entorno.Candado.IntentarTomar();
        Assert.NotNull(siguiente);
        turno.Dispose();
        Assert.Null(entorno.Candado.IntentarTomar());
        siguiente.Dispose();
    }

    /// <summary>
    /// Los presupuestos de una copia de archivo común, sin su WAL: abierta en lectura y escritura sin pool (es un archivo
    /// descartable), con la tabla ausente contada como ninguno.
    /// </summary>
    private static List<long> PresupuestosSinWal(string ruta)
    {
        var cadena = new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Presupuesto'";
        if ((long)comando.ExecuteScalar()! == 0)
        {
            return [];
        }

        comando.CommandText = "SELECT Numero FROM Presupuesto ORDER BY Numero";
        using var lector = comando.ExecuteReader();
        var numeros = new List<long>();
        while (lector.Read())
        {
            numeros.Add(lector.GetInt64(0));
        }

        return numeros;
    }

    internal static Presupuesto NuevoPresupuesto(long numero, long codigo) =>
        Presupuesto.Nuevo(
            numero,
            new DateOnly(2026, 10, 30),
            new ClientePresupuesto("Ficticiez Quimera", "Prueboncia", FacturasPrueba.Dni, null, null, null),
            [new LineaPresupuesto(codigo, "Armazón metálico", 1000m, 1, 0m, 1000m, 1000m)]);

    /// <summary>Los bytes 18 y 19 del encabezado: 1 y 1 en modo de diario de reversión, 2 y 2 en WAL.</summary>
    private static (int Escritura, int Lectura) VersionesDeEscrituraYLectura(string ruta)
    {
        var encabezado = new byte[20];
        using (var archivo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            archivo.ReadExactly(encabezado);
        }

        return (encabezado[18], encabezado[19]);
    }

    private static string Huella(string ruta) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ruta)));
}

/// <summary>
/// Las pruebas cuyo resultado depende de tiempos entre hilos (escrituras concurrentes con una copia o con otra escritura):
/// corren solas, después de las colecciones en paralelo, para que la carga de otras pruebas no cambie el encuentro.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionTiemposSensibles
{
    public const string Nombre = "TiemposSensibles";
}

/// <summary>
/// Spec FEAT-010, Block 4 (FR-05, AC-06): grabar mientras corre el backup no da error. En la colección sin paralelismo
/// (<see cref="ColeccionTiemposSensibles"/>).
/// </summary>
[Collection(ColeccionTiemposSensibles.Nombre)]
public class CopiaConEscriturasConcurrentesTests
{
    [Fact]
    public async Task Backup_GrabarDuranteElBackup_SinError()
    {
        using var entorno = new EntornoBackup();
        await entorno.GrabarUbicacionAsync();
        const int Sembrados = 3_000;
        SiembraPresupuestos.Sembrar(FacturasPrueba.CadenaDe(entorno.Base), cantidad: Sembrados, finales: 300);
        var primero = await new PresupuestoRepository(entorno.Base.CrearContexto()).ObtenerAsync(1);
        var codigo = primero!.Lineas[0].CodigoArticulo;

        // 1) Una grabación de la operadora con su transacción de escritura abierta durante TODA la copia: la copia no
        // espera la escritura ni la escritura falla (FR-05, AC-06). La copia es la foto de lo confirmado al empezar. (El
        // caso de uso completo no va acá adentro: su alta en el historial esperaría, como corresponde, a esta escritura.)
        var proveedor = entorno.Base.Servicios.CreateScope().ServiceProvider;
        var unidad = proveedor.GetRequiredService<IUnidadDeTrabajo>();
        var presupuestos = proveedor.GetRequiredService<IPresupuestoRepository>();
        var temporal = Path.Combine(entorno.Carpeta, CopiaBackup.NombreParcial(EntornoBackup.Hoy));
        var integra = false;
        await unidad.EjecutarEnTransaccionAsync(
            async token =>
            {
                presupuestos.Agregar(CopiadorBackupSqliteTests.NuevoPresupuesto(Sembrados + 1, codigo));
                await unidad.ConfirmarAsync(token);
                await entorno.Copiador.CopiarAsync(temporal, token);
                integra = await entorno.Copiador.VerificarAsync(temporal, token);
                return true;
            });

        Assert.True(integra);
        Assert.Equal(Sembrados, EntornoBackup.PresupuestosDe(temporal).Count);
        Assert.Equal(
            Sembrados + 1,
            EntornoBackup.PresupuestosDe(Path.Combine(entorno.Base.Directorio, FabricaConexion.NombreArchivo)).Count);

        // 2) Grabaciones sueltas mientras corre el backup completo de la base con volumen: ninguna recibe error.
        using var fin = new CancellationTokenSource();
        var errores = 0;
        Exception? primerError = null;
        var grabadas = 0;
        var grabacion = Task.Run(async () =>
        {
            var numero = 5_000L;
            while (!fin.IsCancellationRequested)
            {
                try
                {
                    using var alcance = entorno.Base.Servicios.CreateScope();
                    alcance.ServiceProvider.GetRequiredService<IPresupuestoRepository>()
                        .Agregar(CopiadorBackupSqliteTests.NuevoPresupuesto(numero++, codigo));
                    await alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
                    Interlocked.Increment(ref grabadas);
                }
                catch (Exception error)
                {
                    Interlocked.CompareExchange(ref primerError, error, null);
                    Interlocked.Increment(ref errores);
                }

                // Cede el hilo en cada vuelta: el backup y su alta en el historial también tienen turno.
                await Task.Delay(1);
            }
        });

        // El backup empieza cuando ya hubo grabaciones, y la grabación sigue hasta que termina.
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref grabadas) < 3 && Volatile.Read(ref errores) == 0 && DateTime.UtcNow < limite)
        {
            await Task.Delay(5);
        }

        var conVolumen = await entorno.CrearCasoDeUso().EjecutarAsync(MotivoBackup.Programado);
        var alTerminar = Volatile.Read(ref grabadas);
        await fin.CancelAsync();
        await grabacion;

        Assert.Equal(EstadoBackup.Hecho, conVolumen.Estado);
        Assert.Null(primerError);
        Assert.Equal(0, errores);
        Assert.True(alTerminar >= 3);
        Assert.True(EntornoBackup.PresupuestosDe(entorno.RutaCopia(EntornoBackup.Hoy)).Count >= Sembrados + 4);
        Assert.Equal(["2026-10-30"], entorno.FechasHistorial());
    }
}

/// <summary>
/// Una instalación de prueba para el backup (spec FEAT-010, Block 4), compartida con <see cref="AlmacenCopiasArchivoTests"/>:
/// la base SQLite real con las migraciones, una carpeta de backup propia bajo la carpeta temporal (otra que la de los
/// datos) ya grabada como ubicación, el reloj falso en la zona de la óptica (UTC-3) y el candado singleton. Al
/// descartarla se borran las dos carpetas.
/// </summary>
internal sealed class EntornoBackup : IDisposable
{
    /// <summary>Una zona fija UTC-3, sin horario de verano, como la de la óptica.</summary>
    public static readonly TimeZoneInfo ZonaLocal = TimeZoneInfo.CreateCustomTimeZone(
        "Prueba-03", TimeSpan.FromHours(-3), "Prueba UTC-3", "Prueba UTC-3");

    /// <summary>El 30/10/2026 a las 20:00 en la óptica.</summary>
    public static readonly DateTimeOffset VeinteHoras = new(2026, 10, 30, 23, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Hoy = new(2026, 10, 30);

    private readonly List<IServiceScope> _alcances = [];

    public EntornoBackup()
    {
        Base = new BaseTemporal();
        Carpeta = BaseTemporal.CrearDirectorioTemporal();
        // Empieza a las 08:00 del 30/10 en la óptica: el reloj falso solo avanza, y cada prueba lo lleva a la hora que mira.
        Reloj = new FakeTimeProvider(VeinteHoras.AddHours(-12));
        Reloj.SetLocalTimeZone(ZonaLocal);
        Fabrica = Base.Servicios.GetRequiredService<FabricaConexion>();
        Copiador = new CopiadorBackupSqlite(Fabrica);
        Almacen = new AlmacenCopiasArchivo();
    }

    public BaseTemporal Base { get; }

    /// <summary>La carpeta del backup, vacía al empezar.</summary>
    public string Carpeta { get; }

    public FakeTimeProvider Reloj { get; }

    public FabricaConexion Fabrica { get; }

    public CopiadorBackupSqlite Copiador { get; }

    public AlmacenCopiasArchivo Almacen { get; }

    public CandadoBackup Candado { get; } = new();

    /// <summary>La ruta de la copia de <paramref name="fecha"/> en la carpeta.</summary>
    public string RutaCopia(DateOnly fecha) => Path.Combine(Carpeta, CopiaBackup.Nombre(fecha));

    /// <summary>Graba la carpeta como ubicación del backup, como lo haría la configuración.</summary>
    public async Task GrabarUbicacionAsync()
    {
        var alcance = NuevoAlcance();
        var ubicacion = UbicacionBackup.ParaGrabar(Carpeta).Ubicacion
            ?? throw new InvalidOperationException("La carpeta de prueba no es una ubicación válida.");
        alcance.ServiceProvider.GetRequiredService<IUbicacionBackupRepository>().Reemplazar(ubicacion);
        await alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
    }

    /// <summary>
    /// El caso de uso en un alcance propio, como cada ejecución del programador, con el copiador real o el que reciba y
    /// el candado compartido.
    /// </summary>
    public HacerBackup CrearCasoDeUso(ICopiadorBackup? copiador = null)
    {
        var proveedor = NuevoAlcance().ServiceProvider;
        return new HacerBackup(
            proveedor.GetRequiredService<IUbicacionBackupRepository>(),
            proveedor.GetRequiredService<IHistorialBackupRepository>(),
            proveedor.GetRequiredService<IUnidadDeTrabajo>(),
            copiador ?? Copiador,
            Almacen,
            Candado,
            Reloj,
            NullLoggerFactory.Instance);
    }

    /// <summary>Las fechas del historial de backups, en orden de alta.</summary>
    public List<string> FechasHistorial()
    {
        using var conexion = new SqliteConnection(Fabrica.CadenaConexion);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT Fecha FROM BackupRealizado ORDER BY Id";
        using var lector = comando.ExecuteReader();
        var fechas = new List<string>();
        while (lector.Read())
        {
            fechas.Add(lector.GetString(0));
        }

        return fechas;
    }

    /// <summary>
    /// Los números de presupuesto de una copia, leída en solo lectura y sin pool (no deja auxiliares ni retiene el
    /// archivo).
    /// </summary>
    public static List<long> PresupuestosDe(string rutaCopia)
    {
        var cadena = new SqliteConnectionStringBuilder
        {
            DataSource = rutaCopia,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using var conexion = new SqliteConnection(cadena);
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

    /// <summary>Los nombres de los archivos de la carpeta del backup (sin subcarpetas), ordenados.</summary>
    public List<string> Archivos() =>
        [.. Directory.EnumerateFiles(Carpeta).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    private IServiceScope NuevoAlcance()
    {
        var alcance = Base.Servicios.CreateScope();
        _alcances.Add(alcance);
        return alcance;
    }

    public void Dispose()
    {
        foreach (var alcance in _alcances)
        {
            alcance.Dispose();
        }

        Base.Dispose();
        BaseTemporal.BorrarDirectorio(Carpeta);
    }
}

/// <summary>
/// Un copiador que envuelve al real y deja actuar a la prueba entre la copia y la verificación (corromper el temporal,
/// desconectar la carpeta) o antes de copiar (esperar una señal).
/// </summary>
internal sealed class CopiadorIntervenido(ICopiadorBackup real) : ICopiadorBackup
{
    public Func<Task>? AntesDeCopiar { get; init; }

    public Action<string>? DespuesDeCopiar { get; init; }

    public int Copias { get; private set; }

    public async Task CopiarAsync(string rutaTemporal, CancellationToken cancelacion = default)
    {
        Copias++;
        if (AntesDeCopiar is not null)
        {
            await AntesDeCopiar();
        }

        await real.CopiarAsync(rutaTemporal, cancelacion);
        DespuesDeCopiar?.Invoke(rutaTemporal);
    }

    public Task<bool> VerificarAsync(string rutaTemporal, CancellationToken cancelacion = default) =>
        real.VerificarAsync(rutaTemporal, cancelacion);

    /// <summary>
    /// Pisa con bytes 0xFF todas las páginas después de la primera: la copia queda dañada como una escritura a medias.
    /// </summary>
    public static void Corromper(string ruta)
    {
        using var archivo = new FileStream(ruta, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        const int Pagina = 4096;
        var basura = Enumerable.Repeat((byte)0xFF, Pagina).ToArray();
        for (long posicion = Pagina; posicion < archivo.Length; posicion += Pagina)
        {
            archivo.Position = posicion;
            archivo.Write(basura, 0, (int)Math.Min(Pagina, archivo.Length - posicion));
        }
    }
}
