using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Optica.Api.Backup;
using Optica.Application.Backup;
using Optica.Application.Comun;
using Optica.Domain.Backup;
using Optica.Infrastructure.Backup;

namespace Optica.Api.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 5 (FR-04, FR-08; AC-04, AC-08; D-11, D-12, D-20, D-24, D-25; ADR-030): el programador del backup
/// con el reloj falso y el caso de uso real (<see cref="HacerBackup"/>) sobre dobles de sus puertos. Los archivos son
/// reales (el almacén de Infrastructure en una carpeta temporal con un valor testigo en la ruta) y el copiador escribe un
/// temporal de texto en lugar de una base: la copia con SQLite real ya la prueban las pruebas de Infrastructure.
/// </summary>
/// <remarks>
/// Sin esperas reales: cada despertar del programador crea un temporizador nuevo en el reloj falso, y la prueba espera a
/// que se cree el siguiente (el programador terminó la vuelta y volvió a dormir) antes de afirmar. El plazo real de
/// <see cref="Banco.PlazoDeSeguridad"/> solo corta una prueba colgada; no regula ninguna afirmación.
/// </remarks>
public sealed class ProgramadorBackupTests
{
    /// <summary>Un valor testigo dentro de la ruta: si aparece en el log, la ruta se filtró (D-12).</summary>
    private const string Testigo = "Juana Testigo Quimera";

    /// <summary>Una zona fija UTC-3, sin horario de verano, como la de la óptica.</summary>
    private static readonly TimeZoneInfo ZonaLocal = TimeZoneInfo.CreateCustomTimeZone(
        "Prueba-03", TimeSpan.FromHours(-3), "Prueba UTC-3", "Prueba UTC-3");

    private static readonly DateOnly Hoy = new(2026, 11, 16);

    private static readonly TimeSpan CincoMinutos = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Programador_A_las_20_HaceElBackup()
    {
        await using var banco = new Banco(Local(Hoy, 19, 50));
        banco.Historial.Agregadas.Add((Hoy.AddDays(-1), Local(Hoy.AddDays(-1), 20, 5)));

        await banco.IniciarAsync();
        // Al arrancar, con la de ayer, no copia (FR-08); a las 19:55 tampoco.
        Assert.Empty(banco.Copiador.Copias);
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Empty(banco.Copiador.Copias);

        // A las 20:00 hace la copia del día (AC-04), con la fecha local, en la carpeta configurada.
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Single(banco.Copiador.Copias);
        Assert.Equal([Hoy], banco.CopiasEnLaCarpeta());
        Assert.Equal((Hoy, Local(Hoy, 20, 0)), banco.Historial.Agregadas[^1]);

        // Los despertares siguientes ven la copia programada en el historial y no repiten.
        await banco.AvanzarAsync(CincoMinutos);
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Single(banco.Copiador.Copias);

        banco.AfirmarSinRutasEnElRegistro();
    }

    [Fact]
    public async Task Programador_AlIniciarSinElDeAyer_HaceElBackup()
    {
        await using var banco = new Banco(Local(Hoy, 9, 0));
        banco.Historial.Agregadas.Add((Hoy.AddDays(-2), Local(Hoy.AddDays(-2), 20, 0)));

        await banco.IniciarAsync();

        // AC-08: falta la de ayer, así que al arrancar copia, con la fecha de hoy (D-11).
        Assert.Single(banco.Copiador.Copias);
        Assert.Equal([Hoy], banco.CopiasEnLaCarpeta());
        Assert.Equal((Hoy, Local(Hoy, 9, 0)), banco.Historial.Agregadas[^1]);
        banco.AfirmarSinRutasEnElRegistro();
    }

