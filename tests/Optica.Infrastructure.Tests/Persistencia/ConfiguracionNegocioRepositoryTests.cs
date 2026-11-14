using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Optica.Domain.Configuracion;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

public class ConfiguracionNegocioRepositoryTests
{
    private const int SqliteRestriccionCheck = 275; // SQLITE_CONSTRAINT_CHECK

    /// <summary>La última migración de FEAT-001b: una base instalada antes de FEAT-001c queda acá.</summary>
    private const string MigracionFeat001b = "20261103143513_TokenConcurrenciaCredencial";

    private const string SelectFila =
        "SELECT Id, AlicuotaIvaCentesimas, CondicionFiscal, MultiploRedondeoCentavos, " +
        "IFNULL(TopeIdentificacionCentavos, 'NULL') FROM ConfiguracionNegocio";

    private const string SelectPuntoVenta = "SELECT IFNULL(PuntoVenta, 'NULL') FROM ConfiguracionNegocio";

    /// <summary>La migración de FEAT-007 que agrega el punto de venta (D-07) y la anterior, buscadas por nombre (D-29).</summary>
    private const string SufijoPuntoVenta = "_ConfiguracionPuntoVenta";

    private const string SufijoAnteriorAPuntoVenta = "_CierrePresupuestos";

    [Fact]
    public async Task BaseNueva_TieneFilaConValoresIniciales()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        // La fila inicial, tal como queda en la base: centésimos enteros y el tope sin valor (FR-07, FR-08).
        var fila = Assert.Single(Filas(cadena, SelectFila));
        Assert.Equal(["1", "2100", "responsable_inscripto", "1", "NULL"], fila);
        Assert.Equal("INTEGER", TipoColumna(cadena, "AlicuotaIvaCentesimas"));
        Assert.Equal("INTEGER", TipoColumna(cadena, "MultiploRedondeoCentavos"));
        Assert.Equal("INTEGER", TipoColumna(cadena, "TopeIdentificacionCentavos"));
        Assert.Equal("TEXT", TipoColumna(cadena, "CondicionFiscal"));

