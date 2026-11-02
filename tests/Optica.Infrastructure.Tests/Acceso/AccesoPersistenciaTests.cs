using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        SqliteConnection.ClearAllPools();
        foreach (var archivo in Directory.GetFiles(baseTemporal.Directorio))
        {
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
            Hash = "PBKDF2-SHA256$600000$b3RyYQ==$b3RyYQ==",
            DefinidaEn = Instante.UtcDateTime,
        });
        new RegistroEventosAccesoEf(contexto).Registrar(
            new EventoAcceso(TipoEventoAcceso.Definicion, ResultadoEventoAcceso.Exito, Instante.UtcDateTime));

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Equal(275, Assert.IsType<SqliteException>(error.InnerException?.InnerException).SqliteExtendedErrorCode);
        Assert.Empty(contexto.ChangeTracker.Entries());
        var cadena = CadenaDe(baseTemporal);
        Assert.Empty(Filas(cadena, "SELECT Id FROM CredencialAcceso"));
        Assert.Empty(Filas(cadena, "SELECT Id FROM EventoAcceso"));
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