    [Fact]
    public async Task Programador_AlIniciarConElDeAyer_NoRepite()
    {
        await using var banco = new Banco(Local(Hoy, 9, 0));
        banco.Historial.Agregadas.Add((Hoy.AddDays(-1), Local(Hoy.AddDays(-1), 20, 0)));

        await banco.IniciarAsync();
        for (var vuelta = 0; vuelta < 6; vuelta++)
        {
            await banco.AvanzarAsync(CincoMinutos);
        }

        // Al arrancar le pidió el backup al caso de uso una sola vez, y el caso de uso respondió que no hacía falta: los
        // despertares antes de las 20:00 no lo vuelven a pedir.
        var noHaciaFalta = Assert.Single(
            banco.Registro.Lineas,
            l => l.Categoria == HacerBackup.CategoriaRegistro
                && l.Mensaje.Contains("ya está el de ayer o el de hoy", StringComparison.Ordinal));
        Assert.Contains("Motivo: AlIniciar", noHaciaFalta.Mensaje, StringComparison.Ordinal);

        // Con la de ayer no copia al arrancar ni en los despertares antes de las 20:00.
        Assert.Empty(banco.Copiador.Copias);
        Assert.Empty(banco.CopiasEnLaCarpeta());
        Assert.Single(banco.Historial.Agregadas);
    }

    [Fact]
    public async Task Programador_CopiaAlIniciar_YA_las_20_VuelveACopiar()
    {
        await using var banco = new Banco(Local(Hoy, 19, 58));

        await banco.IniciarAsync();
        // Sin ningún backup, al arrancar copia (FR-08).
        Assert.Single(banco.Copiador.Copias);

        // A las 20:03 la copia de la mañana no es la programada: copia de nuevo y reemplaza la de hoy (D-20, AC-04).
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Equal(2, banco.Copiador.Copias.Count);
        Assert.Equal([Hoy], banco.CopiasEnLaCarpeta());
        Assert.Equal([(Hoy, Local(Hoy, 19, 58)), (Hoy, Local(Hoy, 20, 3))], banco.Historial.Agregadas);

        // Y no una tercera vez.
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Equal(2, banco.Copiador.Copias.Count);
    }

    [Fact]
    public async Task Programador_SaltoDeReloj_CopiaUnaSolaVez()
    {
        await using var banco = new Banco(Local(Hoy, 10, 0));
        banco.Historial.Agregadas.Add((Hoy.AddDays(-1), Local(Hoy.AddDays(-1), 20, 0)));
        await banco.IniciarAsync();

        // La PC se suspende a las 10:00 y vuelve a las 19:30: un solo despertar, todavía sin copia.
        await banco.AvanzarAsync(TimeSpan.FromHours(9.5));
        Assert.Empty(banco.Copiador.Copias);

        // Otra suspensión hasta las 21:10: el despertar compara con el historial, no con un contador, y copia una vez.
        await banco.AvanzarAsync(TimeSpan.FromMinutes(100));
        Assert.Single(banco.Copiador.Copias);
        Assert.Equal((Hoy, Local(Hoy, 21, 10)), banco.Historial.Agregadas[^1]);

        // El reloj salta hasta las 23:40 y siguen los despertares: no repite.
        await banco.AvanzarAsync(TimeSpan.FromMinutes(150));
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Single(banco.Copiador.Copias);

        // Un salto que cruza la medianoche hasta las 20:30 del día siguiente: una sola copia de ese día.
        await banco.AvanzarAsync(TimeSpan.FromMinutes(20 * 60 + 45));
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Equal(2, banco.Copiador.Copias.Count);
        Assert.Equal([Hoy, Hoy.AddDays(1)], banco.CopiasEnLaCarpeta());
    }

