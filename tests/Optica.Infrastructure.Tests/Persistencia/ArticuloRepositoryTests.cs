using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Application.Configuracion;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;
using Optica.Infrastructure.Persistencia;
using Optica.Pruebas.Compartido;
using Xunit.Abstractions;

namespace Optica.Infrastructure.Tests.Persistencia;

public class ArticuloRepositoryTests(ITestOutputHelper salida)
{
    private static readonly TimeSpan LimiteRecalculo = TimeSpan.FromSeconds(10);

    private const int SqliteRestriccionCheck = 275; // SQLITE_CONSTRAINT_CHECK
    private const int SqliteRestriccionUnica = 2067; // SQLITE_CONSTRAINT_UNIQUE

    private const string MensajeFilaInvalida = "El artículo guardado no es válido.";

    private const string SelectFila =
        "SELECT Id, CodigoProveedor, CodigoProveedorClave, Descripcion, PrecioCostoCentavos, MargenCentesimos, " +
        "PrecioVentaCentavos FROM Articulo ORDER BY Id";

    private const string SelectEstado = "SELECT Id, Activo, Version FROM Articulo ORDER BY Id";

    [Fact]
    public async Task Agregar_YConfirmar_AsignaCodigoAutonumerico()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);
        var unidad = new UnidadDeTrabajoEf(contexto);

        repositorio.Agregar(new Articulo(null, "ABC-1", "Armazón metálico", 1210m, 50m, 1815m, activo: true, version: 0));

        // Agregar solo registra el alta: sin confirmar, nada llega a la base.
        Assert.Empty(Filas(cadena, SelectFila));

        await unidad.ConfirmarAsync();
        repositorio.Agregar(new Articulo(null, "XYZ-9", "Cristal orgánico", 1000m, 50m, 1500m, activo: true, version: 0));
        await unidad.ConfirmarAsync();

        // Leídos desde otro alcance, por la clave como lo hace el caso de uso después de confirmar (AC-01).
        var lector = new ArticuloRepository(baseTemporal.CrearContexto());
        var primero = await lector.ObtenerPorClaveAsync("ABC-1");
        var segundo = await lector.ObtenerPorClaveAsync("XYZ-9");
        Assert.NotNull(primero);
        Assert.NotNull(segundo);
        Assert.NotNull(primero.Codigo);
        Assert.Equal(primero.Codigo + 1, segundo!.Codigo);

        // Y por el código asignado, con todos los datos.
        var leido = await lector.ObtenerAsync(primero.Codigo!.Value);
        Assert.NotNull(leido);
        Assert.Equal(primero.Codigo, leido.Codigo);
        Assert.Equal("ABC-1", leido.CodigoProveedor);
        Assert.Equal("ABC-1", leido.CodigoProveedorClave);
        Assert.Equal("Armazón metálico", leido.Descripcion);
        Assert.Equal(1210m, leido.PrecioCosto);
        Assert.Equal(50m, leido.MargenUtilidad);
        Assert.Equal(1815m, leido.PrecioVenta);

        // AUTOINCREMENT: un número usado no se reutiliza aunque su fila ya no esté.
        Ejecutar(cadena, $"DELETE FROM Articulo WHERE Id = {segundo.Codigo}");
        repositorio.Agregar(new Articulo(null, "QRS-2", "Estuche", 10m, 0m, 10m, activo: true, version: 0));
        await unidad.ConfirmarAsync();
        var tercero = await lector.ObtenerPorClaveAsync("QRS-2");
        Assert.Equal(segundo.Codigo + 1, tercero!.Codigo);
    }

    [Fact]
    public async Task Obtener_CodigoOClaveInexistente_DevuelveNull()
    {
        using var baseTemporal = new BaseTemporal();
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        Assert.Null(await repositorio.ObtenerAsync(1));
        Assert.Null(await repositorio.ObtenerPorClaveAsync("ABC-1"));
    }

    [Fact]
    public async Task Agregar_PrecioVenta_GuardadoEnCentavos()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // AC-07: 1.210,00 con 50 % da 1.815,00. El código y la descripción se guardan recortados.
        repositorio.Agregar(new Articulo(null, " abc-1 ", " Armazón ", 1210m, 50.25m, 1815m, activo: true, version: 0));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        var fila = Assert.Single(Filas(cadena, SelectFila));
        Assert.Equal(["abc-1", "ABC-1", "Armazón", "121000", "5025", "181500"], fila[1..]);
        Assert.Equal("INTEGER", TipoColumna(cadena, "PrecioCostoCentavos"));
        Assert.Equal("INTEGER", TipoColumna(cadena, "MargenCentesimos"));
        Assert.Equal("INTEGER", TipoColumna(cadena, "PrecioVentaCentavos"));
    }

    [Fact]
    public async Task Agregar_CostoNegativo_GuardadoYLeidoConSigno()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // FR-05: cero y negativos son válidos.
        repositorio.Agregar(new Articulo(null, "NEG-1", "Bonificación", -100m, 50m, -150m, activo: true, version: 0));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal(["-10000", "5000", "-15000"], Assert.Single(Filas(cadena, SelectFila))[4..]);
        var leido = await new ArticuloRepository(baseTemporal.CrearContexto()).ObtenerPorClaveAsync("NEG-1");
        Assert.Equal(-100m, leido!.PrecioCosto);
        Assert.Equal(-150m, leido.PrecioVenta);
        Assert.True(leido.CostoNoPositivo);
    }

    [Fact]
    public async Task Agregar_ClaveDuplicada_RechazadoConConflicto()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        new ArticuloRepository(contexto).Agregar(new Articulo(null, "ABC-1", "Armazón", 1210m, 50m, 1815m, activo: true, version: 0));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        // Otro alcance da de alta " abc-1 ": misma clave, la segunda barrera es el índice UNIQUE (AC-02, ADR-009).
        var otroContexto = baseTemporal.CrearContexto();
        new ArticuloRepository(otroContexto).Agregar(new Articulo(null, " abc-1 ", "Otro", 1m, 0m, 1m, activo: true, version: 0));

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(otroContexto).ConfirmarAsync());

        Assert.Null(error.InnerException);
        var fila = Assert.Single(Filas(cadena, SelectFila));
        Assert.Equal("Armazón", fila[3]);
    }

    [Fact]
    public void Insertar_ClaveDuplicadaPorSql_RechazadoPorIndiceUnico()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        Insertar(cadena, "abc-1", "ABC-1", "Uno", 100, 0, 100);

        var error = Assert.Throws<SqliteException>(() => Insertar(cadena, "ABC-1", "ABC-1", "Dos", 100, 0, 100));

        Assert.Equal(SqliteRestriccionUnica, error.SqliteExtendedErrorCode);
        Assert.Single(Filas(cadena, SelectFila));
    }

    [Fact]
    public async Task ExisteClave_EncuentraSinDistinguirMayusculas()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);
        Assert.False(await repositorio.ExisteClaveAsync(NormalizacionCodigo.Clave("ABC-1")));

        // Guardados en minúsculas, con "ñandú" escrito con caracteres precompuestos (U+00F1, U+00FA).
        repositorio.Agregar(new Articulo(null, "abc-1", "Armazón", 1m, 0m, 1m, activo: true, version: 0));
        repositorio.Agregar(new Articulo(null, "ñandú-1", "Estuche", 1m, 0m, 1m, activo: true, version: 0));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        var lector = new ArticuloRepository(baseTemporal.CrearContexto());
        Assert.True(await lector.ExisteClaveAsync(NormalizacionCodigo.Clave("ABC-1")));
        Assert.True(await lector.ExisteClaveAsync(NormalizacionCodigo.Clave(" Abc-1\t")));

        // Mayúsculas con tilde y forma descompuesta (N + U+0303, U+0301): la misma clave (ADR-009).
        Assert.True(await lector.ExisteClaveAsync(NormalizacionCodigo.Clave("ÑANDÚ-1")));
        Assert.False(await lector.ExisteClaveAsync(NormalizacionCodigo.Clave("ABC-2")));
    }

    [Theory]
    [InlineData(100001)]
    [InlineData(-1)]
    public void Insertar_MargenFueraDeRango_RechazadoPorCheck(long margenCentesimos)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        var error = Assert.Throws<SqliteException>(
            () => Insertar(cadena, "ABC-1", "ABC-1", "Armazón", 100, margenCentesimos, 100));

        AfirmarCheck(error, "CK_Articulo_Margen");
        Assert.Empty(Filas(cadena, SelectFila));

        // Los extremos del rango sí entran.
        Insertar(cadena, "ABC-1", "ABC-1", "Armazón", 100, 0, 100);
        Insertar(cadena, "ABC-2", "ABC-2", "Armazón", 100, 100000, 1100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Insertar_CodigoVacioODe51_RechazadoPorCheck(int largo)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = new string('A', largo);

        var error = Assert.Throws<SqliteException>(
            () => Insertar(cadena, codigo, "CLAVE", "Armazón", 100, 0, 100));

        AfirmarCheck(error, "CK_Articulo_CodigoProveedor");
        Assert.Empty(Filas(cadena, SelectFila));

        // 1 y 50 caracteres sí entran.
        Insertar(cadena, "A", "A", "Armazón", 100, 0, 100);
        Insertar(cadena, new string('B', 50), new string('B', 50), "Armazón", 100, 0, 100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Insertar_DescripcionVaciaODe201_RechazadoPorCheck(int largo)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        var error = Assert.Throws<SqliteException>(
            () => Insertar(cadena, "ABC-1", "ABC-1", new string('d', largo), 100, 0, 100));

        AfirmarCheck(error, "CK_Articulo_Descripcion");
        Assert.Empty(Filas(cadena, SelectFila));

        Insertar(cadena, "ABC-1", "ABC-1", new string('d', 200), 100, 0, 100);
    }

    [Fact]
    public void Insertar_ClaveMasLargaQueElCodigo_Aceptada()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        // La clave no lleva CHECK de largo: la normalización NFC y las mayúsculas pueden alargarla (ADR-009).
        Insertar(cadena, new string('a', 50), new string('A', 60), "Armazón", 100, 0, 100);

        Assert.Single(Filas(cadena, SelectFila));
    }

    [Fact]
    public void Esquema_Articulo_ColumnasNotNullYChecks()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        // Las columnas exactas, todas NOT NULL, con Activo y Version (FEAT-002b).
        Assert.Equal(
            [
                "Activo INTEGER 1", "CodigoProveedor TEXT 1", "CodigoProveedorClave TEXT 1", "Descripcion TEXT 1",
                "Id INTEGER 1", "MargenCentesimos INTEGER 1", "PrecioCostoCentavos INTEGER 1",
                "PrecioVentaCentavos INTEGER 1", "Version INTEGER 1",
            ],
            Columnas(cadena));

        // Los DEFAULT solo existen en la tabla, para las filas que ya estaban al migrar (D-21).
        Assert.Equal(
            [["Activo", "1"], ["Version", "0"]],
            Filas(cadena,
                "SELECT name, dflt_value FROM pragma_table_info('Articulo') WHERE name IN ('Activo', 'Version') ORDER BY name"));

        // Lo de FEAT-002a sigue: AUTOINCREMENT, los CHECK y el índice UNIQUE, sin índices nuevos.
        var sql = SqlTablaArticulo(cadena);
        Assert.Contains("AUTOINCREMENT", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var restriccion in new[]
                 {
                     "CK_Articulo_CodigoProveedor", "CK_Articulo_Descripcion", "CK_Articulo_Margen", "CK_Articulo_Activo",
                     "CK_Articulo_Version",
                 })
        {
            Assert.Contains(restriccion, sql, StringComparison.Ordinal);
        }

        Assert.Equal([["IX_Articulo_CodigoProveedorClave", "1"]],
            Filas(cadena, "SELECT name, \"unique\" FROM pragma_index_list('Articulo')"));
        Assert.Equal(["CodigoProveedorClave"], Assert.Single(Filas(cadena,
            "SELECT name FROM pragma_index_info('IX_Articulo_CodigoProveedorClave')")));

        // Activo solo 0 o 1, Version desde 0.
        AfirmarCheck(Assert.Throws<SqliteException>(() => InsertarConEstado(cadena, "ABC-1", 2, 0)), "CK_Articulo_Activo");
        AfirmarCheck(Assert.Throws<SqliteException>(() => InsertarConEstado(cadena, "ABC-1", -1, 0)), "CK_Articulo_Activo");
        AfirmarCheck(Assert.Throws<SqliteException>(() => InsertarConEstado(cadena, "ABC-1", 1, -1)), "CK_Articulo_Version");
        Assert.Empty(Filas(cadena, SelectFila));
        InsertarConEstado(cadena, "ABC-1", 0, 0);
        InsertarConEstado(cadena, "ABC-2", 1, long.MaxValue);

        // Sin nombrarlas, las columnas toman los DEFAULT.
        Insertar(cadena, "ABC-3", "ABC-3", "Armazón", 100, 0, 100);
        Assert.Equal(["1", "0"], Filas(cadena, SelectEstado)[2][1..]);
    }

    [Fact]
    public async Task Agregar_AsignaActivoYVersionDelArticulo()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // Agregar asigna siempre las dos columnas: los DEFAULT de la tabla no participan (D-21).
        repositorio.Agregar(Articulo.Nuevo("ABC-1", "Armazón", 1m, 0m, 1m));
        repositorio.Agregar(new Articulo(null, "ABC-2", "Estuche", 1m, 0m, 1m, activo: false, version: 7));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal([["1", "0"], ["0", "7"]], Filas(cadena, SelectEstado).Select(f => f[1..]));
        var lector = new ArticuloRepository(baseTemporal.CrearContexto());
        var inactivo = await lector.ObtenerPorClaveAsync("ABC-2");
        Assert.False(inactivo!.Activo);
        Assert.Equal(7, inactivo.Version);
        Assert.True((await lector.ObtenerPorClaveAsync("ABC-1"))!.Activo);
    }

    [Theory]
    [InlineData("Activo = 2")]
    [InlineData("Activo = -1")]
    public async Task AEntidad_ActivoInvalido_EsEstadoCorrupto(string asignacion)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        Insertar(cadena, CodigoDistintivo, CodigoDistintivo, DescripcionDistintiva, CostoDistintivoCentavos,
            MargenDistintivoCentesimos, VentaDistintivaCentavos);
        var id = long.Parse(Assert.Single(Filas(cadena, SelectFila))[0], CultureInfo.InvariantCulture);
        var sinGrupo = new SqliteConnectionStringBuilder(cadena) { Pooling = false }.ToString();
        Ejecutar(sinGrupo, $"PRAGMA ignore_check_constraints = ON; UPDATE Articulo SET {asignacion};");
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        // Un bool leído de SQLite toma cualquier valor distinto de 0 como verdadero: el repositorio no lo deja pasar.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.ObtenerAsync(id));
        Assert.Equal(MensajeFilaInvalida, error.Message);
        AfirmarSinValoresDeLaFila(error);

        // Lo mismo en la búsqueda, que arma las entidades con la misma conversión.
        var enBusqueda = await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.BuscarAsync(null, 1, 50));
        Assert.Equal(MensajeFilaInvalida, enBusqueda.Message);
        AfirmarSinValoresDeLaFila(enBusqueda);
    }

    [Fact]
    public async Task Migracion_CatalogoActivo_DejaActivasLasFilasExistentes()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var migrador = contexto.GetService<IMigrator>();
        // Por nombre, no por posición (D-29 de FEAT-007).
        var migraciones = contexto.Database.GetMigrations().ToList();
        var catalogo = Assert.Single(migraciones, m => m.EndsWith("_Catalogo", StringComparison.Ordinal));

        // Una base de FEAT-002a con artículos cargados.
        migrador.Migrate(catalogo);
        Assert.Equal(ColumnasFeat002a, Columnas(cadena));
        Insertar(cadena, "ABC-1", "ABC-1", "Armazón", 121000, 5000, 181500);
        Insertar(cadena, "ABC-2", "ABC-2", "Estuche", 1000, 0, 1000);
        Insertar(cadena, "ABC-3", "ABC-3", "Borrado", 1000, 0, 1000);
        Ejecutar(cadena, "DELETE FROM Articulo WHERE CodigoProveedor = 'ABC-3'");
        var datos = Filas(cadena, SelectFila);
        var secuencia = Filas(cadena, "SELECT seq FROM sqlite_sequence WHERE name = 'Articulo'");
        Assert.Equal([["3"]], secuencia);

        // Al actualizarla, las filas quedan activas, con versión 0 y los mismos datos.
        migrador.Migrate();
        Assert.Equal([["1", "0"], ["1", "0"]], Filas(cadena, SelectEstado).Select(f => f[1..]));
        Assert.Equal(datos, Filas(cadena, SelectFila));
        Assert.Contains("AUTOINCREMENT", SqlTablaArticulo(cadena), StringComparison.OrdinalIgnoreCase);

        // La migración no reconstruye la tabla: el código 3, usado y borrado, no se reutiliza.
        Assert.Equal(secuencia, Filas(cadena, "SELECT seq FROM sqlite_sequence WHERE name = 'Articulo'"));
        var leido = await new ArticuloRepository(baseTemporal.CrearContexto()).ObtenerPorClaveAsync("ABC-1");
        Assert.True(leido!.Activo);
        Assert.Equal(0, leido.Version);

        // Revertir quita las dos columnas y sus CHECK y conserva los datos; el estado inactivo se pierde.
        Ejecutar(cadena, "UPDATE Articulo SET Activo = 0, Version = 3 WHERE CodigoProveedor = 'ABC-2'");
        migrador.Migrate(catalogo);
        Assert.Equal(ColumnasFeat002a, Columnas(cadena));
        Assert.Equal(datos, Filas(cadena, SelectFila));
        var sql = SqlTablaArticulo(cadena);
        Assert.DoesNotContain("CK_Articulo_Activo", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CK_Articulo_Version", sql, StringComparison.Ordinal);
        Assert.Contains("CK_Articulo_Margen", sql, StringComparison.Ordinal);
        Assert.Contains("AUTOINCREMENT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([["IX_Articulo_CodigoProveedorClave", "1"]],
            Filas(cadena, "SELECT name, \"unique\" FROM pragma_index_list('Articulo')"));
        Assert.Equal(secuencia, Filas(cadena, "SELECT seq FROM sqlite_sequence WHERE name = 'Articulo'"));

        migrador.Migrate();
        Assert.Equal([["1", "0"], ["1", "0"]], Filas(cadena, SelectEstado).Select(f => f[1..]));
        Assert.Empty(contexto.Database.GetPendingMigrations());
        Insertar(cadena, "ABC-4", "ABC-4", "Nuevo", 1000, 0, 1000);
        Assert.Equal("4", Filas(cadena, SelectFila)[^1][0]);
    }

    [Fact]
    public async Task ExisteClaveDeOtro_NoCuentaAlMismoArticulo()
    {
        using var baseTemporal = new BaseTemporal();
        var codigos = await Grabar(baseTemporal, ("ABC-1", "Armazón"), ("ABC-2", "Estuche"));
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        Assert.False(await repositorio.ExisteClaveDeOtroAsync("ABC-1", codigos[0]));
        Assert.True(await repositorio.ExisteClaveDeOtroAsync("ABC-1", codigos[1]));
        Assert.False(await repositorio.ExisteClaveDeOtroAsync("ZZZ-9", codigos[0]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repositorio.ExisteClaveDeOtroAsync(null!, codigos[0]));
    }

    [Fact]
    public async Task Actualizar_GuardaDatosYIncrementaVersion()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón metálico")))[0];
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // AC-02: el margen pasa de 50 a 60 y el precio a 1.936,00. La versión del artículo no cuenta: vale la leída (D-17).
        repositorio.Actualizar(
            new Articulo(codigo, " abc-9 ", " Armazón de titanio ", 1210m, 60m, 1936m, activo: true, version: 99),
            versionLeida: 0);

        Assert.Equal(
            [
                "CodigoProveedor", "CodigoProveedorClave", "Descripcion", "MargenUtilidad", "PrecioCosto", "PrecioVenta",
                "Version",
            ],
            Modificadas(contexto));
        Assert.Equal(["ABC-1", "ABC-1", "Armazón metálico"], Assert.Single(Filas(cadena, SelectFila))[1..4]);

        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal(["abc-9", "ABC-9", "Armazón de titanio", "121000", "6000", "193600"],
            Assert.Single(Filas(cadena, SelectFila))[1..]);
        Assert.Equal(["1", "1"], Assert.Single(Filas(cadena, SelectEstado))[1..]);

        // Con la entrada que el contexto ya sigue, una segunda modificación parte de la versión 1.
        repositorio.Actualizar(new Articulo(codigo, "abc-9", "Armazón", 1210m, 60m, 1936m, activo: true, version: 1), 1);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        var leido = await new ArticuloRepository(baseTemporal.CrearContexto()).ObtenerAsync(codigo);
        Assert.Equal("Armazón", leido!.Descripcion);
        Assert.Equal(2, leido.Version);
        Assert.True(leido.Activo);
    }

    [Fact]
    public void Actualizar_SinCodigoOVersionInvalida_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        var sinCodigo = Assert.Throws<ArgumentException>(
            () => repositorio.Actualizar(Articulo.Nuevo("ABC-1", "Armazón", 1m, 0m, 1m), 0));
        Assert.Equal("articulo", sinCodigo.ParamName);
        Assert.Throws<ArgumentNullException>(() => repositorio.Actualizar(null!, 0));
        var articulo = new Articulo(1, "ABC-1", "Armazón", 1m, 0m, 1m, activo: true, version: 0);
        Assert.Equal("versionLeida",
            Assert.Throws<ArgumentOutOfRangeException>(() => repositorio.Actualizar(articulo, -1)).ParamName);
        Assert.Equal("versionLeida",
            Assert.Throws<ArgumentOutOfRangeException>(() => repositorio.Actualizar(articulo, long.MaxValue)).ParamName);
        Assert.False(contexto.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Actualizar_VersionCambiada_ConflictoDeConcurrencia()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón")))[0];

        // Dos pantallas leen la versión 0; graba primero la segunda.
        var primera = baseTemporal.CrearContexto();
        var leidoPrimera = await new ArticuloRepository(primera).ObtenerAsync(codigo);
        var segunda = baseTemporal.CrearContexto();
        new ArticuloRepository(segunda).Actualizar(
            new Articulo(codigo, "ABC-1", "De la segunda", 1210m, 60m, 1936m, activo: true, version: 0), 0);
        await new UnidadDeTrabajoEf(segunda).ConfirmarAsync();

        new ArticuloRepository(primera).Actualizar(
            new Articulo(codigo, "ABC-1", "De la primera", 1210m, 50m, 1815m, activo: true, version: 0),
            leidoPrimera!.Version);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(primera).ConfirmarAsync());

        Assert.Equal(MotivoConflicto.Concurrencia, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("De la", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(primera.ChangeTracker.Entries());
        Assert.Equal("De la segunda", Assert.Single(Filas(cadena, SelectFila))[3]);
        Assert.Equal(["1", "1"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
    }

    [Fact]
    public async Task Actualizar_FilaInactiva_ConflictoDeConcurrencia()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón")))[0];
        var baja = baseTemporal.CrearContexto();
        new ArticuloRepository(baja).CambiarEstado(codigo, activo: false);
        await new UnidadDeTrabajoEf(baja).ConfirmarAsync();

        // Un llamador arma un artículo activo con la versión vigente: Activo es token y la fila inactiva no se toca (D-22).
        var contexto = baseTemporal.CrearContexto();
        new ArticuloRepository(contexto).Actualizar(
            new Articulo(codigo, "ABC-1", "Pisado", 1210m, 60m, 1936m, activo: true, version: 1), versionLeida: 1);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Equal(MotivoConflicto.Concurrencia, error.Motivo);
        Assert.Equal("Armazón", Assert.Single(Filas(cadena, SelectFila))[3]);
        Assert.Equal(["0", "1"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
    }

    [Fact]
    public async Task Actualizar_EntradaInactivaYaSeguida_ConflictoDeConcurrencia()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón")))[0];
        var datos = Filas(cadena, SelectFila);

        // En el mismo contexto: la baja se confirma y la entrada queda seguida con Activo = 0 como valor original.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);
        repositorio.CambiarEstado(codigo, activo: false);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        // Sin limpiar el contexto, una modificación con la versión vigente: el UPDATE tiene que exigir Activo = 1 y no el
        // valor que la entrada seguía (D-22).
        repositorio.Actualizar(
            new Articulo(codigo, "ABC-1", "Pisado", 1210m, 60m, 1936m, activo: true, version: 1), versionLeida: 1);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Equal(MotivoConflicto.Concurrencia, error.Motivo);
        Assert.Equal(datos, Filas(cadena, SelectFila));
        Assert.Equal(["0", "1"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
    }

    [Fact]
    public async Task Actualizar_ClaveDuplicada_ConflictoDeUnicidad()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigos = await Grabar(baseTemporal, ("ABC-1", "Armazón"), ("ABC-2", "Estuche"));
        var datos = Filas(cadena, SelectFila);
        var contexto = baseTemporal.CrearContexto();

        // " abc-1 " tiene la clave del otro artículo: el índice UNIQUE lo rechaza (ADR-009).
        new ArticuloRepository(contexto).Actualizar(
            new Articulo(codigos[1], " abc-1 ", "Estuche", 1210m, 50m, 1815m, activo: true, version: 0), 0);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.Equal(datos, Filas(cadena, SelectFila));
        Assert.Equal([["1", "0"], ["1", "0"]], Filas(cadena, SelectEstado).Select(f => f[1..]));
    }

    [Fact]
    public async Task CambiarEstado_Desactivar_SoloCambiaActivoYVersion()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón metálico")))[0];
        var datos = Filas(cadena, SelectFila);
        var contexto = baseTemporal.CrearContexto();

        // AC-05: la baja escribe solo Activo y Version (D-09).
        new ArticuloRepository(contexto).CambiarEstado(codigo, activo: false);
        Assert.Equal(["Activo", "Version"], Modificadas(contexto));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal(datos, Filas(cadena, SelectFila));
        Assert.Equal(["0", "1"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
        var leido = await new ArticuloRepository(baseTemporal.CrearContexto()).ObtenerAsync(codigo);
        Assert.False(leido!.Activo);
        Assert.Equal(1, leido.Version);
        Assert.Equal("Armazón metálico", leido.Descripcion);
        Assert.Equal(1815m, leido.PrecioVenta);
    }

    [Fact]
    public async Task CambiarEstado_Reactivar_QuedaActivo()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón metálico")))[0];
        var datos = Filas(cadena, SelectFila);
        var baja = baseTemporal.CrearContexto();
        new ArticuloRepository(baja).CambiarEstado(codigo, activo: false);
        await new UnidadDeTrabajoEf(baja).ConfirmarAsync();

        // AC-06: reactivar en otro alcance deja el artículo activo con sus datos.
        var contexto = baseTemporal.CrearContexto();
        new ArticuloRepository(contexto).CambiarEstado(codigo, activo: true);
        Assert.Equal(["Activo", "Version"], Modificadas(contexto));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal(datos, Filas(cadena, SelectFila));
        Assert.Equal(["1", "2"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
        Assert.True((await new ArticuloRepository(baseTemporal.CrearContexto()).ObtenerAsync(codigo))!.Activo);
    }

    [Fact]
    public async Task CambiarEstado_CambioEnElMedio_ConflictoDeConcurrencia()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón")))[0];
        var contexto = baseTemporal.CrearContexto();
        new ArticuloRepository(contexto).CambiarEstado(codigo, activo: false);

        // Otra escritura confirma entre la lectura y la grabación de la baja (D-16).
        var otro = baseTemporal.CrearContexto();
        new ArticuloRepository(otro).Actualizar(
            new Articulo(codigo, "ABC-1", "Editado", 1210m, 50m, 1815m, activo: true, version: 0), 0);
        await new UnidadDeTrabajoEf(otro).ConfirmarAsync();

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());
        Assert.Equal(MotivoConflicto.Concurrencia, error.Motivo);
        Assert.Equal(["1", "1"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
    }

    [Fact]
    public async Task CambiarEstado_CodigoInexistente_LanzaError()
    {
        using var baseTemporal = new BaseTemporal();
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón")))[0];
        var contexto = baseTemporal.CrearContexto();

        var error = Assert.Throws<InvalidOperationException>(
            () => new ArticuloRepository(contexto).CambiarEstado(codigo + 1000, activo: false));

        Assert.DoesNotContain((codigo + 1000).ToString(CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.False(contexto.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task ListarParaRecalculo_DevuelveActivosEInactivos()
    {
        using var baseTemporal = new BaseTemporal();
        var codigos = await Grabar(baseTemporal, ("ABC-1", "Armazón"), ("ABC-2", "Estuche"), ("ABC-3", "Cristal"));
        var baja = baseTemporal.CrearContexto();
        new ArticuloRepository(baja).CambiarEstado(codigos[1], activo: false);
        await new UnidadDeTrabajoEf(baja).ConfirmarAsync();
        var contexto = baseTemporal.CrearContexto();

        // AC-03: el recálculo alcanza también a los inactivos, cada uno con el estado y la versión leídos (D-04).
        var articulos = await new ArticuloRepository(contexto).ListarParaRecalculoAsync();

        Assert.Equal(codigos, articulos.Select(a => a.Codigo!.Value));
        Assert.Equal([true, false, true], articulos.Select(a => a.Activo));
        Assert.Equal([0L, 1L, 0L], articulos.Select(a => a.Version));
        Assert.Equal(["Armazón", "Estuche", "Cristal"], articulos.Select(a => a.Descripcion));
        Assert.All(articulos, a => Assert.Equal(1815m, a.PrecioVenta));
        // Sin seguimiento: las filas leídas no quedan en el contexto.
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public void RegistrarPrecioRecalculado_SinCodigo_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        var sinCodigo = Assert.Throws<ArgumentException>(
            () => repositorio.RegistrarPrecioRecalculado(Articulo.Nuevo("QZ-777", "Lente distintivo", 1m, 0m, 1m)));
        Assert.Equal("articulo", sinCodigo.ParamName);
        Assert.DoesNotContain("QZ-777", sinCodigo.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Lente distintivo", sinCodigo.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => repositorio.RegistrarPrecioRecalculado(null!));

        // La versión se incrementa con checked: la última posible no se desborda a negativa (D-04).
        Assert.Throws<OverflowException>(() => repositorio.RegistrarPrecioRecalculado(
            new Articulo(1, "ABC-1", "Armazón", 1m, 0m, 1m, activo: true, version: long.MaxValue)));
        Assert.False(contexto.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task RegistrarPrecioRecalculado_Inactivo_GrabaPrecioYVersion()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón metálico")))[0];
        var baja = baseTemporal.CrearContexto();
        new ArticuloRepository(baja).CambiarEstado(codigo, activo: false);
        await new UnidadDeTrabajoEf(baja).ConfirmarAsync();
        var datos = Filas(cadena, SelectFila);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // AC-03: el inactivo de 1.815,00 se recalcula con múltiplo 50 y queda en 1.850,00, inactivo.
        var leido = Assert.Single(await repositorio.ListarParaRecalculoAsync());
        var recalculo = leido.ConPrecioRecalculado(new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 50m, null, puntoVenta: null));
        Assert.True(recalculo.EsCambiado);
        repositorio.RegistrarPrecioRecalculado(recalculo.Articulo);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal([.. datos[0][..6], "185000"], Assert.Single(Filas(cadena, SelectFila)));
        Assert.Equal(["0", "2"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
    }

    [Fact]
    public async Task RegistrarPrecioRecalculado_EntradaSeguidaConOtrosCambios_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón metálico")))[0];
        var datos = Filas(cadena, SelectFila);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // La baja deja la fila seguida con Activo y Version modificados: registrar el precio encima no garantizaría que
        // solo se escriban el precio y la versión (D-04). Es un error de programación y se rechaza sin valores.
        repositorio.CambiarEstado(codigo, activo: false);
        var error = Assert.Throws<InvalidOperationException>(() => repositorio.RegistrarPrecioRecalculado(
            new Articulo(codigo, "QZ-777", "Lente distintivo", 1210m, 50m, 1850m, activo: true, version: 0)));

        Assert.DoesNotContain("QZ-777", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Lente distintivo", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1850", error.Message, StringComparison.Ordinal);
        Assert.Equal(["Activo", "Version"], Modificadas(contexto));
        Assert.Equal(datos, Filas(cadena, SelectFila));

        // Confirmada la baja, la entrada queda seguida sin cambios y el precio recalculado se registra sobre ella.
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        Assert.Equal(EntityState.Unchanged, Assert.Single(contexto.ChangeTracker.Entries<ArticuloFila>()).State);
        repositorio.RegistrarPrecioRecalculado(
            new Articulo(codigo, "ABC-1", "Armazón metálico", 1210m, 50m, 1850m, activo: false, version: 1));
        Assert.Equal(["PrecioVenta", "Version"], Modificadas(contexto));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal("185000", Assert.Single(Filas(cadena, SelectFila))[6]);
        Assert.Equal(["0", "2"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
    }

    [Fact]
    public async Task RegistrarPrecioRecalculado_SoloCambiaPrecioYVersion()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = (await Grabar(baseTemporal, ("ABC-1", "Armazón metálico")))[0];
        var datos = Filas(cadena, SelectFila);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // D-04: aunque el artículo recibido traiga otros datos, solo se escriben el precio de venta y la versión.
        repositorio.RegistrarPrecioRecalculado(
            new Articulo(codigo, "ZZZ-9", "Otra descripción", 99m, 10m, 1850m, activo: true, version: 0));
        Assert.Equal(["PrecioVenta", "Version"], Modificadas(contexto));
        Assert.Equal(datos, Filas(cadena, SelectFila));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        Assert.Equal([.. datos[0][..6], "185000"], Assert.Single(Filas(cadena, SelectFila)));
        Assert.Equal(["1", "1"], Assert.Single(Filas(cadena, SelectEstado))[1..]);

        // Los tokens son la versión y el estado leídos: con una versión vieja, o con el estado que ya no es, no se escribe.
        var versionVieja = baseTemporal.CrearContexto();
        new ArticuloRepository(versionVieja).RegistrarPrecioRecalculado(
            new Articulo(codigo, "ABC-1", "Armazón metálico", 1210m, 50m, 1900m, activo: true, version: 0));
        var porVersion = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(versionVieja).ConfirmarAsync());
        var estadoViejo = baseTemporal.CrearContexto();
        new ArticuloRepository(estadoViejo).RegistrarPrecioRecalculado(
            new Articulo(codigo, "ABC-1", "Armazón metálico", 1210m, 50m, 1900m, activo: false, version: 1));
        var porEstado = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(estadoViejo).ConfirmarAsync());

        Assert.Equal(MotivoConflicto.Concurrencia, porVersion.Motivo);
        Assert.Equal(MotivoConflicto.Concurrencia, porEstado.Motivo);
        Assert.Equal("185000", Assert.Single(Filas(cadena, SelectFila))[6]);
        Assert.Equal(["1", "1"], Assert.Single(Filas(cadena, SelectEstado))[1..]);
    }

    /// <summary>
    /// AC-05 con AC-02 y AC-03 sobre 10.000 artículos sembrados (D-15): pasar el múltiplo de 0,01 a 50 recalcula los
    /// 10.000, activos e inactivos, cada uno con el precio de la calculadora y la versión 1, sin tocar los demás datos;
    /// el de costo 1.210 y margen 50 pasa de 1.815,00 a 1.850,00. Registra la duración y verifica que queda por debajo
    /// de 10 s, lejos de la espera de 30 s (D-21).
    /// </summary>
    [Fact]
    [Trait("Categoria", "Volumen")]
    public async Task Volumen10000_Recalculo_Multiplo50()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var sembrados = SiembraCatalogo.Generar();
        SiembraCatalogo.Sembrar(cadena, sembrados);
        var antes = Filas(cadena, SelectFila);
        Assert.Equal(SiembraCatalogo.Volumen, antes.Count);
        Assert.Equal("181500", antes[0][6]);

        var contexto = baseTemporal.CrearContexto();
        var vigente = await new ConfiguracionNegocioRepository(baseTemporal.CrearContexto()).ObtenerAsync();
        var caso = new GrabarConfiguracionConRecalculo(
            new ConfiguracionNegocioRepository(contexto), new ArticuloRepository(contexto), new UnidadDeTrabajoEf(contexto),
            TimeProvider.System, NullLoggerFactory.Instance);
        var datos = new DatosConfiguracion(
            vigente.AlicuotaIva, PoliticaConfiguracionNegocio.TextoDe(vigente.CondicionFiscal), 50m,
            vigente.TopeIdentificacion);

        var reloj = Stopwatch.StartNew();
        var resultado = await caso.EjecutarAsync(datos);
        reloj.Stop();
        salida.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Recálculo de {SiembraCatalogo.Volumen} artículos con múltiplo 50: {reloj.Elapsed.TotalMilliseconds:F0} ms."));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(SiembraCatalogo.Volumen, resultado.PreciosCambiados);
        Assert.True(
            reloj.Elapsed < LimiteRecalculo,
            string.Create(CultureInfo.InvariantCulture, $"El recálculo tardó {reloj.Elapsed.TotalMilliseconds:F0} ms."));

        var nueva = new ConfiguracionNegocio(
            vigente.AlicuotaIva, vigente.CondicionFiscal, 50m, vigente.TopeIdentificacion, puntoVenta: null);
        var despues = Filas(cadena, SelectFila);
        Assert.Equal(antes.Count, despues.Count);
        for (var i = 0; i < despues.Count; i++)
        {
            var esperado = CalculadoraPrecioVenta.Calcular(sembrados[i].PrecioCosto, sembrados[i].MargenUtilidad, nueva)
                .Precio!.PrecioVenta;
            Assert.Equal(antes[i][..6], despues[i][..6]);
            Assert.Equal(SiembraCatalogo.Centesimos(esperado).ToString(CultureInfo.InvariantCulture), despues[i][6]);
        }

        Assert.Equal("185000", despues[0][6]);
        var estados = Filas(cadena, SelectEstado);
        Assert.Equal(sembrados.Select(s => s.Activo ? "1" : "0"), estados.Select(e => e[1]));
        Assert.Equal(1_000, estados.Count(e => e[1] == "0"));
        Assert.All(estados, e => Assert.Equal("1", e[2]));
        Assert.Equal(50m, (await new ConfiguracionNegocioRepository(baseTemporal.CrearContexto()).ObtenerAsync())
            .MultiploRedondeo);
    }

    /// <summary>
    /// Registrar un precio recalculado no recorre todo el contexto (DetectChanges global): con 10.000 artículos
    /// registrados uno por uno, ese recorrido en cada llamada hacía cuadrático el recálculo (Block 5 de FEAT-002c). Solo
    /// revisa la entrada del artículo, y deja la detección automática como estaba, también si lanza.
    /// </summary>
    [Fact]
    public async Task RegistrarPrecioRecalculado_NoRecorreElContexto_ConservaLaDeteccionAutomatica()
    {
        using var baseTemporal = new BaseTemporal();
        var codigos = await Grabar(baseTemporal, ("ABC-1", "Armazón"), ("ABC-2", "Estuche"), ("ABC-3", "Cristal"));
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // Una fila seguida, cambiada sin que el contexto lo haya detectado todavía.
        var ajena = contexto.Articulos.Find(codigos[0])!;
        ajena.Descripcion = "Otra descripción";
        repositorio.RegistrarPrecioRecalculado(
            new Articulo(codigos[1], "ABC-2", "Estuche", 1210m, 50m, 1850m, activo: true, version: 0));
        Assert.True(contexto.ChangeTracker.AutoDetectChangesEnabled);

        // La que sí se revisa es la entrada del artículo registrado: una baja sin confirmar se rechaza igual (D-04).
        repositorio.CambiarEstado(codigos[2], activo: false);
        Assert.Throws<InvalidOperationException>(() => repositorio.RegistrarPrecioRecalculado(
            new Articulo(codigos[2], "ABC-3", "Cristal", 1210m, 50m, 1850m, activo: true, version: 0)));
        Assert.True(contexto.ChangeTracker.AutoDetectChangesEnabled);

        // Entries() también llama a DetectChanges si la detección automática está prendida: leerlo así detectaría el cambio
        // ajeno en este momento y la prueba ya no vería si el registro recorrió el contexto. Se apaga solo para leer.
        contexto.ChangeTracker.AutoDetectChangesEnabled = false;
        var estados = contexto.ChangeTracker.Entries<ArticuloFila>().ToDictionary(e => e.Entity.Id, e => e.State);
        contexto.ChangeTracker.AutoDetectChangesEnabled = true;
        Assert.Equal(EntityState.Unchanged, estados[codigos[0]]);
        Assert.Equal(EntityState.Modified, estados[codigos[1]]);
        Assert.Equal(EntityState.Modified, estados[codigos[2]]);

        // Al confirmar, SaveChanges detecta lo que el registro no revisó: el cambio ajeno se graba junto con el precio
        // recalculado (D-29).
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        var cadena = CadenaDe(baseTemporal);
        var filas = Filas(cadena, SelectFila).ToDictionary(f => long.Parse(f[0], CultureInfo.InvariantCulture));
        Assert.Equal("Otra descripción", filas[codigos[0]][3]);
        Assert.Equal("185000", filas[codigos[1]][6]);
        Assert.Equal("Cristal", filas[codigos[2]][3]);
        var estadosGrabados = Filas(cadena, SelectEstado).ToDictionary(f => long.Parse(f[0], CultureInfo.InvariantCulture));
        Assert.Equal(["1", "1"], estadosGrabados[codigos[1]][1..]);
        Assert.Equal(["0", "1"], estadosGrabados[codigos[2]][1..]);
    }

    [Theory]
    [InlineData(0, 50, "pagina")]
    [InlineData(-1, 50, "pagina")]
    [InlineData(int.MinValue, 50, "pagina")]
    [InlineData(1, 0, "tamano")]
    [InlineData(1, -5, "tamano")]
    public async Task BuscarAsync_PaginaInvalida_Lanza(int pagina, int tamano, string parametro)
    {
        using var baseTemporal = new BaseTemporal();
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repositorio.BuscarAsync(null, pagina, tamano));

        Assert.Equal(parametro, error.ParamName);
    }

    [Fact]
    public async Task BuscarAsync_DesplazamientoMayorQueInt_LanzaError()
    {
        using var baseTemporal = new BaseTemporal();
        await Grabar(baseTemporal, ("ABC-1", "Armazón"));
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        // (int.MaxValue - 1) × 2 no entra en un int: se rechaza en lugar de desbordar (D-21).
        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repositorio.BuscarAsync(null, int.MaxValue, 2));
        Assert.Equal("pagina", error.ParamName);

        // (int.MaxValue - 1) × 1 sí entra: una página posterior a la última, vacía, con el total.
        var vacia = await repositorio.BuscarAsync(null, int.MaxValue, 1);
        Assert.Empty(vacia.Articulos);
        Assert.Equal(1, vacia.Total);
    }

    [Fact]
    public async Task BuscarAsync_Armazon_EncuentraConYSinTilde()
    {
        using var baseTemporal = new BaseTemporal();
        await Grabar(baseTemporal,
            ("ABC-1", "Armazón metálico"),
            ("ABC-2", "ARMAZON de acetato"),
            ("ABC-3", "Estuche rígido"),
            // La descripción guardada en forma descompuesta (o + U+0301) también coincide.
            ("ABC-4", "Armazo\U00000301n flex"));
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        // AC-04: "armazon" encuentra "Armazón", sin mayúsculas ni acentos.
        foreach (var buscado in new[] { "armazon", "Armazón", "ARMAZ\U000000D3N" })
        {
            var pagina = await repositorio.BuscarAsync(PlegadoBusqueda.Plegar(buscado), 1, 50);
            Assert.Equal(3, pagina.Total);
            Assert.Equal(
                ["ARMAZON de acetato", "Armazo\U00000301n flex", "Armazón metálico"],
                pagina.Articulos.Select(a => a.Descripcion));
        }

        // Sin texto, todo el catálogo.
        Assert.Equal(4, (await repositorio.BuscarAsync(null, 1, 50)).Total);
        Assert.Equal(0, (await repositorio.BuscarAsync(PlegadoBusqueda.Plegar("cristal"), 1, 50)).Total);
    }

    [Fact]
    public async Task BuscarAsync_SoloActivosArmazon_ExcluyeInactivos()
    {
        using var baseTemporal = new BaseTemporal();
        var codigos = await Grabar(baseTemporal,
            ("ABC-1", "Armazón metálico"),
            ("ABC-2", "ARMAZON de acetato"),
            ("ABC-3", "Armazón dado de baja"),
            ("ABC-4", "Estuche rígido"));
        var baja = baseTemporal.CrearContexto();
        new ArticuloRepository(baja).CambiarEstado(codigos[2], activo: false);
        await new UnidadDeTrabajoEf(baja).ConfirmarAsync();
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());
        var armazon = PlegadoBusqueda.Plegar("armazon");

        // AC-13: "armazon" lista los activos con o sin tilde y ningún inactivo, en la página y en el total (D-15).
        var activos = await repositorio.BuscarAsync(armazon, 1, 50, soloActivos: true);
        Assert.Equal(2, activos.Total);
        Assert.Equal(["ARMAZON de acetato", "Armazón metálico"], activos.Articulos.Select(a => a.Descripcion));
        Assert.All(activos.Articulos, a => Assert.True(a.Activo));

        // La página cuenta solo los activos: la segunda de a 1 es el segundo activo.
        var segunda = await repositorio.BuscarAsync(armazon, 2, 1, soloActivos: true);
        Assert.Equal(["Armazón metálico"], segunda.Articulos.Select(a => a.Descripcion));
        Assert.Equal(2, segunda.Total);

        // Sin texto, todo el catálogo activo; sin soloActivos, el listado del catálogo sigue mostrando los inactivos.
        Assert.Equal(3, (await repositorio.BuscarAsync(null, 1, 50, soloActivos: true)).Total);
        var todos = await repositorio.BuscarAsync(armazon, 1, 50);
        Assert.Equal(3, todos.Total);
        Assert.Equal("Armazón dado de baja", todos.Articulos[0].Descripcion);
    }

    [Fact]
    public async Task BuscarAsync_SoloActivosPorCodigo_ListaElArticulo()
    {
        using var baseTemporal = new BaseTemporal();
        var articulos = Enumerable.Range(0, 12)
            .Select(i => ($"PROV-{(char)('A' + i)}", $"Artículo {(char)('a' + i)}"))
            .ToArray();
        var codigos = await Grabar(baseTemporal, articulos);
        var inactivo = codigos[9];
        var baja = baseTemporal.CrearContexto();
        new ArticuloRepository(baja).CambiarEstado(inactivo, activo: false);
        await new UnidadDeTrabajoEf(baja).ConfirmarAsync();
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());
        static string Texto(long codigo) => codigo.ToString(CultureInfo.InvariantCulture);

        // AC-14: el código de un artículo activo lo lista.
        var activo = codigos[11];
        var porCodigo = await repositorio.BuscarAsync(Texto(activo), 1, 50, soloActivos: true);
        Assert.Equal([activo], porCodigo.Articulos.Select(a => a.Codigo!.Value));
        Assert.Equal(1, porCodigo.Total);

        // El código de uno inactivo no lo lista con soloActivos, y sí sin él.
        var buscadoInactivo = await repositorio.BuscarAsync(Texto(inactivo), 1, 50, soloActivos: true);
        Assert.DoesNotContain(inactivo, buscadoInactivo.Articulos.Select(a => a.Codigo!.Value));
        Assert.Contains(inactivo, (await repositorio.BuscarAsync(Texto(inactivo), 1, 50)).Articulos.Select(a => a.Codigo!.Value));

        // La coincidencia parcial por código tampoco trae al inactivo.
        var conUno = codigos.Where(c => c != inactivo && Texto(c).Contains('1', StringComparison.Ordinal)).Order().ToList();
        var parcial = await repositorio.BuscarAsync("1", 1, 50, soloActivos: true);
        Assert.Equal(conUno, parcial.Articulos.Select(a => a.Codigo!.Value).Order());
        Assert.Equal(conUno.Count, parcial.Total);
    }

    [Fact]
    public async Task BuscarAsync_PorcentajeYGuionBajo_NoSonComodines()
    {
        using var baseTemporal = new BaseTemporal();
        await Grabar(baseTemporal,
            ("P-1", "Descuento 10%"),
            ("P-2", "Cristal 100 unidades"),
            ("P-3", "Lente_A"),
            ("P-4", "Lente A"),
            ("P-5", "Lente BA"));
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        // Con LIKE, "10%" encontraría "100" y "nte_a" encontraría "Lente A" (D-05).
        Assert.Equal(["Descuento 10%"], await Descripciones(repositorio, "10%"));
        Assert.Equal(["Descuento 10%"], await Descripciones(repositorio, "%"));
        Assert.Equal(["Lente_A"], await Descripciones(repositorio, "nte_a"));
        Assert.Equal(["Lente_A"], await Descripciones(repositorio, "_"));
    }

    [Fact]
    public async Task BuscarAsync_Consulta_InstrConPlegarYParametro()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var registro = new ComandosCapturados();
            using (var servicios = BaseTemporal.CrearServicios(directorio, registro))
            {
                servicios.InicializarBaseDeDatos();
                using var alcance = servicios.CreateScope();
                var repositorio = new ArticuloRepository(alcance.ServiceProvider.GetRequiredService<OpticaDbContext>());
                while (registro.Comandos.TryDequeue(out _))
                {
                }

                await repositorio.BuscarAsync("zq%7_x", 1, 50);
            }

            // D-05 y ADR-011: instr sobre plegar, sin LIKE; el texto viaja como parámetro, no en el SQL.
            var sql = string.Join("\n", registro.Comandos);
            Assert.Contains("instr(\"plegar\"(\"a\".\"Descripcion\"), @", sql, StringComparison.Ordinal);
            Assert.Contains("instr(\"plegar\"(\"a\".\"CodigoProveedor\"), @", sql, StringComparison.Ordinal);
            Assert.Contains("instr(CAST(\"a\".\"Id\" AS TEXT), @", sql, StringComparison.Ordinal);
            Assert.Contains("ORDER BY \"orden_articulo\"(\"a\".\"Descripcion\"), \"a\".\"Id\"", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("LIKE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("zq%7_x", sql, StringComparison.Ordinal);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task BuscarAsync_PorCodigoYCodigoProveedor_CoincidenciaParcial()
    {
        using var baseTemporal = new BaseTemporal();
        var articulos = Enumerable.Range(0, 12)
            .Select(i => ($"PROV-{(char)('A' + i)}", $"Artículo {(char)('a' + i)}"))
            .ToArray();
        var codigos = await Grabar(baseTemporal, articulos);
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        // Por el código, su número decimal en forma parcial (D-06).
        var conUno = codigos.Where(c => c.ToString(CultureInfo.InvariantCulture).Contains('1', StringComparison.Ordinal))
            .Order()
            .ToList();
        Assert.True(conUno.Count > 1);
        var porCodigo = await repositorio.BuscarAsync("1", 1, 50);
        Assert.Equal(conUno.Count, porCodigo.Total);
        Assert.Equal(conUno, porCodigo.Articulos.Select(a => a.Codigo!.Value).Order());

        // Por el código en el proveedor, plegado y parcial.
        var porProveedor = await repositorio.BuscarAsync(PlegadoBusqueda.Plegar("ov-C"), 1, 50);
        Assert.Equal(["PROV-C"], porProveedor.Articulos.Select(a => a.CodigoProveedor));
        Assert.Equal(12, (await repositorio.BuscarAsync("prov", 1, 50)).Total);

        // Y por la descripción, sin el acento.
        Assert.Equal(12, (await repositorio.BuscarAsync(PlegadoBusqueda.Plegar("ARTICULO"), 1, 50)).Total);
    }

    [Fact]
    public async Task BuscarAsync_OrdenPorDescripcionYPaginasDe50()
    {
        using var baseTemporal = new BaseTemporal();

        // 120 artículos dados de alta en orden inverso a la descripción; cada descripción aparece dos veces, una en
        // mayúsculas, así el orden binario de la columna cruda no coincide con el esperado.
        var articulos = Enumerable.Range(0, 120)
            .Select(i => ($"C-{i:000}", (i % 2 == 0 ? "item " : "ITEM ") + ((119 - i) / 2).ToString("000", CultureInfo.InvariantCulture)))
            .ToArray();
        var codigos = await Grabar(baseTemporal, articulos);
        var esperado = articulos
            .Select((a, i) => (Codigo: codigos[i], Clave: a.Item2.ToLowerInvariant()))
            .OrderBy(a => a.Clave, StringComparer.Ordinal)
            .ThenBy(a => a.Codigo)
            .Select(a => a.Codigo)
            .ToList();
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        // D-01: de a 50, por descripción sin mayúsculas, con el código como desempate.
        var leidos = new List<long>();
        foreach (var (pagina, cantidad) in new[] { (1, 50), (2, 50), (3, 20) })
        {
            var resultado = await repositorio.BuscarAsync(null, pagina, 50);
            Assert.Equal(120, resultado.Total);
            Assert.Equal(cantidad, resultado.Articulos.Count);
            leidos.AddRange(resultado.Articulos.Select(a => a.Codigo!.Value));
        }

        Assert.Equal(esperado, leidos);

        var posterior = await repositorio.BuscarAsync(null, 4, 50);
        Assert.Empty(posterior.Articulos);
        Assert.Equal(120, posterior.Total);
    }

    [Fact]
    public async Task BuscarAsync_Enie_OrdenaEntreNyO()
    {
        using var baseTemporal = new BaseTemporal();
        await Grabar(baseTemporal,
            ("E-1", "ojo"),
            ("E-2", "\U000000D8lens"),
            ("E-3", "\U000000F1u"),
            ("E-4", "Zapato"),
            ("E-5", "\U000000D1and\U000000FA"),
            ("E-6", "Nzeta"),
            ("E-7", "nube"));
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        // D-15: la ñ entre la n y la o; las demás letras que no son ASCII, después de la z.
        var pagina = await repositorio.BuscarAsync(null, 1, 50);
        Assert.Equal(
            ["nube", "Nzeta", "\U000000D1and\U000000FA", "\U000000F1u", "ojo", "Zapato", "\U000000D8lens"],
            pagina.Articulos.Select(a => a.Descripcion));
    }

    /// <summary>
    /// Valores con dígitos y letras reconocibles: si alguno aparece en el error (mensaje o causa), un mensaje del
    /// dominio o del repositorio empezó a filtrar datos de la fila.
    /// </summary>
    private const string CodigoDistintivo = "QZ-90817";
    private const string DescripcionDistintiva = "Lenteñ-55213";
    private const long CostoDistintivoCentavos = 7319004; // 73.190,04
    private const long MargenDistintivoCentesimos = 4321; // 43,21 %
    private const long VentaDistintivaCentavos = 9876543; // 98.765,43

    [Theory]
    [InlineData("Descripcion = '   '", "   ")]
    [InlineData("CodigoProveedor = ''", "")]
    [InlineData("MargenCentesimos = 123457", "1234")] // 1.234,57 %, fuera de rango
    [InlineData("PrecioCostoCentavos = -9223372036854775808", "92233720368547758")]
    [InlineData("PrecioVentaCentavos = -9223372036854775808", "92233720368547758")]
    [InlineData("CodigoProveedorClave = 'CLAVE-ROTA-5309'", "CLAVE-ROTA-5309")]
    [InlineData("Version = -48213", "48213")]
    public async Task Obtener_FilaInvalida_LanzaErrorDeEstadoCorrupto(string asignacion, string valorCorrupto)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        Insertar(cadena, CodigoDistintivo, CodigoDistintivo, DescripcionDistintiva, CostoDistintivoCentavos,
            MargenDistintivoCentesimos, VentaDistintivaCentavos);
        var id = long.Parse(Assert.Single(Filas(cadena, SelectFila))[0], CultureInfo.InvariantCulture);

        // Una fila editada a mano con los CHECK desactivados. La conexión es propia y sin grupo, así el pragma no queda
        // activo en una conexión que después reutilice el contexto.
        var sinGrupo = new SqliteConnectionStringBuilder(cadena) { Pooling = false }.ToString();
        Ejecutar(sinGrupo, $"PRAGMA ignore_check_constraints = ON; UPDATE Articulo SET {asignacion};");
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        // Estado corrupto, no el ArgumentException de una entrada inválida; el mensaje no lleva valores.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.ObtenerAsync(id));
        Assert.Equal(MensajeFilaInvalida, error.Message);

        // Ni el mensaje ni la causa (todo lo que iría a un log) llevan los valores que siguen en la fila, ni el corrupto.
        AfirmarSinValoresDeLaFila(error);
        if (valorCorrupto.Trim().Length > 0)
        {
            Assert.DoesNotContain(valorCorrupto, error.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ObtenerPorClave_FilaInvalida_LanzaErrorDeEstadoCorrupto()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        Insertar(cadena, CodigoDistintivo, CodigoDistintivo, DescripcionDistintiva, CostoDistintivoCentavos,
            MargenDistintivoCentesimos, VentaDistintivaCentavos);
        Ejecutar(cadena, "UPDATE Articulo SET Descripcion = '   '");
        var repositorio = new ArticuloRepository(baseTemporal.CrearContexto());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repositorio.ObtenerPorClaveAsync(CodigoDistintivo));
        Assert.Equal(MensajeFilaInvalida, error.Message);
        AfirmarSinValoresDeLaFila(error);
    }

    /// <summary>
    /// El error completo (<see cref="Exception.ToString"/>: mensaje, causa y trazas) no contiene ninguno de los valores
    /// distintivos de la fila, en ninguna de las formas en que un mensaje podría escribirlos.
    /// </summary>
    private static void AfirmarSinValoresDeLaFila(Exception error)
    {
        var texto = error.ToString();
        string[] valores =
        [
            CodigoDistintivo, "90817", DescripcionDistintiva, "55213",
            "7319004", "73190", "4321", "43.21", "43,21", "9876543", "98765",
        ];
        foreach (var valor in valores)
        {
            Assert.DoesNotContain(valor, texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Agregar_ArticuloConCodigo_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);

        // Agregar es un alta: el código lo asigna la base.
        var error = Assert.Throws<ArgumentException>(
            () => repositorio.Agregar(new Articulo(7, "ABC-1", "Armazón", 1m, 0m, 1m, activo: true, version: 0)));

        Assert.Equal("articulo", error.ParamName);
        Assert.False(contexto.ChangeTracker.HasChanges());
    }

    [Fact]
    public void AgregarInfraestructura_RegistraRepositorioDeArticulos()
    {
        using var baseTemporal = new BaseTemporal();
        using var alcance = baseTemporal.Servicios.CreateScope();
        using var otroAlcance = baseTemporal.Servicios.CreateScope();

        var repositorio = alcance.ServiceProvider.GetRequiredService<IArticuloRepository>();

        // Scoped: uno por alcance, el mismo dentro del alcance.
        Assert.IsType<ArticuloRepository>(repositorio);
        Assert.Same(repositorio, alcance.ServiceProvider.GetRequiredService<IArticuloRepository>());
        Assert.NotSame(repositorio, otroAlcance.ServiceProvider.GetRequiredService<IArticuloRepository>());
    }

    private static void AfirmarCheck(SqliteException error, string restriccion)
    {
        Assert.Equal(SqliteRestriccionCheck, error.SqliteExtendedErrorCode);
        Assert.Contains("CHECK constraint failed: " + restriccion, error.Message, StringComparison.Ordinal);
    }

    private static string CadenaDe(BaseTemporal baseTemporal) =>
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion;

    /// <summary>Las columnas de la tabla de FEAT-002a, como "nombre tipo notnull" en orden alfabético.</summary>
    private static readonly string[] ColumnasFeat002a =
    [
        "CodigoProveedor TEXT 1", "CodigoProveedorClave TEXT 1", "Descripcion TEXT 1", "Id INTEGER 1",
        "MargenCentesimos INTEGER 1", "PrecioCostoCentavos INTEGER 1", "PrecioVentaCentavos INTEGER 1",
    ];

    private static List<string> Columnas(string cadena) =>
        Filas(cadena, "SELECT name, type, \"notnull\" FROM pragma_table_info('Articulo')")
            .OrderBy(c => c[0], StringComparer.Ordinal)
            .Select(c => string.Join(" ", c))
            .ToList();

    private static string SqlTablaArticulo(string cadena) =>
        Assert.Single(Filas(cadena, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Articulo'"))[0];

    /// <summary>Las propiedades marcadas como modificadas en las filas de artículo que sigue el contexto, en orden.</summary>
    private static List<string> Modificadas(OpticaDbContext contexto) =>
        contexto.ChangeTracker.Entries<ArticuloFila>()
            .SelectMany(e => e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Da de alta los artículos (costo 1.210, margen 50 %) y devuelve sus códigos, en el mismo orden.</summary>
    private static async Task<List<long>> Grabar(
        BaseTemporal baseTemporal, params (string Codigo, string Descripcion)[] articulos)
    {
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);
        foreach (var (codigo, descripcion) in articulos)
        {
            repositorio.Agregar(Articulo.Nuevo(codigo, descripcion, 1210m, 50m, 1815m));
        }

        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        var lector = new ArticuloRepository(baseTemporal.CrearContexto());
        var codigos = new List<long>();
        foreach (var (codigo, _) in articulos)
        {
            codigos.Add((await lector.ObtenerPorClaveAsync(NormalizacionCodigo.Clave(codigo)))!.Codigo!.Value);
        }

        return codigos;
    }

    private static async Task<List<string>> Descripciones(ArticuloRepository repositorio, string buscado)
    {
        var pagina = await repositorio.BuscarAsync(PlegadoBusqueda.Plegar(buscado), 1, 50);
        Assert.Equal(pagina.Articulos.Count, pagina.Total);
        return [.. pagina.Articulos.Select(a => a.Descripcion)];
    }

    /// <summary>El texto de los comandos que EF Core registra al ejecutarlos (sin valores de parámetros).</summary>
    private sealed class ComandosCapturados : ILoggerProvider
    {
        public ConcurrentQueue<string> Comandos { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Registrador(Comandos, categoryName);

        public void Dispose()
        {
        }

        private sealed class Registrador(ConcurrentQueue<string> comandos, string categoria) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (eventId.Id == RelationalEventId.CommandExecuted.Id
                    && categoria == DbLoggerCategory.Database.Command.Name)
                {
                    comandos.Enqueue(formatter(state, exception));
                }
            }
        }
    }

    private static void InsertarConEstado(string cadena, string codigo, long activo, long version)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText =
            "INSERT INTO Articulo (CodigoProveedor, CodigoProveedorClave, Descripcion, PrecioCostoCentavos, " +
            "MargenCentesimos, PrecioVentaCentavos, Activo, Version) " +
            "VALUES ($codigo, $codigo, 'Armazón', 100, 0, 100, $activo, $version)";
        comando.Parameters.AddWithValue("$codigo", codigo);
        comando.Parameters.AddWithValue("$activo", activo);
        comando.Parameters.AddWithValue("$version", version);
        comando.ExecuteNonQuery();
    }

    private static string TipoColumna(string cadena, string columna) =>
        Assert.Single(Filas(cadena, $"SELECT type FROM pragma_table_info('Articulo') WHERE name = '{columna}'"))[0];

    private static void Insertar(
        string cadena, string codigo, string clave, string descripcion, long costoCentavos, long margenCentesimos,
        long ventaCentavos)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText =
            "INSERT INTO Articulo (CodigoProveedor, CodigoProveedorClave, Descripcion, PrecioCostoCentavos, " +
            "MargenCentesimos, PrecioVentaCentavos) VALUES ($codigo, $clave, $descripcion, $costo, $margen, $venta)";
        comando.Parameters.AddWithValue("$codigo", codigo);
        comando.Parameters.AddWithValue("$clave", clave);
        comando.Parameters.AddWithValue("$descripcion", descripcion);
        comando.Parameters.AddWithValue("$costo", costoCentavos);
        comando.Parameters.AddWithValue("$margen", margenCentesimos);
        comando.Parameters.AddWithValue("$venta", ventaCentavos);
        comando.ExecuteNonQuery();
    }

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
