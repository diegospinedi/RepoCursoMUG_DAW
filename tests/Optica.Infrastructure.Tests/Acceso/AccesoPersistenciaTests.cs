using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Acceso;
using Optica.Application.Comun;
using Optica.Domain.Acceso;
using Optica.Infrastructure.Persistencia;
using Optica.Infrastructure.Seguridad;

namespace Optica.Infrastructure.Tests.Acceso;

public class AccesoPersistenciaTests
{
    private static readonly DateTimeOffset Instante = new(2026, 10, 30, 15, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task DefinirContrasena_De8Caracteres_GuardaSoloHash()
    {
        const string contrasena = "Clave#28"; // 8 caracteres
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        var contexto = baseTemporal.CrearContexto();

        var resultado = await CrearDefinir(contexto, hasher).EjecutarAsync(contrasena);

        Assert.Equal(TipoResultado.Exito, resultado.Tipo);

        var cadena = CadenaDe(baseTemporal);
        var credenciales = Filas(cadena, "SELECT Id, Hash, DefinidaEn FROM CredencialAcceso");
        var credencial = Assert.Single(credenciales);
        Assert.Equal("1", credencial[0]);
        var hash = credencial[1];
        Assert.DoesNotContain(contrasena, hash, StringComparison.Ordinal);
        Assert.StartsWith("PBKDF2-SHA256$600000$", hash, StringComparison.Ordinal);
        Assert.True(hasher.Verificar(contrasena, hash));
        Assert.False(hasher.Verificar("Clave#29", hash));
        Assert.Equal(Instante.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), credencial[2]);

        // La definición queda registrada junto con la credencial.
        var evento = Assert.Single(Filas(cadena, "SELECT Tipo, Resultado, FechaHora FROM EventoAcceso"));
        Assert.Equal(["definicion", "exito", Instante.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)], evento);

        // La contraseña en claro no aparece en ninguna columna de ninguna tabla...
        foreach (var tabla in new[] { "CredencialAcceso", "EventoAcceso" })
        {
            foreach (var fila in Filas(cadena, $"SELECT * FROM \"{tabla}\""))
            {
                Assert.DoesNotContain(fila, valor => valor.Contains(contrasena, StringComparison.Ordinal));
            }
        }