    [Fact]
    public async Task Programador_FallaDelBackup_SigueFuncionando()
    {
        await using var banco = new Banco(Local(Hoy, 19, 55));
        banco.Historial.Agregadas.Add((Hoy.AddDays(-1), Local(Hoy.AddDays(-1), 20, 0)));
        await banco.IniciarAsync();

        // 20:00: la copia falla (carpeta llena o desconectada). Es una falla del día: el caso de uso la registra con su tipo.
        banco.Copiador.FallarAlCopiar = true;
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Single(banco.Copiador.Copias);
        Assert.Empty(banco.CopiasEnLaCarpeta());
        Assert.Contains(banco.Registro.Lineas, l => l.Nivel == LogLevel.Error && l.TextoCompleto.Contains("Falla: Copia", StringComparison.Ordinal));

        // 20:05 y 20:10: el primer reintento espera 15 minutos.
        banco.Copiador.FallarAlCopiar = false;
        await banco.AvanzarPasosAsync(2);
        Assert.Single(banco.Copiador.Copias);

        // 20:15: el historial no se puede leer (excepción fuera del caso de uso). Queda en el log con el tipo, diciendo que
        // falló la consulta, y el servicio sigue.
        banco.Historial.FallasAlConsultar = 1;
        await banco.AvanzarAsync(CincoMinutos);
        var excepcion = Assert.Single(
            banco.Registro.Lineas,
            l => l.Categoria == typeof(ProgramadorBackup).FullName && l.Nivel == LogLevel.Error);
        Assert.Contains("consulta del historial", excepcion.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain("no se pudo pedir", excepcion.Mensaje, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), excepcion.Mensaje, StringComparison.Ordinal);
        Assert.Null(excepcion.Excepcion);
        Assert.Single(banco.Copiador.Copias);

        // 20:45 (segunda falla seguida: 30 minutos): otro backup tiene el candado (D-22). No hace nada y reintenta en el
        // próximo despertar, sin espaciar (D-24).
        var turno = banco.Candado.IntentarTomar();
        Assert.NotNull(turno);
        await banco.AvanzarAsync(TimeSpan.FromMinutes(30));
        Assert.Single(banco.Copiador.Copias);
        Assert.Contains(
            banco.Registro.Lineas,
            l => l.Mensaje.Contains("hay otro backup en curso", StringComparison.Ordinal));
        turno.Dispose();

        // 20:50: el servicio siguió y la copia del día sale.
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Equal(2, banco.Copiador.Copias.Count);
        Assert.Equal([Hoy], banco.CopiasEnLaCarpeta());
        Assert.False(banco.Programador.ExecuteTask!.IsCompleted);

        banco.AfirmarSinRutasEnElRegistro();
    }

    [Fact]
    public async Task Programador_AlIniciarFallido_ReintentaEspaciado()
    {
        // Sin la de ayer y con la copia fallando: el intento al arrancar falla.
        await using var banco = new Banco(Local(Hoy, 9, 0));
        banco.Copiador.FallarAlCopiar = true;
        await banco.IniciarAsync();
        Assert.Single(banco.Copiador.Copias);
        Assert.Empty(banco.CopiasEnLaCarpeta());

        // 09:05 y 09:10: no lo vuelve a pedir todavía, aunque la carpeta ya esté disponible.
        banco.Copiador.FallarAlCopiar = false;
        await banco.AvanzarPasosAsync(2);
        Assert.Single(banco.Copiador.Copias);

        // 09:15: lo reintenta y la copia sale con la fecha de hoy (D-11).
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Equal(2, banco.Copiador.Copias.Count);
        Assert.Equal([Hoy], banco.CopiasEnLaCarpeta());

        // Hecho: no lo vuelve a pedir.
        await banco.AvanzarPasosAsync(3);
        Assert.Equal(2, banco.Copiador.Copias.Count);
        banco.AfirmarSinRutasEnElRegistro();
    }