        // Y leída por el repositorio: 21 %, Responsable Inscripto, 0,01 y sin tope (AC-06, AC-07).
        var configuracion = await new ConfiguracionNegocioRepository(baseTemporal.CrearContexto()).ObtenerAsync();
        Assert.Equal(21m, configuracion.AlicuotaIva);
        Assert.Equal(CondicionFiscal.ResponsableInscripto, configuracion.CondicionFiscal);
        Assert.Equal(0.01m, configuracion.MultiploRedondeo);
        Assert.Null(configuracion.TopeIdentificacion);
        Assert.True(configuracion.FaltaTope);
    }

    [Fact]
    public async Task BaseDeFeat001b_AlMigrar_RecibeLaFila()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            using (var servicios = BaseTemporal.CrearServicios(directorio))
            {
                var fabrica = servicios.GetRequiredService<FabricaConexion>();
                fabrica.PrepararDirectorio();
                using var alcance = servicios.CreateScope();
                var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
                var migrador = contexto.GetService<IMigrator>();

                // Una base como la que dejó FEAT-001b: sin la tabla de configuración y con una credencial cargada.
                migrador.Migrate(MigracionFeat001b);
                Assert.Equal(MigracionFeat001b, contexto.Database.GetAppliedMigrations().Last());
                Assert.Empty(Filas(fabrica.CadenaConexion,
                    "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'ConfiguracionNegocio'"));
                Ejecutar(fabrica.CadenaConexion,
                    "INSERT INTO CredencialAcceso (Id, Hash, DefinidaEn) VALUES (1, 'hash', '2026-11-03T12:00:00.0000000Z')");

                // Migrar hasta la última, como en el arranque, le agrega la fila con los valores iniciales.
                migrador.Migrate();
                Assert.Empty(contexto.Database.GetPendingMigrations());
                Assert.Equal(["1", "2100", "responsable_inscripto", "1", "NULL"],
                    Assert.Single(Filas(fabrica.CadenaConexion, SelectFila)));
                Assert.Single(Filas(fabrica.CadenaConexion, "SELECT Id FROM CredencialAcceso"));

                var configuracion = await new ConfiguracionNegocioRepository(contexto).ObtenerAsync();
                Assert.Equal(21m, configuracion.AlicuotaIva);
                Assert.True(configuracion.FaltaTope);
            }
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task Reemplazar_YConfirmar_OtroContextoLeeElValorNuevo()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var otroContexto = baseTemporal.CrearContexto();
        var otroRepositorio = new ConfiguracionNegocioRepository(otroContexto);

        // El otro alcance ya leyó la configuración inicial antes de la grabación.
        Assert.Equal(21m, (await otroRepositorio.ObtenerAsync()).AlicuotaIva);

        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ConfiguracionNegocioRepository(contexto);
        var unidad = new UnidadDeTrabajoEf(contexto);
        repositorio.Reemplazar(new ConfiguracionNegocio(10.5m, CondicionFiscal.Monotributo, 0.05m, 1000.25m, puntoVenta: null));

        // Sin confirmar, nada llega a la base.
        Assert.Equal(["1", "2100", "responsable_inscripto", "1", "NULL"], Assert.Single(Filas(cadena, SelectFila)));

        await unidad.ConfirmarAsync();

        Assert.Equal(["1", "1050", "monotributo", "5", "100025"], Assert.Single(Filas(cadena, SelectFila)));

        // El otro alcance vuelve a leer y ve lo grabado, sin reiniciar nada (FR-06, AC-01).
        var leida = await otroRepositorio.ObtenerAsync();
        Assert.Equal(10.5m, leida.AlicuotaIva);
        Assert.Equal(CondicionFiscal.Monotributo, leida.CondicionFiscal);
        Assert.Equal(0.05m, leida.MultiploRedondeo);
        Assert.Equal(1000.25m, leida.TopeIdentificacion);

        // Un segundo reemplazo en el mismo contexto reutiliza la entrada seguida y la última grabación gana (D-03).
        repositorio.Reemplazar(new ConfiguracionNegocio(27m, CondicionFiscal.ResponsableInscripto, 10m, 99999.99m, puntoVenta: null));
        await unidad.ConfirmarAsync();

        Assert.Equal(["1", "2700", "responsable_inscripto", "1000", "9999999"], Assert.Single(Filas(cadena, SelectFila)));
        var ultima = await otroRepositorio.ObtenerAsync();
        Assert.Equal(27m, ultima.AlicuotaIva);
        Assert.Equal(CondicionFiscal.ResponsableInscripto, ultima.CondicionFiscal);
        Assert.Equal(10m, ultima.MultiploRedondeo);
        Assert.Equal(99999.99m, ultima.TopeIdentificacion);
    }

    [Fact]
    public async Task Reemplazar_ConEntradaLocalDesactualizada_LaUltimaGrabacionGana()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ConfiguracionNegocioRepository(contexto);
        var unidad = new UnidadDeTrabajoEf(contexto);
        var original = new ConfiguracionNegocio(10.5m, CondicionFiscal.Monotributo, 0.05m, 1000.25m, puntoVenta: null);

        // El primer contexto graba y se queda con la entrada seguida.
        repositorio.Reemplazar(original);
        await unidad.ConfirmarAsync();

        // Otro alcance graba otros valores: la entrada del primero queda desactualizada.
        var otroContexto = baseTemporal.CrearContexto();
        new ConfiguracionNegocioRepository(otroContexto)
            .Reemplazar(new ConfiguracionNegocio(27m, CondicionFiscal.ResponsableInscripto, 10m, null, puntoVenta: null));
        await new UnidadDeTrabajoEf(otroContexto).ConfirmarAsync();
        Assert.Equal(["1", "2700", "responsable_inscripto", "1000", "NULL"], Assert.Single(Filas(cadena, SelectFila)));

        // El primero vuelve a grabar los valores que su entrada ya tenía: igual tiene que llegar a la base (D-03).
        repositorio.Reemplazar(new ConfiguracionNegocio(10.5m, CondicionFiscal.Monotributo, 0.05m, 1000.25m, puntoVenta: null));
        await unidad.ConfirmarAsync();

        Assert.Equal(["1", "1050", "monotributo", "5", "100025"], Assert.Single(Filas(cadena, SelectFila)));
    }

    [Theory]
    [InlineData("AlicuotaIva")]
    [InlineData("MultiploRedondeo")]
    [InlineData("TopeIdentificacion")]
    public async Task Guardar_ValorConMasDeDosDecimales_LanzaSinTruncar(string propiedad)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();

        // La fila se modifica sin pasar por el dominio: el conversor es la última barrera antes de la base.
        var fila = await contexto.ConfiguracionesNegocio.SingleAsync();
        contexto.Entry(fila).Property(propiedad).CurrentValue = 21.005m;

        var error = await Record.ExceptionAsync(() => contexto.SaveChangesAsync());

        Assert.NotNull(error);
        var causa = error is ArgumentException or InvalidOperationException ? error : error.InnerException;
        Assert.True(causa is ArgumentException or InvalidOperationException, $"Excepción inesperada: {error}");
        Assert.DoesNotContain("21.005", causa!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("21,005", causa.Message, StringComparison.Ordinal);
        Assert.Equal(["1", "2100", "responsable_inscripto", "1", "NULL"], Assert.Single(Filas(cadena, SelectFila)));
    }

    [Fact]
    public async Task Reemplazar_TopeNull_GuardaNull()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ConfiguracionNegocioRepository(contexto);
        var unidad = new UnidadDeTrabajoEf(contexto);

        repositorio.Reemplazar(new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, 500m, puntoVenta: null));
        await unidad.ConfirmarAsync();
        Assert.Equal("50000", Assert.Single(Filas(cadena, SelectFila))[4]);

        // Un tope ya cargado se puede vaciar: queda NULL, no 0 (D-02).
        repositorio.Reemplazar(new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null));
        await unidad.ConfirmarAsync();

        Assert.Equal("NULL", Assert.Single(Filas(cadena, SelectFila))[4]);
        var leida = await new ConfiguracionNegocioRepository(baseTemporal.CrearContexto()).ObtenerAsync();
        Assert.Null(leida.TopeIdentificacion);
        Assert.True(leida.FaltaTope);
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(-1)]
    public void Insertar_AlicuotaFueraDeRango_RechazadoPorCheck(int centesimas)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        Ejecutar(cadena, "DELETE FROM ConfiguracionNegocio");

        var error = Assert.Throws<SqliteException>(() => Ejecutar(cadena,
            "INSERT INTO ConfiguracionNegocio (Id, AlicuotaIvaCentesimas, CondicionFiscal, MultiploRedondeoCentavos, " +
            $"TopeIdentificacionCentavos) VALUES (1, {centesimas}, 'responsable_inscripto', 1, NULL)"));

        AfirmarCheck(error, "CK_ConfiguracionNegocio_Alicuota");

        // Los extremos del rango sí entran.
        Ejecutar(cadena,
            "INSERT INTO ConfiguracionNegocio (Id, AlicuotaIvaCentesimas, CondicionFiscal, MultiploRedondeoCentavos, " +
            "TopeIdentificacionCentavos) VALUES (1, 10000, 'responsable_inscripto', 1, NULL)");
        Ejecutar(cadena, "UPDATE ConfiguracionNegocio SET AlicuotaIvaCentesimas = 0");
    }

    [Theory]
    [InlineData("otro")]
    [InlineData("Monotributo")]
    [InlineData("")]
    public void Insertar_CondicionDesconocida_RechazadoPorCheck(string condicion)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        Ejecutar(cadena, "DELETE FROM ConfiguracionNegocio");

        var error = Assert.Throws<SqliteException>(() => Ejecutar(cadena,
            "INSERT INTO ConfiguracionNegocio (Id, AlicuotaIvaCentesimas, CondicionFiscal, MultiploRedondeoCentavos, " +
            $"TopeIdentificacionCentavos) VALUES (1, 2100, '{condicion}', 1, NULL)"));

        AfirmarCheck(error, "CK_ConfiguracionNegocio_Condicion");
    }

    [Fact]
    public void Insertar_SegundaFila_RechazadoPorCheck()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        var error = Assert.Throws<SqliteException>(() => Ejecutar(cadena,
            "INSERT INTO ConfiguracionNegocio (Id, AlicuotaIvaCentesimas, CondicionFiscal, MultiploRedondeoCentavos, " +
            "TopeIdentificacionCentavos) VALUES (2, 2100, 'responsable_inscripto', 1, NULL)"));

        AfirmarCheck(error, "CK_ConfiguracionNegocio_Id");
        Assert.Single(Filas(cadena, SelectFila));
    }

    [Theory]
    [InlineData("MultiploRedondeoCentavos = 0", "CK_ConfiguracionNegocio_Multiplo")]
    [InlineData("TopeIdentificacionCentavos = 0", "CK_ConfiguracionNegocio_Tope")]
    [InlineData("TopeIdentificacionCentavos = -5", "CK_ConfiguracionNegocio_Tope")]
    public void Actualizar_MultiploOTopeFueraDeRango_RechazadoPorCheck(string asignacion, string restriccion)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        var error = Assert.Throws<SqliteException>(() =>
            Ejecutar(cadena, $"UPDATE ConfiguracionNegocio SET {asignacion}"));

        AfirmarCheck(error, restriccion);
        Assert.Equal(["1", "2100", "responsable_inscripto", "1", "NULL"], Assert.Single(Filas(cadena, SelectFila)));
    }

    [Fact]
    public async Task Obtener_SinFila_LanzaError()
    {
        using var baseTemporal = new BaseTemporal();
        Ejecutar(CadenaDe(baseTemporal), "DELETE FROM ConfiguracionNegocio");
        var repositorio = new ConfiguracionNegocioRepository(baseTemporal.CrearContexto());

        // Estado corrupto: no se recrea en silencio (D-04).
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.ObtenerAsync());
        Assert.Equal("No hay configuración de negocio.", error.Message);
    }

    [Fact]
    public void Reemplazar_SinFila_LanzaError()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        Ejecutar(cadena, "DELETE FROM ConfiguracionNegocio");
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ConfiguracionNegocioRepository(contexto);

        var error = Assert.Throws<InvalidOperationException>(() => repositorio.Reemplazar(
            new ConfiguracionNegocio(10.5m, CondicionFiscal.Monotributo, 1m, null, puntoVenta: null)));

        Assert.Equal("No hay configuración de negocio.", error.Message);
        Assert.False(contexto.ChangeTracker.HasChanges());
        Assert.Empty(Filas(cadena, SelectFila));
    }

    [Theory]
    [InlineData("AlicuotaIvaCentesimas = 10001")]
    [InlineData("CondicionFiscal = 'otro'")]
    [InlineData("MultiploRedondeoCentavos = 0")]
    [InlineData("TopeIdentificacionCentavos = 0")]
    public async Task Obtener_FilaQueEludioLosCheck_LanzaErrorDeEstadoCorrupto(string asignacion)
    {
        using var baseTemporal = new BaseTemporal();
        // Una fila editada a mano con los CHECK desactivados: la entidad no puede representarla. La conexión es propia
        // y sin grupo, así el pragma no queda activo en una conexión que después reutilice el contexto.
        var sinGrupo = new SqliteConnectionStringBuilder(CadenaDe(baseTemporal)) { Pooling = false }.ToString();
        Ejecutar(sinGrupo, $"PRAGMA ignore_check_constraints = ON; UPDATE ConfiguracionNegocio SET {asignacion};");
        var repositorio = new ConfiguracionNegocioRepository(baseTemporal.CrearContexto());

        // Se informa como estado corrupto (D-04), no como el ArgumentException de una entrada inválida.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.ObtenerAsync());
        Assert.Equal("La configuración de negocio guardada no es válida.", error.Message);
    }

    // ---- FEAT-007: punto de venta (D-07, D-27, D-33) y alícuotas de ARCA (D-06) ----

    [Fact]
    public async Task BaseNueva_PuntoVentaSinValor()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        Assert.Equal(["NULL"], Assert.Single(Filas(cadena, SelectPuntoVenta)));
        Assert.Equal("INTEGER", TipoColumna(cadena, "PuntoVenta"));
        var configuracion = await new ConfiguracionNegocioRepository(baseTemporal.CrearContexto()).ObtenerAsync();
        Assert.Null(configuracion.PuntoVenta);
        Assert.True(configuracion.FaltaPuntoVenta);
    }

    [Fact]
    public async Task Reemplazar_PuntoVenta_SeGuardaYSeLee()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();

        new ConfiguracionNegocioRepository(contexto).Reemplazar(
            new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: 3));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal(["3"], Assert.Single(Filas(cadena, SelectPuntoVenta)));
        var leida = await new ConfiguracionNegocioRepository(baseTemporal.CrearContexto()).ObtenerAsync();
        Assert.Equal(3, leida.PuntoVenta);
    }

    [Fact]
    public void Reemplazar_AlicuotaNoAdmitida_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ConfiguracionNegocioRepository(contexto);
        // Una configuración de lectura (D-06) con una alícuota que ARCA no acepta: la escritura la frena igual (D-33).
        var leidaDeUnaBaseAnterior =
            new ConfiguracionNegocio(19m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

        var error = Assert.Throws<ArgumentException>(() => repositorio.Reemplazar(leidaDeUnaBaseAnterior));

        Assert.DoesNotContain("19", error.Message, StringComparison.Ordinal);
        Assert.False(contexto.ChangeTracker.HasChanges());
        Assert.Equal(["1", "2100", "responsable_inscripto", "1", "NULL"], Assert.Single(Filas(cadena, SelectFila)));

        // La regla de las 6 se controla antes que la fila faltante.
        Ejecutar(cadena, "DELETE FROM ConfiguracionNegocio");
        Assert.Throws<ArgumentException>(() => new ConfiguracionNegocioRepository(baseTemporal.CrearContexto())
            .Reemplazar(leidaDeUnaBaseAnterior));
    }

    [Fact]
    public async Task Obtener_AlicuotaGuardada19_SeLeeNoAdmitida()
    {
        using var baseTemporal = new BaseTemporal();
        // Una base con la alícuota 19 grabada antes de FR-26: cumple el CHECK de 0 a 100.
        Ejecutar(CadenaDe(baseTemporal), "UPDATE ConfiguracionNegocio SET AlicuotaIvaCentesimas = 1900");

        var configuracion = await new ConfiguracionNegocioRepository(baseTemporal.CrearContexto()).ObtenerAsync();

        // D-06: se lee sin error; ni el GET ni el recálculo responden 500.
        Assert.Equal(19m, configuracion.AlicuotaIva);
        Assert.False(configuracion.AlicuotaAdmitidaPorArca);
    }

    [Fact]
    public async Task Obtener_PuntoVentaGuardadoCero_ErrorDeEstadoCorrupto()
    {
        using var baseTemporal = new BaseTemporal();
        // Editada a mano con los CHECK desactivados, con una conexión propia y sin grupo.
        var sinGrupo = new SqliteConnectionStringBuilder(CadenaDe(baseTemporal)) { Pooling = false }.ToString();
        Ejecutar(sinGrupo, "PRAGMA ignore_check_constraints = ON; UPDATE ConfiguracionNegocio SET PuntoVenta = 0;");
        var repositorio = new ConfiguracionNegocioRepository(baseTemporal.CrearContexto());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.ObtenerAsync());

        Assert.Equal("La configuración de negocio guardada no es válida.", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100000)]
    public void Migracion_PuntoVenta_CheckRechazaCero(int puntoVenta)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        var error = Assert.Throws<SqliteException>(() =>
            Ejecutar(cadena, $"UPDATE ConfiguracionNegocio SET PuntoVenta = {puntoVenta}"));

        AfirmarCheck(error, "CK_ConfiguracionNegocio_PuntoVenta");
        Assert.Equal(["NULL"], Assert.Single(Filas(cadena, SelectPuntoVenta)));

        // Los extremos y la falta de valor sí entran.
        Ejecutar(cadena, "UPDATE ConfiguracionNegocio SET PuntoVenta = 1");
        Ejecutar(cadena, "UPDATE ConfiguracionNegocio SET PuntoVenta = 99999");
        Ejecutar(cadena, "UPDATE ConfiguracionNegocio SET PuntoVenta = NULL");
    }

    [Fact]
    public void Migracion_ConfiguracionPuntoVenta_AgregaLaColumnaSinReconstruirYRevierte()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var migrador = contexto.GetService<IMigrator>();
        var migraciones = contexto.Database.GetMigrations().ToList();
        var puntoVenta = Assert.Single(migraciones, m => m.EndsWith(SufijoPuntoVenta, StringComparison.Ordinal));
        var anterior = Assert.Single(migraciones, m => m.EndsWith(SufijoAnteriorAPuntoVenta, StringComparison.Ordinal));
        Assert.Equal(migraciones.IndexOf(anterior) + 1, migraciones.IndexOf(puntoVenta));

        // ADR-012: SQL escrito a mano; ni AddColumn ni AddCheckConstraint, que en SQLite reconstruyen la tabla.
        var ensamblado = contexto.GetService<IMigrationsAssembly>();
        var migracion = ensamblado.CreateMigration(ensamblado.Migrations[puntoVenta], contexto.Database.ProviderName!);
        Assert.All(migracion.UpOperations, operacion => Assert.IsType<SqlOperation>(operacion));
        Assert.All(migracion.DownOperations, operacion => Assert.IsType<SqlOperation>(operacion));

        // Una base anterior con la configuración ya cargada por la operadora.
        migrador.Migrate(anterior);
        Assert.DoesNotContain("PuntoVenta", Columnas(cadena));
        Ejecutar(cadena,
            "UPDATE ConfiguracionNegocio SET AlicuotaIvaCentesimas = 1050, CondicionFiscal = 'monotributo', " +
            "MultiploRedondeoCentavos = 5000, TopeIdentificacionCentavos = 100025");
        var sqlAntes = SqlTabla(cadena);
        var datos = Filas(cadena, SelectFila);

        migrador.Migrate(puntoVenta);

        // Sin reconstruir: el CREATE TABLE es el mismo, con la columna nueva al final; los datos iguales y sin punto de
        // venta (D-07).
        const string columna =
            ", \"PuntoVenta\" INTEGER NULL CONSTRAINT \"CK_ConfiguracionNegocio_PuntoVenta\" CHECK " +
            "(\"PuntoVenta\" IS NULL OR \"PuntoVenta\" BETWEEN 1 AND 99999)";
        var sqlDespues = SqlTabla(cadena);
        Assert.Contains(columna, sqlDespues, StringComparison.Ordinal);
        Assert.Equal(sqlAntes, sqlDespues.Replace(columna, string.Empty, StringComparison.Ordinal));
        Assert.Equal(datos, Filas(cadena, SelectFila));
        Assert.Equal(["NULL"], Assert.Single(Filas(cadena, SelectPuntoVenta)));

        // Down (solo desarrollo): DROP COLUMN deja la tabla como estaba, con los datos.
        Ejecutar(cadena, "UPDATE ConfiguracionNegocio SET PuntoVenta = 3");
        migrador.Migrate(anterior);
        Assert.DoesNotContain("PuntoVenta", Columnas(cadena));
        Assert.Equal(sqlAntes, SqlTabla(cadena));
        Assert.Equal(datos, Filas(cadena, SelectFila));

        migrador.Migrate();
        Assert.Empty(contexto.Database.GetPendingMigrations());
        Assert.Equal(["NULL"], Assert.Single(Filas(cadena, SelectPuntoVenta)));
    }

    [Fact]
    public void AgregarInfraestructura_RegistraRepositorioDeConfiguracion()
    {
        using var baseTemporal = new BaseTemporal();
        using var alcance = baseTemporal.Servicios.CreateScope();
        using var otroAlcance = baseTemporal.Servicios.CreateScope();

        var repositorio = alcance.ServiceProvider.GetRequiredService<IConfiguracionNegocioRepository>();

        // Scoped: uno por alcance, el mismo dentro del alcance.
        Assert.IsType<ConfiguracionNegocioRepository>(repositorio);
        Assert.Same(repositorio, alcance.ServiceProvider.GetRequiredService<IConfiguracionNegocioRepository>());
        Assert.NotSame(repositorio, otroAlcance.ServiceProvider.GetRequiredService<IConfiguracionNegocioRepository>());
    }

    private static void AfirmarCheck(SqliteException error, string restriccion)
    {
        Assert.Equal(SqliteRestriccionCheck, error.SqliteExtendedErrorCode);
        Assert.Contains("CHECK constraint failed: " + restriccion, error.Message, StringComparison.Ordinal);
    }

    private static string CadenaDe(BaseTemporal baseTemporal) =>
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion;

    private static List<string> Columnas(string cadena) =>
        [.. Filas(cadena, "SELECT name FROM pragma_table_info('ConfiguracionNegocio')").Select(fila => fila[0])];

    private static string SqlTabla(string cadena) =>
        Assert.Single(Filas(cadena, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'ConfiguracionNegocio'"))[0];

    private static string TipoColumna(string cadena, string columna) =>
        Assert.Single(Filas(cadena, $"SELECT type FROM pragma_table_info('ConfiguracionNegocio') WHERE name = '{columna}'"))[0];

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
}