        // ...ni en los archivos de la base (incluido el WAL).
        await contexto.DisposeAsync();
        BaseTemporal.LiberarConexiones(baseTemporal.Directorio);
        foreach (var archivo in Directory.GetFiles(baseTemporal.Directorio))
        {
            if (Path.GetFileName(archivo) == CandadoInstanciaArchivo.NombreArchivo)
            {
                // El candado de instancia sigue tomado (abierto en exclusiva) mientras viva la base temporal: no se
                // puede leer, pero tiene que estar vacío.
                Assert.Equal(0, new FileInfo(archivo).Length);
                continue;
            }

            var bytes = File.ReadAllBytes(archivo);
            Assert.False(
                Contiene(bytes, Encoding.UTF8.GetBytes(contrasena)) || Contiene(bytes, Encoding.Unicode.GetBytes(contrasena)),
                $"La contraseña en claro aparece en {Path.GetFileName(archivo)}.");
        }
    }

    [Fact]
    public async Task DefinirContrasena_ErrorAlConfirmar_NoGuardaCredencialNiEvento()
    {
        using var baseTemporal = new BaseTemporal();
        var falla = new FallaDespuesDeGuardar();
        var opciones = new DbContextOptionsBuilder<OpticaDbContext>();
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().Configurar(opciones);
        opciones.AddInterceptors(falla);
        await using var contexto = new OpticaDbContext(opciones.Options);

        await Assert.ThrowsAsync<FallaForzadaException>(
            () => CrearDefinir(contexto, new HasherContrasenaPbkdf2()).EjecutarAsync("clave-valida"));

        // Las dos filas llegaron a escribirse en la transacción antes de la falla...
        Assert.Equal(2, falla.FilasGuardadas);

        // ...y la reversión no dejó ninguna.
        var cadena = CadenaDe(baseTemporal);
        Assert.Empty(Filas(cadena, "SELECT Id FROM CredencialAcceso"));
        Assert.Empty(Filas(cadena, "SELECT Id FROM EventoAcceso"));
    }

    [Fact]
    public async Task DefinirContrasena_CredencialGrabadaEnParalelo_Conflicto()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        // Otra definición graba su credencial entre la consulta y la confirmación de esta.
        var hasher = new HasherQueSeAdelanta(cadena);
        var contexto = baseTemporal.CrearContexto();

        var resultado = await CrearDefinir(contexto, hasher).EjecutarAsync("clave-valida");

        Assert.Equal(TipoResultado.Conflicto, resultado.Tipo);
        var credencial = Assert.Single(Filas(cadena, "SELECT Hash FROM CredencialAcceso"));
        Assert.Equal(HasherQueSeAdelanta.HashDeLaOtra, credencial[0]);
        Assert.Empty(Filas(cadena, "SELECT Id FROM EventoAcceso"));
    }

    [Fact]
    public async Task UnidadDeTrabajo_SegundaCredencialConId2_ConflictoSinGuardar()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        // Id = 2 viola CHECK (Id = 1): SQLITE_CONSTRAINT_CHECK (275).
        contexto.CredencialesAcceso.Add(new CredencialAccesoFila
        {
            Id = 2,
            Hash = HasherQueSeAdelanta.HashDeLaOtra,
            DefinidaEn = Instante.UtcDateTime,
        });
        new RegistroEventosAccesoEf(contexto).Registrar(
            new EventoAcceso(TipoEventoAcceso.Definicion, ResultadoEventoAcceso.Exito, Instante.UtcDateTime));

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        // Clasificado como conflicto (otro CHECK se propagaría como DbUpdateException, ver la prueba siguiente), con
        // mensaje genérico y sin la causa de EF: sus Entries llevan el hash de la credencial duplicada.
        Assert.Equal("Los datos a guardar chocan con datos existentes.", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(HasherQueSeAdelanta.HashDeLaOtra, error.ToString(), StringComparison.Ordinal);
        Assert.Empty(contexto.ChangeTracker.Entries());
        var cadena = CadenaDe(baseTemporal);
        Assert.Empty(Filas(cadena, "SELECT Id FROM CredencialAcceso"));
        Assert.Empty(Filas(cadena, "SELECT Id FROM EventoAcceso"));
    }

    [Fact]
    public async Task UnidadDeTrabajo_ClavePrimariaDuplicada_ConflictoSinCausaNiHash()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        // Otra definición ya grabó la credencial (Id = 1): agregar otra viola la clave primaria (1555).
        Ejecutar(cadena, "INSERT INTO CredencialAcceso (Id, Hash, DefinidaEn) VALUES (1, '"
            + HasherQueSeAdelanta.HashDeLaOtra + "', '2026-10-30T14:00:00.0000000Z')");
        var contexto = baseTemporal.CrearContexto();
        var hash = new HasherContrasenaPbkdf2().Calcular("clave-valida");
        new CredencialAccesoRepository(contexto).Agregar(new CredencialAcceso(hash, Instante.UtcDateTime));

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Equal("Los datos a guardar chocan con datos existentes.", error.Message);
        Assert.Null(error.InnerException);
        var completa = error.ToString();
        Assert.DoesNotContain(hash, completa, StringComparison.Ordinal);
        Assert.DoesNotContain(HasherQueSeAdelanta.HashDeLaOtra, completa, StringComparison.Ordinal);
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Equal([HasherQueSeAdelanta.HashDeLaOtra], Assert.Single(Filas(cadena, "SELECT Hash FROM CredencialAcceso")));
    }

    [Fact]
    public async Task UnidadDeTrabajo_OtraRestriccionCheck_SePropagaComoError()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        // Solo en esta base de prueba: EventoAcceso con un CHECK ajeno a la unicidad de la credencial.
        Ejecutar(cadena, """
            DROP TABLE "EventoAcceso";
            CREATE TABLE "EventoAcceso" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_EventoAcceso" PRIMARY KEY AUTOINCREMENT,
                "FechaHora" TEXT NOT NULL,
                "Tipo" TEXT NOT NULL,
                "Resultado" TEXT NOT NULL,
                CONSTRAINT "CK_Prueba_SinSalida" CHECK ("Tipo" <> 'salida'));
            """);
        var contexto = baseTemporal.CrearContexto();
        new RegistroEventosAccesoEf(contexto).Registrar(
            new EventoAcceso(TipoEventoAcceso.Salida, ResultadoEventoAcceso.Exito, Instante.UtcDateTime));

        var error = await Record.ExceptionAsync(() => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        var actualizacion = Assert.IsType<DbUpdateException>(error);
        var sqlite = Assert.IsType<SqliteException>(actualizacion.InnerException);
        Assert.Equal(275, sqlite.SqliteExtendedErrorCode);
        Assert.Contains("CK_Prueba_SinSalida", sqlite.Message, StringComparison.Ordinal);
        Assert.Empty(Filas(cadena, "SELECT Id FROM EventoAcceso"));
    }

    [Fact]
    public async Task DefinirEIngresar_SustitutoSuelto_RechazadoSinExcepcion()
    {
        const string invalida = "abcdefg\uD800";
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        var cadena = CadenaDe(baseTemporal);

        // Definir: inválida, sin excepción y sin guardar nada.
        Resultado? definicion = null;
        var errorAlDefinir = await Record.ExceptionAsync(async () =>
            definicion = await CrearDefinir(baseTemporal.CrearContexto(), hasher).EjecutarAsync(invalida));
        Assert.Null(errorAlDefinir);
        Assert.NotNull(definicion);
        Assert.Equal(TipoResultado.Invalido, definicion.Tipo);
        var errorValidacion = Assert.Single(definicion.Errores);
        Assert.Equal("contrasena", errorValidacion.Campo);
        Assert.Equal("La contraseña tiene caracteres no válidos. Escribila de nuevo.", errorValidacion.Accion);
        Assert.Empty(Filas(cadena, "SELECT Id FROM CredencialAcceso"));
        Assert.Empty(Filas(cadena, "SELECT Id FROM EventoAcceso"));

        // Ingresar: con una contraseña válida definida, el sustituto suelto es inválido, sin excepción y sin evento
        // (decisión del usuario del 2026-10-31, ADR-005).
        Assert.True((await CrearDefinir(baseTemporal.CrearContexto(), hasher).EjecutarAsync("clave-valida")).EsExito);
        var contexto = baseTemporal.CrearContexto();
        var ingresar = new IngresarConContrasena(
            new CredencialAccesoRepository(contexto),
            new RegistroEventosAccesoEf(contexto),
            hasher,
            new UnidadDeTrabajoEf(contexto),
            new Reloj());
        IntentoIngreso? ingreso = null;
        var errorAlIngresar = await Record.ExceptionAsync(async () => ingreso = await ingresar.EjecutarAsync(invalida));
        Assert.Null(errorAlIngresar);
        Assert.Equal(ResultadoIngreso.Invalido, ingreso?.Tipo);
        Assert.Equal(
            [["definicion", "exito"]],
            Filas(cadena, "SELECT Tipo, Resultado FROM EventoAcceso ORDER BY Id"));
    }

    [Fact]
    public async Task CredencialGuardada_OtroContexto_SeLeeYPermiteIngresar()
    {
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        await CrearDefinir(baseTemporal.CrearContexto(), hasher).EjecutarAsync("clave-valida");

        var contexto = baseTemporal.CrearContexto();
        var repositorio = new CredencialAccesoRepository(contexto);
        Assert.True((await new ConsultarEstadoAcceso(repositorio).EjecutarAsync()).ContrasenaDefinida);
        var credencial = await repositorio.ObtenerAsync();
        Assert.NotNull(credencial);
        Assert.Equal(Instante.UtcDateTime, credencial.DefinidaEn);
        Assert.Equal(DateTimeKind.Utc, credencial.DefinidaEn.Kind);

        var ingresar = new IngresarConContrasena(
            repositorio, new RegistroEventosAccesoEf(contexto), hasher, new UnidadDeTrabajoEf(contexto), new Reloj());
        Assert.Equal(ResultadoIngreso.Rechazo, (await ingresar.EjecutarAsync("otra-clave")).Tipo);
        Assert.Equal(ResultadoIngreso.Exito, (await ingresar.EjecutarAsync("clave-valida")).Tipo);
        await new CerrarSesion(new RegistroEventosAccesoEf(contexto), new UnidadDeTrabajoEf(contexto), new Reloj())
            .EjecutarAsync();

        var eventos = Filas(CadenaDe(baseTemporal), "SELECT Tipo, Resultado FROM EventoAcceso ORDER BY Id");
        Assert.Equal(
            [["definicion", "exito"], ["ingreso", "rechazo"], ["ingreso", "exito"], ["salida", "exito"]],
            eventos);
    }

    [Fact]
    public void AgregarInfraestructura_RegistraServiciosDeAcceso()
    {
        using var baseTemporal = new BaseTemporal();
        using var alcance = baseTemporal.Servicios.CreateScope();
        var proveedor = alcance.ServiceProvider;

        Assert.IsType<CredencialAccesoRepository>(proveedor.GetRequiredService<ICredencialAccesoRepository>());
        Assert.IsType<RegistroEventosAccesoEf>(proveedor.GetRequiredService<IRegistroEventosAcceso>());
        Assert.IsType<UnidadDeTrabajoEf>(proveedor.GetRequiredService<IUnidadDeTrabajo>());
        Assert.IsType<HasherContrasenaPbkdf2>(proveedor.GetRequiredService<IHasherContrasena>());
        Assert.NotNull(proveedor.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task RegistroEventos_TextoDesconocido_IgnoradoSinError()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        // Filas de otra versión: textos que esta no conoce (y fechas ilegibles), entre rechazos conocidos. Ninguna
        // cuenta ni reinicia, y como no se leen, su fecha no hace fallar la consulta. La comparación distingue
        // mayúsculas: 'INGRESO'/'exito' no reinicia y 'ingreso'/'RECHAZO' no cuenta (su fecha es legible a propósito,
        // para que contarla se vea en la lista y no como un error de lectura).
        Ejecutar(cadena, """
            INSERT INTO EventoAcceso (Tipo, Resultado, FechaHora) VALUES
                ('ingreso', 'rechazo', '2026-11-03T12:00:00.0000000Z'),
                ('futuro', 'exito', 'no es una fecha'),
                ('ingreso', 'desconocido', 'tampoco'),
                ('INGRESO', 'EXITO', '???'),
                ('definicion', 'Exito', '???'),
                ('ingreso', 'rechazo', '2026-11-03T12:00:10.0000000Z'),
                ('INGRESO', 'exito', '???'),
                ('ingreso', 'RECHAZO', '2026-11-03T12:00:15.0000000Z'),
                ('ingreso', 'rechazo', '2026-11-03T12:00:20.0000000Z');
            """);
        var contexto = baseTemporal.CrearContexto();

        IReadOnlyList<DateTime>? rechazos = null;
        var error = await Record.ExceptionAsync(async () =>
            rechazos = await new RegistroEventosAccesoEf(contexto).ObtenerRechazosIngresoDesdeUltimoReinicioAsync());

        Assert.Null(error);
        Assert.Equal(
            [
                new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 3, 12, 0, 10, DateTimeKind.Utc),
                new DateTime(2026, 11, 3, 12, 0, 20, DateTimeKind.Utc),
            ],
            rechazos);
    }

    [Fact]
    public async Task RegistroEventos_RechazosDesdeUltimoReinicio_OrdenadosPorId()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var t0 = Instante.UtcDateTime;
        var contexto = baseTemporal.CrearContexto();
        var registro = new RegistroEventosAccesoEf(contexto);

        // Sin eventos, la cuenta está vacía.
        Assert.Empty(await registro.ObtenerRechazosIngresoDesdeUltimoReinicioAsync());

        // El reinicio tiene una fecha anterior a la del rechazo previo, pero un Id mayor: manda el Id. Después, la hora
        // de la PC se atrasa y los rechazos quedan con fechas fuera de orden. Bloqueado, el rechazo de un cambio y la
        // salida no cuentan ni reinician.
        await Registrar(contexto,
            (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, t0.AddMinutes(1)),
            (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Exito, t0.AddHours(-2)),
            (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, t0.AddMinutes(10)),
            (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, t0.AddHours(-1)),
            (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Bloqueado, t0.AddMinutes(11)),
            (TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Rechazo, t0.AddMinutes(12)),
            (TipoEventoAcceso.Salida, ResultadoEventoAcceso.Exito, t0.AddMinutes(13)),
            (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, t0.AddMinutes(-30)));

        var rechazos = await registro.ObtenerRechazosIngresoDesdeUltimoReinicioAsync();

        Assert.Equal([t0.AddMinutes(10), t0.AddHours(-1), t0.AddMinutes(-30)], rechazos);
        Assert.All(rechazos, fecha => Assert.Equal(DateTimeKind.Utc, fecha.Kind));

        // Cada evento que reinicia deja la cuenta en cero, y el rechazo siguiente vuelve a contar.
        foreach (var (tipo, resultado) in PoliticaBloqueo.EventosQueReinician)
        {
            await Registrar(contexto, (tipo, resultado, t0.AddMinutes(20)));
            Assert.Empty(await registro.ObtenerRechazosIngresoDesdeUltimoReinicioAsync());

            await Registrar(contexto, (TipoEventoAcceso.Ingreso, ResultadoEventoAcceso.Rechazo, t0.AddMinutes(21)));
            Assert.Equal([t0.AddMinutes(21)], await registro.ObtenerRechazosIngresoDesdeUltimoReinicioAsync());
        }

        // Los textos nuevos quedan como los declara la spec.
        var textos = Filas(cadena, "SELECT DISTINCT Tipo || '/' || Resultado FROM EventoAcceso").Select(f => f[0]);
        Assert.Contains("ingreso/bloqueado", textos);
        Assert.Contains("cambio_contrasena/rechazo", textos);
        Assert.Contains("cambio_contrasena/exito", textos);
        Assert.Contains("restablecimiento/exito", textos);
    }

    [Fact]
    public void RegistroEventos_CadaValorTieneTextoYUnoFueraDelEnumSeRechaza()
    {
        // Todo valor declarado tiene su texto, distinto de los demás: un valor nuevo sin texto rompe esta prueba.
        var tipos = Enum.GetValues<TipoEventoAcceso>().Select(RegistroEventosAccesoEf.TextoDe).ToList();
        var resultados = Enum.GetValues<ResultadoEventoAcceso>().Select(RegistroEventosAccesoEf.TextoDe).ToList();
        Assert.Equal(tipos.Count, tipos.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(resultados.Count, resultados.Distinct(StringComparer.Ordinal).Count());

        Assert.Throws<ArgumentOutOfRangeException>(() => RegistroEventosAccesoEf.TextoDe((TipoEventoAcceso)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegistroEventosAccesoEf.TextoDe((ResultadoEventoAcceso)99));
    }

    [Fact]
    public void Migracion_IndiceTipoResultado_CreadoYRevertible()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();

        // Las migraciones del ensamblado, en orden, y todas aplicadas.
        var migraciones = contexto.Database.GetMigrations().ToList();
        Assert.Equal(
            [
                "Inicial", "IndiceEventoAccesoTipoResultado", "TokenConcurrenciaCredencial", "ConfiguracionNegocio", "Catalogo",
                "CatalogoActivo", "Presupuestos", "CierrePresupuestos",
            ],
            migraciones.Select(m => m[(m.IndexOf('_') + 1)..]));
        Assert.Equal(migraciones.Order(StringComparer.Ordinal), migraciones);
        Assert.Equal("20261030220627_Inicial", migraciones[0]);
        Assert.Equal(migraciones, contexto.Database.GetAppliedMigrations());

        // El índice existe, no es único, cubre (Tipo, Resultado) en ese orden y lo usa la consulta real de rechazos.
        AfirmarIndiceTipoResultado(cadena);
        AfirmarPlanUsaIndiceTipoResultado(contexto, cadena);

        Ejecutar(cadena,
            "INSERT INTO EventoAcceso (Tipo, Resultado, FechaHora) VALUES ('ingreso', 'rechazo', '2026-11-03T12:00:00.0000000Z')");
        var migrador = contexto.GetService<IMigrator>();

        // La tabla de configuración existe con su fila inicial.
        Assert.Contains("ConfiguracionNegocio", Tablas(cadena));
        Assert.Single(Filas(cadena, "SELECT Id FROM ConfiguracionNegocio"));

        // La tabla del catálogo existe, vacía, con su índice único.
        Assert.Contains("Articulo", Tablas(cadena));
        Assert.Empty(Filas(cadena, "SELECT Id FROM Articulo"));
        AfirmarIndiceClaveArticulo(cadena);

        // Las tablas de presupuestos existen, vacías.
        Assert.Contains("Presupuesto", Tablas(cadena));
        Assert.Contains("LineaPresupuesto", Tablas(cadena));
        Assert.Empty(Filas(cadena, "SELECT Numero FROM Presupuesto"));

        // Revertir hasta Inicial elimina el índice, la tabla de configuración, la del catálogo y las de presupuestos: los
        // datos de los eventos y el índice de FechaHora quedan.
        migrador.Migrate(migraciones[0]);
        Assert.DoesNotContain("IX_EventoAcceso_Tipo_Resultado", Indices(cadena));
        Assert.Contains("IX_EventoAcceso_FechaHora", Indices(cadena));
        Assert.DoesNotContain("ConfiguracionNegocio", Tablas(cadena));
        Assert.DoesNotContain("Articulo", Tablas(cadena));
        Assert.DoesNotContain("Presupuesto", Tablas(cadena));
        Assert.DoesNotContain("LineaPresupuesto", Tablas(cadena));
        Assert.Single(Filas(cadena, "SELECT Id FROM EventoAcceso"));
        Assert.Equal([migraciones[0]], contexto.Database.GetAppliedMigrations());

        // Y volver a aplicarlas recrea el índice, la tabla de configuración con su fila inicial, la del catálogo y las de
        // presupuestos.
        migrador.Migrate();
        AfirmarIndiceTipoResultado(cadena);
        Assert.Contains("ConfiguracionNegocio", Tablas(cadena));
        Assert.Single(Filas(cadena, "SELECT Id FROM ConfiguracionNegocio"));
        Assert.Contains("Articulo", Tablas(cadena));
        AfirmarIndiceClaveArticulo(cadena);
        Assert.Contains("Presupuesto", Tablas(cadena));
        Assert.Contains("LineaPresupuesto", Tablas(cadena));
        Assert.Empty(contexto.Database.GetPendingMigrations());
    }

    private static void AfirmarIndiceClaveArticulo(string cadena)
    {
        var indice = Assert.Single(Filas(cadena,
            "SELECT \"unique\" FROM pragma_index_list('Articulo') WHERE name = 'IX_Articulo_CodigoProveedorClave'"));
        Assert.Equal("1", indice[0]);
    }

    [Fact]
    public async Task RegistroEventos_ConsultaYOraculoDeDominio_MismasFechasEnElMismoOrden()
    {
        // Cada escenario: filas tal como quedan en la tabla (texto de Tipo, texto de Resultado, minuto respecto de
        // Instante). Los minutos van fuera de orden a propósito: el orden que vale es el de registro (Id).
        (string Nombre, (string Tipo, string Resultado, int Minuto)[] Filas)[] escenarios =
        [
            ("sin reinicio",
            [
                ("ingreso", "rechazo", 3), ("ingreso", "rechazo", -2), ("salida", "exito", 4),
                ("cambio_contrasena", "rechazo", 5), ("ingreso", "rechazo", 1),
            ]),
            ("con reinicio en el medio",
            [
                ("ingreso", "rechazo", 1), ("ingreso", "rechazo", 2), ("ingreso", "exito", 3),
                ("ingreso", "rechazo", 9), ("definicion", "exito", 4), ("ingreso", "rechazo", -5),
                ("ingreso", "rechazo", 6), ("cambio_contrasena", "exito", 7), ("ingreso", "rechazo", 8),
                ("restablecimiento", "exito", 0), ("ingreso", "rechazo", 10), ("ingreso", "rechazo", -1),
            ]),
            ("textos desconocidos",
            [
                ("ingreso", "rechazo", 1), ("INGRESO", "exito", 2), ("ingreso", "rechazo", 3),
                ("ingreso", "RECHAZO", 4), ("futuro", "exito", 5), ("definicion", "Exito", 6),
                ("ingreso", "desconocido", 7), ("ingreso", "rechazo", 0),
            ]),
            ("bloqueados intercalados",
            [
                ("ingreso", "rechazo", 1), ("ingreso", "bloqueado", 2), ("ingreso", "rechazo", 3),
                ("ingreso", "bloqueado", 4), ("ingreso", "exito", 5), ("ingreso", "bloqueado", 6),
                ("ingreso", "rechazo", 8), ("ingreso", "bloqueado", 9), ("ingreso", "rechazo", 7),
                ("cambio_contrasena", "rechazo", 10), ("ingreso", "bloqueado", 11),
            ]),
        ];

        var tipos = Enum.GetValues<TipoEventoAcceso>().ToDictionary(t => RegistroEventosAccesoEf.TextoDe(t), StringComparer.Ordinal);
        var resultados = Enum.GetValues<ResultadoEventoAcceso>()
            .ToDictionary(r => RegistroEventosAccesoEf.TextoDe(r), StringComparer.Ordinal);

        foreach (var (nombre, filas) in escenarios)
        {
            using var baseTemporal = new BaseTemporal();
            foreach (var (tipo, resultado, minuto) in filas)
            {
                InsertarFila(CadenaDe(baseTemporal), tipo, resultado, Instante.UtcDateTime.AddMinutes(minuto));
            }

            // Las filas con textos desconocidos no son eventos de esta versión: el oráculo no las ve.
            var eventos = filas
                .Where(f => tipos.ContainsKey(f.Tipo) && resultados.ContainsKey(f.Resultado))
                .Select(f => new EventoAcceso(tipos[f.Tipo], resultados[f.Resultado], Instante.UtcDateTime.AddMinutes(f.Minuto)));
            var oraculo = PoliticaBloqueo.RechazosDesdeUltimoReinicio(eventos);
            Assert.NotEmpty(oraculo);

            var consulta = await new RegistroEventosAccesoEf(baseTemporal.CrearContexto())
                .ObtenerRechazosIngresoDesdeUltimoReinicioAsync();

            Assert.Equal(Describir(nombre, oraculo), Describir(nombre, consulta));
        }
    }

    [Fact]
    public async Task Reemplazar_SinCambiosEnElMedio_GuardaHashYFechaNuevos()
    {
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        Assert.True((await CrearDefinir(baseTemporal.CrearContexto(), hasher).EjecutarAsync("clave-valida")).EsExito);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new CredencialAccesoRepository(contexto);
        var anterior = await repositorio.ObtenerAsync();
        Assert.NotNull(anterior);
        var nueva = new CredencialAcceso(hasher.Calcular("otra-clave"), Instante.UtcDateTime.AddDays(4));

        repositorio.Reemplazar(anterior, nueva);
        new RegistroEventosAccesoEf(contexto).Registrar(
            new EventoAcceso(TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Exito, nueva.DefinidaEn));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        var cadena = CadenaDe(baseTemporal);
        var fila = Assert.Single(Filas(cadena, "SELECT Id, Hash, DefinidaEn FROM CredencialAcceso"));
        Assert.Equal(["1", nueva.Hash, nueva.DefinidaEn.ToString("O", CultureInfo.InvariantCulture)], fila);
        Assert.Equal(
            [["definicion", "exito"], ["cambio_contrasena", "exito"]],
            Filas(cadena, "SELECT Tipo, Resultado FROM EventoAcceso ORDER BY Id"));

        // Un segundo reemplazo en el mismo contexto parte de lo recién confirmado.
        var otra = new CredencialAcceso(hasher.Calcular("tercera-clave"), Instante.UtcDateTime.AddDays(5));
        repositorio.Reemplazar(nueva, otra);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        Assert.Equal([otra.Hash], Assert.Single(Filas(cadena, "SELECT Hash FROM CredencialAcceso")));
    }

    [Theory]
    [InlineData("UPDATE CredencialAcceso SET Hash = '" + HasherQueSeAdelanta.HashDeLaOtra + "' WHERE Id = 1", true)]
    [InlineData("DELETE FROM CredencialAcceso WHERE Id = 1", false)]
    public async Task Reemplazar_CredencialCambiadaEnElMedio_Conflicto(string cambioEnElMedio, bool quedaLaOtra)
    {
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        Assert.True((await CrearDefinir(baseTemporal.CrearContexto(), hasher).EjecutarAsync("clave-valida")).EsExito);
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new CredencialAccesoRepository(contexto);
        var anterior = await repositorio.ObtenerAsync();
        Assert.NotNull(anterior);

        // Otra operación (un cambio o un restablecimiento) confirma entre la lectura y la confirmación de esta.
        Ejecutar(cadena, cambioEnElMedio);

        var nueva = new CredencialAcceso(hasher.Calcular("otra-clave"), Instante.UtcDateTime.AddDays(4));
        repositorio.Reemplazar(anterior, nueva);
        new RegistroEventosAccesoEf(contexto).Registrar(
            new EventoAcceso(TipoEventoAcceso.CambioContrasena, ResultadoEventoAcceso.Exito, nueva.DefinidaEn));

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        // Sin la causa de EF: sus Entries llevan el hash anterior y el nuevo, que no pueden llegar a un log.
        Assert.Null(error.InnerException);
        Assert.Empty(contexto.ChangeTracker.Entries());

        // Nada a medias: ni la credencial nueva ni el evento.
        var credenciales = Filas(cadena, "SELECT Hash FROM CredencialAcceso");
        if (quedaLaOtra)
        {
            Assert.Equal([HasherQueSeAdelanta.HashDeLaOtra], Assert.Single(credenciales));
        }
        else
        {
            Assert.Empty(credenciales);
        }

        Assert.Equal([["definicion", "exito"]], Filas(cadena, "SELECT Tipo, Resultado FROM EventoAcceso ORDER BY Id"));

        // El contexto queda usable: lo que se registra después se confirma normalmente.
        new RegistroEventosAccesoEf(contexto).Registrar(
            new EventoAcceso(TipoEventoAcceso.Salida, ResultadoEventoAcceso.Exito, Instante.UtcDateTime));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        Assert.Equal(2, Filas(cadena, "SELECT Id FROM EventoAcceso").Count);
    }

    [Fact]
    public async Task Confirmar_ConflictoDeConcurrencia_SinCausaNiHashesEnLaExcepcion()
    {
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        Assert.True((await CrearDefinir(baseTemporal.CrearContexto(), hasher).EjecutarAsync("clave-valida")).EsExito);
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new CredencialAccesoRepository(contexto);
        var anterior = await repositorio.ObtenerAsync();
        Assert.NotNull(anterior);
        Ejecutar(cadena, "UPDATE CredencialAcceso SET Hash = '" + HasherQueSeAdelanta.HashDeLaOtra + "' WHERE Id = 1");
        var nueva = new CredencialAcceso(hasher.Calcular("otra-clave"), Instante.UtcDateTime.AddDays(4));
        repositorio.Reemplazar(anterior, nueva);

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        // Mensaje genérico y sin excepción interna: nada de lo que se loguee con ella lleva un hash.
        Assert.Equal("Los datos a guardar cambiaron después de leerlos.", error.Message);
        Assert.Equal(MotivoConflicto.Concurrencia, error.Motivo);
        Assert.Null(error.InnerException);
        var completa = error.ToString();
        Assert.DoesNotContain(anterior.Hash, completa, StringComparison.Ordinal);
        Assert.DoesNotContain(nueva.Hash, completa, StringComparison.Ordinal);
        Assert.DoesNotContain(HasherQueSeAdelanta.HashDeLaOtra, completa, StringComparison.Ordinal);
    }

    [Fact]
    public void Reemplazar_MismaInstancia_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new CredencialAccesoRepository(contexto);
        var credencial = new CredencialAcceso(HasherQueSeAdelanta.HashDeLaOtra, Instante.UtcDateTime);

        // La misma instancia ya modificada haría que el WHERE compare contra el hash nuevo: conflicto seguro.
        var error = Assert.Throws<ArgumentException>(() => repositorio.Reemplazar(credencial, credencial));

        Assert.Equal("nueva", error.ParamName);
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Reemplazar_ContextoConVersionVieja_ComparaContraElHashReleido()
    {
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        var cadena = CadenaDe(baseTemporal);
        // El contexto queda siguiendo la fila con el hash de la definición.
        var contexto = baseTemporal.CrearContexto();
        Assert.True((await CrearDefinir(contexto, hasher).EjecutarAsync("clave-valida")).EsExito);
        Assert.NotNull(contexto.CredencialesAcceso.Local.FindEntry(CredencialAccesoRepository.IdUnico));

        // Otro proceso cambia la fila; este la relee con el hash actual y la reemplaza a partir de ella.
        Ejecutar(cadena, "UPDATE CredencialAcceso SET Hash = '" + HasherQueSeAdelanta.HashDeLaOtra + "' WHERE Id = 1");
        var repositorio = new CredencialAccesoRepository(contexto);
        var releida = await repositorio.ObtenerAsync();
        Assert.NotNull(releida);
        Assert.Equal(HasherQueSeAdelanta.HashDeLaOtra, releida.Hash);
        var nueva = new CredencialAcceso(hasher.Calcular("otra-clave"), Instante.UtcDateTime.AddDays(4));

        repositorio.Reemplazar(releida, nueva);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal([nueva.Hash], Assert.Single(Filas(cadena, "SELECT Hash FROM CredencialAcceso")));
    }

    [Fact]
    public void Reemplazar_ConArgumentoNulo_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var repositorio = new CredencialAccesoRepository(baseTemporal.CrearContexto());
        var credencial = new CredencialAcceso(HasherQueSeAdelanta.HashDeLaOtra, Instante.UtcDateTime);

        Assert.Throws<ArgumentNullException>(() => repositorio.Reemplazar(null!, credencial));
        Assert.Throws<ArgumentNullException>(() => repositorio.Reemplazar(credencial, null!));
    }

    [Fact]
    public async Task Eliminar_ConElEvento_BorraSoloLaCredencialAlConfirmar()
    {
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        Assert.True((await CrearDefinir(baseTemporal.CrearContexto(), hasher).EjecutarAsync("clave-valida")).EsExito);
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();

        new CredencialAccesoRepository(contexto).Eliminar();
        new RegistroEventosAccesoEf(contexto).Registrar(
            new EventoAcceso(TipoEventoAcceso.Restablecimiento, ResultadoEventoAcceso.Exito, Instante.UtcDateTime));

        // Sin ExecuteDelete: hasta confirmar, la fila sigue en la base.
        Assert.Single(Filas(cadena, "SELECT Id FROM CredencialAcceso"));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Empty(Filas(cadena, "SELECT Id FROM CredencialAcceso"));
        Assert.Equal(
            [["definicion", "exito"], ["restablecimiento", "exito"]],
            Filas(cadena, "SELECT Tipo, Resultado FROM EventoAcceso ORDER BY Id"));

        // Sin credencial no registra nada: repetirlo solo agrega el evento.
        var otro = baseTemporal.CrearContexto();
        new CredencialAccesoRepository(otro).Eliminar();
        Assert.Empty(otro.ChangeTracker.Entries());
        new RegistroEventosAccesoEf(otro).Registrar(
            new EventoAcceso(TipoEventoAcceso.Restablecimiento, ResultadoEventoAcceso.Exito, Instante.UtcDateTime));
        await new UnidadDeTrabajoEf(otro).ConfirmarAsync();
        Assert.Equal(3, Filas(cadena, "SELECT Id FROM EventoAcceso").Count);
    }

    [Fact]
    public async Task Eliminar_ContextoQueYaSigueLaFila_LaReutilizaYLaBorra()
    {
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        var contexto = baseTemporal.CrearContexto();
        Assert.True((await CrearDefinir(contexto, hasher).EjecutarAsync("clave-valida")).EsExito);
        Assert.NotNull(contexto.CredencialesAcceso.Local.FindEntry(CredencialAccesoRepository.IdUnico));

        new CredencialAccesoRepository(contexto).Eliminar();
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Empty(Filas(CadenaDe(baseTemporal), "SELECT Id FROM CredencialAcceso"));
    }

    [Fact]
    public async Task Eliminar_CredencialCambiadaEnElMedio_ConflictoSinBorrarNiRegistrar()
    {
        using var baseTemporal = new BaseTemporal();
        var hasher = new HasherContrasenaPbkdf2();
        Assert.True((await CrearDefinir(baseTemporal.CrearContexto(), hasher).EjecutarAsync("clave-valida")).EsExito);
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();

        new CredencialAccesoRepository(contexto).Eliminar();
        new RegistroEventosAccesoEf(contexto).Registrar(
            new EventoAcceso(TipoEventoAcceso.Restablecimiento, ResultadoEventoAcceso.Exito, Instante.UtcDateTime));
        // Un cambio de contraseña confirma entre la lectura y la confirmación del restablecimiento.
        Ejecutar(cadena, "UPDATE CredencialAcceso SET Hash = '" + HasherQueSeAdelanta.HashDeLaOtra + "' WHERE Id = 1");

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Null(error.InnerException);
        Assert.Equal([HasherQueSeAdelanta.HashDeLaOtra], Assert.Single(Filas(cadena, "SELECT Hash FROM CredencialAcceso")));
        Assert.Equal([["definicion", "exito"]], Filas(cadena, "SELECT Tipo, Resultado FROM EventoAcceso ORDER BY Id"));
    }

    [Fact]
    public void Modelo_SinCambiosPendientes_TrasLasMigraciones()
    {
        // BaseTemporal arranca la infraestructura como la Api (InicializarBaseDeDatos → Migrate): si el modelo tuviera
        // cambios sin migración, EF lanzaría ahí y la Api no arrancaría.
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();

        Assert.False(contexto.Database.HasPendingModelChanges());
        Assert.Empty(contexto.Database.GetPendingMigrations());

        // El hash de la credencial es el token de concurrencia del reemplazo.
        var hash = contexto.Model.FindEntityType(typeof(CredencialAccesoFila))?.FindProperty(nameof(CredencialAccesoFila.Hash));
        Assert.NotNull(hash);
        Assert.True(hash.IsConcurrencyToken);

        // La versión del artículo es el token de la modificación y del cambio de estado (D-08); Activo también, así una
        // fila inactiva no recibe datos nuevos (D-22). Ninguna de las dos lleva DEFAULT en el modelo (D-21).
        var articulo = contexto.Model.FindEntityType(typeof(ArticuloFila));
        Assert.NotNull(articulo);
        foreach (var nombre in new[] { nameof(ArticuloFila.Version), nameof(ArticuloFila.Activo) })
        {
            var propiedad = articulo.FindProperty(nombre);
            Assert.NotNull(propiedad);
            Assert.True(propiedad.IsConcurrencyToken, nombre);
            Assert.Null(propiedad.GetDefaultValueSql());
            Assert.False(propiedad.TryGetDefaultValue(out _), nombre);
        }
    }

    [Fact]
    public async Task UnidadDeTrabajo_ClavePrimariaYCheck_MotivoUnicidad()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        Ejecutar(cadena, "INSERT INTO CredencialAcceso (Id, Hash, DefinidaEn) VALUES (1, '"
            + HasherQueSeAdelanta.HashDeLaOtra + "', '2026-10-30T14:00:00.0000000Z')");

        // Clave primaria duplicada (1555): motivo Unicidad (D-18).
        var duplicada = baseTemporal.CrearContexto();
        new CredencialAccesoRepository(duplicada).Agregar(
            new CredencialAcceso(new HasherContrasenaPbkdf2().Calcular("clave-valida"), Instante.UtcDateTime));
        var porClave = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(duplicada).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.Unicidad, porClave.Motivo);
        Assert.Null(porClave.InnerException);

        // El CHECK de credencial única (Id = 2, 275): también Unicidad.
        var segunda = baseTemporal.CrearContexto();
        segunda.CredencialesAcceso.Add(new CredencialAccesoFila
        {
            Id = 2,
            Hash = HasherQueSeAdelanta.HashDeLaOtra,
            DefinidaEn = Instante.UtcDateTime,
        });
        var porCheck = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(segunda).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.Unicidad, porCheck.Motivo);
        Assert.Null(porCheck.InnerException);
        Assert.Equal("Los datos a guardar chocan con datos existentes.", porCheck.Message);
    }

    private static string Describir(string escenario, IEnumerable<DateTime> fechas) =>
        $"{escenario}: {string.Join(", ", fechas.Select(f => f.ToString("O", CultureInfo.InvariantCulture)))}";

    private static void InsertarFila(string cadena, string tipo, string resultado, DateTime fecha)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "INSERT INTO EventoAcceso (Tipo, Resultado, FechaHora) VALUES ($tipo, $resultado, $fecha)";
        comando.Parameters.AddWithValue("$tipo", tipo);
        comando.Parameters.AddWithValue("$resultado", resultado);
        comando.Parameters.AddWithValue("$fecha", fecha.ToString("O", CultureInfo.InvariantCulture));
        comando.ExecuteNonQuery();
    }

    /// <summary>
    /// <c>EXPLAIN QUERY PLAN</c> sobre el SQL que genera la consulta real: la principal y la subconsulta del último
    /// reinicio buscan por <c>IX_EventoAcceso_Tipo_Resultado</c>, sin recorrer la tabla ni ordenar aparte.
    /// </summary>
    private static void AfirmarPlanUsaIndiceTipoResultado(OpticaDbContext contexto, string cadena)
    {
        var sql = new RegistroEventosAccesoEf(contexto).ConsultaRechazosIngresoDesdeUltimoReinicio().ToQueryString();
        // Columnas: id, parent, notused, detail.
        var plan = Filas(cadena, "EXPLAIN QUERY PLAN " + sql);
        var resumen = string.Join(" | ", plan.Select(f => $"{f[0]}<{f[1]}: {f[3]}"));

        Assert.DoesNotContain(plan, f => f[3].StartsWith("SCAN", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, f => f[3].Contains("TEMP B-TREE", StringComparison.Ordinal));
        var busquedas = plan.Where(f => f[3].StartsWith("SEARCH", StringComparison.Ordinal)).ToList();
        Assert.All(busquedas, f => Assert.Contains("IX_EventoAcceso_Tipo_Resultado", f[3], StringComparison.Ordinal));

        // La principal busca en el nivel de arriba; la subconsulta, debajo de su nodo SUBQUERY.
        Assert.True(busquedas.Any(f => f[1] == "0"), $"La consulta principal no busca por el índice: {resumen}");
        var subconsultas = plan.Where(f => f[3].Contains("SUBQUERY", StringComparison.Ordinal)).Select(f => f[0]).ToHashSet();
        Assert.True(subconsultas.Count > 0, $"No hay subconsulta en el plan: {resumen}");
        Assert.True(
            busquedas.Any(f => DesciendeDe(plan, f, subconsultas)),
            $"La subconsulta no busca por el índice: {resumen}");
    }

    private static bool DesciendeDe(List<string[]> plan, string[] fila, HashSet<string> ancestros)
    {
        var padre = fila[1];
        while (padre != "0")
        {
            if (ancestros.Contains(padre))
            {
                return true;
            }

            padre = plan.Single(f => f[0] == padre)[1];
        }

        return false;
    }

    private static async Task Registrar(
        OpticaDbContext contexto, params (TipoEventoAcceso Tipo, ResultadoEventoAcceso Resultado, DateTime Fecha)[] eventos)
    {
        var registro = new RegistroEventosAccesoEf(contexto);
        foreach (var (tipo, resultado, fecha) in eventos)
        {
            registro.Registrar(new EventoAcceso(tipo, resultado, fecha));
        }

        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    private static List<string> Indices(string cadena) =>
        [.. Filas(cadena, "SELECT name FROM pragma_index_list('EventoAcceso')").Select(f => f[0])];

    private static List<string> Tablas(string cadena) =>
        [.. Filas(cadena, "SELECT name FROM sqlite_master WHERE type = 'table'").Select(f => f[0])];

    private static void AfirmarIndiceTipoResultado(string cadena)
    {
        var indice = Assert.Single(Filas(cadena,
            "SELECT \"unique\" FROM pragma_index_list('EventoAcceso') WHERE name = 'IX_EventoAcceso_Tipo_Resultado'"));
        Assert.Equal("0", indice[0]);
        Assert.Equal(
            ["Tipo", "Resultado"],
            Filas(cadena, "SELECT name FROM pragma_index_info('IX_EventoAcceso_Tipo_Resultado') ORDER BY seqno")
                .Select(f => f[0]));
    }

    private static DefinirContrasena CrearDefinir(OpticaDbContext contexto, IHasherContrasena hasher) =>
        new(
            new CredencialAccesoRepository(contexto),
            new RegistroEventosAccesoEf(contexto),
            hasher,
            new UnidadDeTrabajoEf(contexto),
            new Reloj());

    private static string CadenaDe(BaseTemporal baseTemporal) =>
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion;

    private static List<string[]> Filas(string cadena, string consulta)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = consulta;
        using var lector = comando.ExecuteReader();
        var filas = new List<string[]>();
        while (lector.Read())
        {
            var fila = new string[lector.FieldCount];
            for (var i = 0; i < lector.FieldCount; i++)
            {
                fila[i] = Convert.ToString(lector.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
            }

            filas.Add(fila);
        }

        return filas;
    }

    private static void Ejecutar(string cadena, string sql)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    private static bool Contiene(byte[] datos, byte[] buscado) =>
        datos.AsSpan().IndexOf(buscado) >= 0;

    private sealed class Reloj : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Instante;
    }

    private sealed class FallaForzadaException() : Exception("Falla forzada al confirmar.");

    /// <summary>Hace fallar la confirmación justo después de escribir las filas y antes del commit.</summary>
    private sealed class FallaDespuesDeGuardar : SaveChangesInterceptor
    {
        public int FilasGuardadas { get; private set; }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            FilasGuardadas = result;
            throw new FallaForzadaException();
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            FilasGuardadas = result;
            throw new FallaForzadaException();
        }
    }

    /// <summary>Al calcular el hash, simula que otra definición ya grabó la credencial.</summary>
    private sealed class HasherQueSeAdelanta(string cadena) : IHasherContrasena
    {
        public const string HashDeLaOtra = "PBKDF2-SHA256$600000$b3RyYQ==$b3RyYQ==";

        public string Calcular(string contrasena)
        {
            using var conexion = new SqliteConnection(cadena);
            conexion.Open();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "INSERT INTO CredencialAcceso (Id, Hash, DefinidaEn) VALUES (1, $hash, $fecha)";
            comando.Parameters.AddWithValue("$hash", HashDeLaOtra);
            comando.Parameters.AddWithValue("$fecha", "2026-10-30T14:00:00.0000000Z");
            comando.ExecuteNonQuery();
            return "PBKDF2-SHA256$600000$ZXN0YQ==$ZXN0YQ==";
        }

        public bool Verificar(string contrasena, string hash) => false;
    }
}