    [Fact]
    public async Task Programador_FallasRepetidas_EspacianLosReintentos()
    {
        await using var banco = new Banco(Local(Hoy, 9, 0));
        banco.Copiador.FallarAlCopiar = true;

        // 09:00: falla al arrancar. Los reintentos siguientes esperan 15, 30 y después 60 minutos.
        await banco.IniciarAsync();
        Assert.Single(banco.Copiador.Copias);
        await banco.AvanzarPasosAsync(2); // 09:10
        Assert.Single(banco.Copiador.Copias);
        await banco.AvanzarAsync(CincoMinutos); // 09:15
        Assert.Equal(2, banco.Copiador.Copias.Count);
        await banco.AvanzarPasosAsync(5); // 09:40
        Assert.Equal(2, banco.Copiador.Copias.Count);
        await banco.AvanzarAsync(CincoMinutos); // 09:45
        Assert.Equal(3, banco.Copiador.Copias.Count);
        await banco.AvanzarPasosAsync(11); // 10:40
        Assert.Equal(3, banco.Copiador.Copias.Count);
        await banco.AvanzarAsync(CincoMinutos); // 10:45
        Assert.Equal(4, banco.Copiador.Copias.Count);
        await banco.AvanzarPasosAsync(11); // 11:40: el tope es 60 minutos
        Assert.Equal(4, banco.Copiador.Copias.Count);
        await banco.AvanzarAsync(CincoMinutos); // 11:45
        Assert.Equal(5, banco.Copiador.Copias.Count);

        // 23:55: el programado también respeta el espaciado (un intento, y el próximo recién en una hora).
        await banco.AvanzarAsync(Local(Hoy, 23, 55) - banco.Reloj.GetUtcNow());
        Assert.Equal(6, banco.Copiador.Copias.Count);

        // 00:00: cambió el día local y el espaciado vuelve al principio: lo pide en este despertar.
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Equal(7, banco.Copiador.Copias.Count);
        await banco.AvanzarPasosAsync(2); // 00:10
        Assert.Equal(7, banco.Copiador.Copias.Count);

        // 00:15: la carpeta vuelve y el intento sale bien.
        banco.Copiador.FallarAlCopiar = false;
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Equal(8, banco.Copiador.Copias.Count);
        Assert.Equal([Hoy.AddDays(1)], banco.CopiasEnLaCarpeta());

        // Después de un intento bien, una falla nueva vuelve al primer escalón: a las 20:00 falla y reintenta a las 20:15,
        // no una hora después.
        banco.Copiador.FallarAlCopiar = true;
        await banco.AvanzarAsync(Local(Hoy.AddDays(1), 20, 0) - banco.Reloj.GetUtcNow());
        Assert.Equal(9, banco.Copiador.Copias.Count);
        await banco.AvanzarPasosAsync(2); // 20:10
        Assert.Equal(9, banco.Copiador.Copias.Count);
        await banco.AvanzarAsync(CincoMinutos); // 20:15
        Assert.Equal(10, banco.Copiador.Copias.Count);

        // Cada intento fallido deja un solo Error del caso de uso: unos pocos por día, no uno cada 5 minutos.
        Assert.Equal(
            9,
            banco.Registro.Lineas.Count(l => l.Categoria == HacerBackup.CategoriaRegistro && l.Nivel == LogLevel.Error));
        banco.AfirmarSinRutasEnElRegistro();
    }

    [Fact]
    public async Task Programador_FallaDeHistorial_NoRecopiaEnElDespertarSiguiente()
    {
        await using var banco = new Banco(Local(Hoy, 19, 55));
        banco.Historial.Agregadas.Add((Hoy.AddDays(-1), Local(Hoy.AddDays(-1), 20, 0)));
        banco.Historial.FallasAlConfirmar = 1;
        await banco.IniciarAsync();

        // 20:00: la copia queda verificada en la carpeta pero no se puede anotar (FallaBackup.Historial).
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Single(banco.Copiador.Copias);
        Assert.Equal([Hoy], banco.CopiasEnLaCarpeta());
        Assert.Single(banco.Historial.Agregadas);
        Assert.Contains(
            banco.Registro.Lineas,
            l => l.Nivel == LogLevel.Error && l.TextoCompleto.Contains("Falla: Historial", StringComparison.Ordinal));

        // 20:05 y 20:10: el historial todavía no tiene la copia programada, pero no recopia la base en cada despertar.
        await banco.AvanzarPasosAsync(2);
        Assert.Single(banco.Copiador.Copias);

        // 20:15: reintenta, y esta vez la copia queda anotada.
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Equal(2, banco.Copiador.Copias.Count);
        Assert.Equal((Hoy, Local(Hoy, 20, 15)), banco.Historial.Agregadas[^1]);
        banco.AfirmarSinRutasEnElRegistro();
    }

