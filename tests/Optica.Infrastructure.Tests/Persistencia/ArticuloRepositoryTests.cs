using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

public class ArticuloRepositoryTests
{
    private const int SqliteRestriccionCheck = 275; // SQLITE_CONSTRAINT_CHECK
    private const int SqliteRestriccionUnica = 2067; // SQLITE_CONSTRAINT_UNIQUE

    private const string MensajeFilaInvalida = "El artículo guardado no es válido.";

    private const string SelectFila =
        "SELECT Id, CodigoProveedor, CodigoProveedorClave, Descripcion, PrecioCostoCentavos, MargenCentesimos, " +
        "PrecioVentaCentavos FROM Articulo ORDER BY Id";

    [Fact]
    public async Task Agregar_YConfirmar_AsignaCodigoAutonumerico()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);
        var unidad = new UnidadDeTrabajoEf(contexto);

        repositorio.Agregar(new Articulo(null, "ABC-1", "Armazón metálico", 1210m, 50m, 1815m));

        // Agregar solo registra el alta: sin confirmar, nada llega a la base.
        Assert.Empty(Filas(cadena, SelectFila));

        await unidad.ConfirmarAsync();
        repositorio.Agregar(new Articulo(null, "XYZ-9", "Cristal orgánico", 1000m, 50m, 1500m));
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
        repositorio.Agregar(new Articulo(null, "QRS-2", "Estuche", 10m, 0m, 10m));
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
        repositorio.Agregar(new Articulo(null, " abc-1 ", " Armazón ", 1210m, 50.25m, 1815m));
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
        repositorio.Agregar(new Articulo(null, "NEG-1", "Bonificación", -100m, 50m, -150m));
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
        new ArticuloRepository(contexto).Agregar(new Articulo(null, "ABC-1", "Armazón", 1210m, 50m, 1815m));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        // Otro alcance da de alta " abc-1 ": misma clave, la segunda barrera es el índice UNIQUE (AC-02, ADR-009).
        var otroContexto = baseTemporal.CrearContexto();
        new ArticuloRepository(otroContexto).Agregar(new Articulo(null, " abc-1 ", "Otro", 1m, 0m, 1m));

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
        repositorio.Agregar(new Articulo(null, "abc-1", "Armazón", 1m, 0m, 1m));
        repositorio.Agregar(new Articulo(null, "ñandú-1", "Estuche", 1m, 0m, 1m));
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
    public void Esquema_Articulo_ColumnasNotNullYAutoincrement()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        // Las columnas exactas, todas NOT NULL; sin columna de activo (D-08).
        var columnas = Filas(cadena, "SELECT name, type, \"notnull\" FROM pragma_table_info('Articulo')")
            .OrderBy(c => c[0], StringComparer.Ordinal)
            .Select(c => string.Join(" ", c));
        Assert.Equal(
            [
                "CodigoProveedor TEXT 1", "CodigoProveedorClave TEXT 1", "Descripcion TEXT 1", "Id INTEGER 1",
                "MargenCentesimos INTEGER 1", "PrecioCostoCentavos INTEGER 1", "PrecioVentaCentavos INTEGER 1",
            ],
            columnas);

        var sql = Assert.Single(Filas(cadena, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'Articulo'"))[0];
        Assert.Contains("AUTOINCREMENT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["1"], Assert.Single(Filas(cadena,
            "SELECT \"unique\" FROM pragma_index_list('Articulo') WHERE name = 'IX_Articulo_CodigoProveedorClave'")));
        Assert.Equal(["CodigoProveedorClave"], Assert.Single(Filas(cadena,
            "SELECT name FROM pragma_index_info('IX_Articulo_CodigoProveedorClave')")));
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
            () => repositorio.Agregar(new Articulo(7, "ABC-1", "Armazón", 1m, 0m, 1m)));

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