    [Fact]
    public async Task Programador_SinUbicacion_CopiaAlElegirla()
    {
        await using var banco = new Banco(Local(Hoy, 9, 0), conUbicacion: false);

        // Sin ubicación no hay nada que copiar; se consulta en cada despertar, solo con Debug y sin espaciar.
        await banco.IniciarAsync();
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Empty(banco.Copiador.Copias);
        Assert.Equal(
            2,
            banco.Registro.Lineas.Count(l => l.Mensaje.Contains("no hay una ubicación de backup elegida", StringComparison.Ordinal)));
        Assert.DoesNotContain(banco.Registro.Lineas, l => l.Nivel >= LogLevel.Warning);

        // Se elige la ubicación después del despertar de las 09:05: la primera copia sale en el despertar siguiente, no
        // recién a las 20:00.
        banco.Ubicacion.Ruta = banco.Carpeta;
        await banco.AvanzarAsync(CincoMinutos);
        Assert.Single(banco.Copiador.Copias);
        Assert.Equal([Hoy], banco.CopiasEnLaCarpeta());

        // Hecho: no lo vuelve a pedir.
        await banco.AvanzarPasosAsync(2);
        Assert.Single(banco.Copiador.Copias);
        banco.AfirmarSinRutasEnElRegistro();
    }

    [Fact]
    public async Task Programador_DetenerDuranteElBackup_ParcialSeLimpiaDespues()
    {
        var carpeta = Banco.NuevaCarpeta();
        try
        {
            // Primer arranque, sin ningún backup: la copia al arrancar queda trabada escribiendo el temporal.
            await using (var primero = new Banco(Local(Hoy, 9, 0), carpeta))
            {
                primero.Copiador.Trabar = true;
                // La limpieza después de la cancelación no puede borrar el temporal (archivo en uso).
                primero.Almacen.FallarAlLimpiar = true;
                await primero.Programador.StartAsync(CancellationToken.None);
                await primero.Copiador.Copiando.Task.WaitAsync(Banco.PlazoDeSeguridad);
                Assert.True(File.Exists(Path.Combine(carpeta, CopiaBackup.NombreParcial(Hoy))));

                // Detener la Api durante la copia: la cancelación llega al copiador y el servicio termina sin excepción.
                await primero.Programador.StopAsync(CancellationToken.None);
                Assert.True(primero.Copiador.Cancelado);
                Assert.Equal(TaskStatus.RanToCompletion, primero.Programador.ExecuteTask!.Status);
                Assert.DoesNotContain(
                    primero.Registro.Lineas,
                    l => l.Categoria == typeof(ProgramadorBackup).FullName && l.Nivel >= LogLevel.Warning);
                Assert.Empty(primero.Historial.Agregadas);
                Assert.Empty(primero.CopiasEnLaCarpeta());
                // El temporal interrumpido quedó en la carpeta.
                Assert.True(File.Exists(Path.Combine(carpeta, CopiaBackup.NombreParcial(Hoy))));
                primero.AfirmarSinRutasEnElRegistro();
            }

            // Próximo arranque: antes de copiar se borra el temporal interrumpido (D-23) y la copia del día sale bien.
            await using (var segundo = new Banco(Local(Hoy, 9, 30), carpeta))
            {
                await segundo.IniciarAsync();
                Assert.Equal([false], segundo.Copiador.TemporalExistiaAlCopiar);
                Assert.Equal([Hoy], segundo.CopiasEnLaCarpeta());
                Assert.Equal([CopiaBackup.Nombre(Hoy)], Directory.GetFiles(carpeta).Select(Path.GetFileName));
                segundo.AfirmarSinRutasEnElRegistro();
            }
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(carpeta)!, recursive: true);
        }
    }

    [Fact]
    public void Program_ProgramadorSoloEnLaRamaDelServidor()
    {
        // La rama del servidor lo registra (FR-04, FR-08, ADR-030); la composición compartida con los comandos de consola
        // no, así que un comando nunca hace un backup.
        var servidor = WebApplication.CreateBuilder();
        Program.ConfigurarServidor(servidor);
        Assert.Contains(servidor.Services, EsElProgramador);

        var comando = WebApplication.CreateBuilder(Program.OpcionesDelComando());
        Program.ConfigurarServicios(comando);
        Assert.DoesNotContain(comando.Services, EsElProgramador);

        // D-25: la suite de la Api no hace backups: la fábrica lo quita.
        using var fabrica = new FabricaApi();
        Assert.DoesNotContain(fabrica.Services.GetServices<IHostedService>(), servicio => servicio is ProgramadorBackup);
    }

    private static bool EsElProgramador(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(ProgramadorBackup);

    /// <summary>El instante UTC de la hora local indicada del día <paramref name="fecha"/> en la zona de la prueba.</summary>
    private static DateTimeOffset Local(DateOnly fecha, int hora, int minuto) =>
        new DateTimeOffset(fecha.ToDateTime(new TimeOnly(hora, minuto)), ZonaLocal.BaseUtcOffset).ToUniversalTime();

    /// <summary>
    /// El programador real armado sobre un contenedor con el caso de uso real (scoped) y sus puertos: ubicación fija en
    /// una carpeta temporal con el testigo, el almacén de archivos real, un copiador falso, un historial en memoria y el
    /// candado real.
    /// </summary>
    private sealed class Banco : IAsyncDisposable
    {
        /// <summary>Solo corta una prueba colgada: ninguna afirmación depende de este plazo.</summary>
        public static readonly TimeSpan PlazoDeSeguridad = TimeSpan.FromSeconds(30);

        private readonly ServiceProvider _servicios;
        private readonly bool _carpetaPropia;
        private bool _detenido;

        public Banco(DateTimeOffset inicioUtc, string? carpeta = null, bool conUbicacion = true)
        {
            _carpetaPropia = carpeta is null;
            Carpeta = carpeta ?? NuevaCarpeta();
            Ubicacion = new UbicacionVariable { Ruta = conUbicacion ? Carpeta : null };
            Reloj = new RelojDePrueba(inicioUtc);
            Reloj.SetLocalTimeZone(ZonaLocal);
            Historial = new HistorialEnMemoria(Reloj);
            Almacen = new AlmacenQueFallaAlLimpiar(new AlmacenCopiasArchivo());
            Copiador = new CopiadorFalso();

            var servicios = new ServiceCollection();
            servicios.AddLogging(log =>
            {
                log.SetMinimumLevel(LogLevel.Trace);
                log.AddProvider(Registro);
            });
            servicios.AddSingleton<TimeProvider>(Reloj);
            servicios.AddSingleton<IUbicacionBackupRepository>(Ubicacion);
            servicios.AddSingleton<IHistorialBackupRepository>(Historial);
            servicios.AddSingleton<IUnidadDeTrabajo>(Historial);
            servicios.AddSingleton<ICopiadorBackup>(Copiador);
            servicios.AddSingleton<IAlmacenCopias>(Almacen);
            servicios.AddSingleton<ICandadoBackup>(Candado);
            servicios.AddScoped<HacerBackup>();
            servicios.AddScoped<ConsultarBackupProgramado>();
            servicios.AddSingleton<ProgramadorBackup>();
            _servicios = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            Programador = _servicios.GetRequiredService<ProgramadorBackup>();
        }

        public string Carpeta { get; }

        public UbicacionVariable Ubicacion { get; }

        public RelojDePrueba Reloj { get; }

        public HistorialEnMemoria Historial { get; }

        public AlmacenQueFallaAlLimpiar Almacen { get; }

        public CopiadorFalso Copiador { get; }

        public CandadoBackup Candado { get; } = new();

        public RegistroEnMemoria Registro { get; } = new();

        public ProgramadorBackup Programador { get; }

        /// <summary>Una carpeta temporal nueva con el testigo en la ruta, fuera de <c>/mnt</c>.</summary>
        public static string NuevaCarpeta()
        {
            var carpeta = Path.Combine(
                Path.GetTempPath(), "optica-pruebas-programador", Guid.NewGuid().ToString("N"), "Pendrive " + Testigo);
            Directory.CreateDirectory(carpeta);
            return carpeta;
        }

        /// <summary>Arranca el programador y espera a que termine el intento al arrancar y se duerma.</summary>
        public async Task IniciarAsync()
        {
            await Programador.StartAsync(CancellationToken.None);
            await Reloj.EsperarQueDuermaAsync();
        }

        /// <summary>Adelanta el reloj (un despertar) y espera a que el programador termine la vuelta y se vuelva a dormir.</summary>
        public async Task AvanzarAsync(TimeSpan lapso)
        {
            Reloj.Advance(lapso);
            await Reloj.EsperarQueDuermaAsync();
        }

        /// <summary><paramref name="pasos"/> despertares de 5 minutos, uno por uno.</summary>
        public async Task AvanzarPasosAsync(int pasos)
        {
            for (var paso = 0; paso < pasos; paso++)
            {
                await AvanzarAsync(CincoMinutos);
            }
        }

        /// <summary>Las fechas de las copias terminadas en la carpeta, ordenadas.</summary>
        public List<DateOnly> CopiasEnLaCarpeta() =>
            [.. new AlmacenCopiasArchivo().ListarCopias(Carpeta).Order()];

        public void AfirmarSinRutasEnElRegistro()
        {
            Assert.NotEmpty(Registro.Lineas);
            Assert.DoesNotContain(Registro.Lineas, l => l.TextoCompleto.Contains(Testigo, StringComparison.Ordinal));
        }

        public async ValueTask DisposeAsync()
        {
            if (!_detenido)
            {
                _detenido = true;
                await Programador.StopAsync(CancellationToken.None);
            }

            await _servicios.DisposeAsync();
            if (_carpetaPropia)
            {
                Directory.Delete(Path.GetDirectoryName(Carpeta)!, recursive: true);
            }
        }
    }

    /// <summary>
    /// El reloj falso que avisa cada vez que alguien crea un temporizador: el programador crea uno por cada espera, así
    /// que un temporizador nuevo dice que terminó la vuelta y volvió a dormir.
    /// </summary>
    private sealed class RelojDePrueba(DateTimeOffset inicio) : FakeTimeProvider(inicio)
    {
        private readonly SemaphoreSlim _esperas = new(0);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var temporizador = base.CreateTimer(callback, state, dueTime, period);
            _esperas.Release();
            return temporizador;
        }

        public async Task EsperarQueDuermaAsync() =>
            Assert.True(await _esperas.WaitAsync(Banco.PlazoDeSeguridad), "El programador no volvió a dormir.");
    }

    /// <summary>La ubicación elegida, que la prueba puede cambiar (null: sin ubicación).</summary>
    private sealed class UbicacionVariable : IUbicacionBackupRepository
    {
        public string? Ruta { get; set; }

        public Task<UbicacionBackup?> ObtenerAsync(CancellationToken cancelacion = default) =>
            Task.FromResult(Ruta is null ? null : UbicacionBackup.Leer(Ruta));

        public void Reemplazar(UbicacionBackup ubicacion) =>
            throw new NotSupportedException("El programador no graba la ubicación.");
    }

    /// <summary>
    /// Escribe un temporal de texto ("completa") y lo verifica por su contenido. Puede fallar al copiar o quedar trabado
    /// en la copia hasta que lo cancelen, con el temporal a medio escribir.
    /// </summary>
    private sealed class CopiadorFalso : ICopiadorBackup
    {
        private const string Completa = "completa";

        public List<string> Copias { get; } = [];

        public List<bool> TemporalExistiaAlCopiar { get; } = [];

        public bool FallarAlCopiar { get; set; }

        public bool Trabar { get; set; }

        public bool Cancelado { get; private set; }

        public TaskCompletionSource Copiando { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task CopiarAsync(string rutaTemporal, CancellationToken cancelacion = default)
        {
            Copias.Add(rutaTemporal);
            TemporalExistiaAlCopiar.Add(File.Exists(rutaTemporal));
            if (FallarAlCopiar)
            {
                throw new IOException("El dispositivo no está listo: " + rutaTemporal);
            }

            if (Trabar)
            {
                await File.WriteAllTextAsync(rutaTemporal, "a medio escribir", CancellationToken.None);
                Copiando.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancelacion);
                }
                catch (OperationCanceledException)
                {
                    Cancelado = true;
                    throw;
                }
            }

            await File.WriteAllTextAsync(rutaTemporal, Completa, CancellationToken.None);
        }

        public async Task<bool> VerificarAsync(string rutaTemporal, CancellationToken cancelacion = default) =>
            File.Exists(rutaTemporal) && await File.ReadAllTextAsync(rutaTemporal, cancelacion) == Completa;
    }

    /// <summary>
    /// El almacén real; con <see cref="FallarAlLimpiar"/>, el borrado de temporales que sigue a una copia empezada falla
    /// (como un archivo en uso) y el temporal queda en la carpeta.
    /// </summary>
    private sealed class AlmacenQueFallaAlLimpiar(IAlmacenCopias real) : IAlmacenCopias
    {
        private int _borrados;

        public bool FallarAlLimpiar { get; set; }

        public string RutaTemporal(string carpeta, DateOnly fecha) => real.RutaTemporal(carpeta, fecha);

        public bool SePuedeLeer(string carpeta) => real.SePuedeLeer(carpeta);

        public IReadOnlyList<DateOnly> ListarCopias(string carpeta) => real.ListarCopias(carpeta);

        public IReadOnlyList<DateOnly> ListarTemporalesSinCopia(string carpeta) => real.ListarTemporalesSinCopia(carpeta);

        public void RenombrarTemporal(string carpeta, DateOnly fecha) => real.RenombrarTemporal(carpeta, fecha);

        public int BorrarCopias(string carpeta, IReadOnlyCollection<DateOnly> fechas) => real.BorrarCopias(carpeta, fechas);

        public void BorrarTemporales(string carpeta)
        {
            // El primer borrado es el de los huérfanos, antes de copiar; el siguiente es la limpieza.
            if (FallarAlLimpiar && Interlocked.Increment(ref _borrados) > 1)
            {
                throw new IOException("El archivo está en uso: " + carpeta);
            }

            real.BorrarTemporales(carpeta);
        }
    }

    /// <summary>
    /// Historial y unidad de trabajo en memoria, compartidos por los alcances. La copia programada se decide como la
    /// implementación real: una fila de esa fecha hecha desde las 20:00 locales de ese día.
    /// </summary>
    private sealed class HistorialEnMemoria(TimeProvider reloj) : IHistorialBackupRepository, IUnidadDeTrabajo
    {
        private readonly List<(DateOnly Fecha, DateTimeOffset MomentoUtc)> _pendientes = [];

        public List<(DateOnly Fecha, DateTimeOffset MomentoUtc)> Agregadas { get; } = [];

        /// <summary>Cuántas consultas más de la copia programada fallan.</summary>
        public int FallasAlConsultar { get; set; }

        public void Agregar(DateOnly fecha, DateTimeOffset momentoUtc) => _pendientes.Add((fecha, momentoUtc));

        public Task<DateOnly?> UltimaFechaHastaAsync(DateOnly hoy, CancellationToken cancelacion = default) =>
            Task.FromResult(Agregadas.Where(c => c.Fecha <= hoy).Select(c => (DateOnly?)c.Fecha).Max());

        public Task<bool> HuboCopiaProgramadaAsync(DateOnly fecha, CancellationToken cancelacion = default)
        {
            if (FallasAlConsultar > 0)
            {
                FallasAlConsultar--;
                throw new InvalidOperationException("La base no está disponible en " + Testigo);
            }

            var desde = fecha.ToDateTime(CopiaBackup.HoraProgramada);
            var hasta = fecha.AddDays(1).ToDateTime(TimeOnly.MinValue);
            return Task.FromResult(Agregadas.Any(c => c.Fecha == fecha
                && TimeZoneInfo.ConvertTime(c.MomentoUtc, reloj.LocalTimeZone).DateTime is var local
                && local >= desde && local < hasta));
        }

        public Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default) =>
            operacion(cancelacion);

        /// <summary>Cuántas confirmaciones más fallan (la copia ya renombrada queda sin anotar).</summary>
        public int FallasAlConfirmar { get; set; }

        public Task ConfirmarAsync(CancellationToken cancelacion = default)
        {
            cancelacion.ThrowIfCancellationRequested();
            if (FallasAlConfirmar > 0)
            {
                FallasAlConfirmar--;
                _pendientes.Clear();
                throw new InvalidOperationException("La base no está disponible.");
            }

            Agregadas.AddRange(_pendientes);
            _pendientes.Clear();
            return Task.CompletedTask;
        }
    }
}
