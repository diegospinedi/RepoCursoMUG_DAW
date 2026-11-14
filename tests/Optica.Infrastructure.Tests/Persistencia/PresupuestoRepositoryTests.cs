using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Presupuestos sobre SQLite (Block 2 de FEAT-003a): tablas, migración, repositorio y numeración. Todos los datos de
/// clientes son ficticios (D-17).
/// </summary>
public partial class PresupuestoRepositoryTests
{
    private const int SqliteRestriccionCheck = 275; // SQLITE_CONSTRAINT_CHECK
    private const int SqliteRestriccionClaveForanea = 787; // SQLITE_CONSTRAINT_FOREIGNKEY

    private const string MensajeFilaInvalida = "El presupuesto guardado no es válido.";

    // Cliente ficticio. Los valores son distintivos para poder buscarlos en mensajes y en el log.
    private const string Apellido = "Ficticiez Quimera";
    private const string Nombre = "Prueboncia";
    private const string DniConPuntos = "23.456.789";
    private const string Dni = "23456789";
    private const string Domicilio = "Calle Inventada 4321";
    private const string Email = "prueboncia@ejemplo.com";
    private const string Telefono = "11 5555-0199";

    private static readonly DateOnly Fecha = new(2026, 3, 10);

    private const string SelectPresupuesto =
        "SELECT Numero, Fecha, Estado, Apellido, Nombre, Dni, Domicilio, Email, Telefono FROM Presupuesto ORDER BY Numero";

    private const string SelectLinea =
        "SELECT PresupuestoNumero, Orden, CodigoArticulo, Descripcion, PrecioUnitarioCentavos, " +
        "PrecioConDescuentoCentavos, PrecioFinalCentavos, Cantidad, DescuentoCentesimos FROM LineaPresupuesto " +
        "ORDER BY PresupuestoNumero, Orden";

    public static TheoryData<string> AsignacionesCorruptas => new()
    {
        // DNI que el dominio rechaza, y uno que normalizaría a otro valor ("1234567"): los dos son corruptos.
        "UPDATE Presupuesto SET Dni = '123456'",
        "UPDATE Presupuesto SET Dni = '12.345.67'",
        "UPDATE Presupuesto SET Dni = '2345678A'",
        "UPDATE Presupuesto SET Estado = 'anulado'",
        "UPDATE Presupuesto SET Estado = 'Borrador'",
        // TR_Presupuesto_FechaYNumeroFijos rechaza el UPDATE de la fecha (FEAT-003c): la fila se vuelve a insertar.
        "CREATE TEMP TABLE p AS SELECT * FROM Presupuesto; CREATE TEMP TABLE l AS SELECT * FROM LineaPresupuesto; " +
        "DELETE FROM LineaPresupuesto; DELETE FROM Presupuesto; UPDATE p SET Fecha = '2026-02-30'; " +
        "INSERT INTO Presupuesto SELECT * FROM p; INSERT INTO LineaPresupuesto SELECT * FROM l",
        "UPDATE Presupuesto SET Apellido = ' Ficticiez Quimera'",
        "UPDATE Presupuesto SET Nombre = ''",
        "UPDATE Presupuesto SET Email = 'prueboncia-sin-arroba'",
        "UPDATE Presupuesto SET Domicilio = ''",
        "UPDATE LineaPresupuesto SET PrecioUnitarioCentavos = -1",
        "UPDATE LineaPresupuesto SET PrecioFinalCentavos = -185000",
        "UPDATE LineaPresupuesto SET Cantidad = 0",
        "UPDATE LineaPresupuesto SET Cantidad = 3000000000",
        "UPDATE LineaPresupuesto SET DescuentoCentesimos = 10001",
        "UPDATE LineaPresupuesto SET Descripcion = ' Armazón metálico'",
        "DELETE FROM LineaPresupuesto",
    };

    [Theory]
    [MemberData(nameof(AsignacionesCorruptas))]
    public async Task ObtenerAsync_FilaCorrupta_EsEstadoCorrupto(string sentencia)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticulo(baseTemporal, "ARM-1", "Armazón metálico", 1850m);
        InsertarCrudo(cadena, "Presupuesto", ValoresPresupuesto(numero: 7));
        InsertarCrudo(cadena, "LineaPresupuesto", ValoresLinea(numero: 7, orden: 1, codigo));

        // La fila válida se lee; la misma fila con un valor que eludió los CHECK es estado corrupto.
        Assert.NotNull(await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(7));
        var sinGrupo = new SqliteConnectionStringBuilder(cadena) { Pooling = false }.ToString();
        Ejecutar(sinGrupo, $"PRAGMA ignore_check_constraints = ON; {sentencia};");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(7));

        Assert.Equal(MensajeFilaInvalida, error.Message);
        AfirmarSinDatosDelCliente(error.ToString());
        Assert.DoesNotContain("123456", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("anulado", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("2026-02-30", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Agregar_SinNumero_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);

        Assert.Throws<ArgumentNullException>(() => repositorio.Agregar(null!));

        // El dominio no deja construir un presupuesto sin número o sin líneas: el repositorio igual lo verifica, por si
        // llega uno armado sin constructor.
        var sinNumero = (Presupuesto)RuntimeHelpers.GetUninitializedObject(typeof(Presupuesto));
        var errorNumero = Assert.Throws<ArgumentException>(() => repositorio.Agregar(sinNumero));
        Assert.Equal("presupuesto", errorNumero.ParamName);

        // Con número y cliente, sin líneas: el rechazo sale por las líneas.
        var sinLineas = (Presupuesto)RuntimeHelpers.GetUninitializedObject(typeof(Presupuesto));
        AsignarPropiedad(sinLineas, nameof(Presupuesto.Numero), 5L);
        AsignarPropiedad(sinLineas, nameof(Presupuesto.Cliente), ClienteCompleto());
        var errorLineas = Assert.Throws<ArgumentException>(() => repositorio.Agregar(sinLineas));
        Assert.Equal("presupuesto", errorLineas.ParamName);

        // Con número y líneas, sin cliente.
        var sinCliente = (Presupuesto)RuntimeHelpers.GetUninitializedObject(typeof(Presupuesto));
        AsignarPropiedad(sinCliente, nameof(Presupuesto.Numero), 5L);
        AsignarPropiedad(sinCliente, nameof(Presupuesto.Lineas),
            (IReadOnlyList<LineaPresupuesto>)[new LineaPresupuesto(1, "Armazón", 10m, 1, 0m, 10m, 10m)]);
        var errorCliente = Assert.Throws<ArgumentException>(() => repositorio.Agregar(sinCliente));
        Assert.Equal("presupuesto", errorCliente.ParamName);

        // Mensajes fijos, sin valores del cliente.
        AfirmarSinDatosDelCliente(errorNumero.Message + errorLineas.Message + errorCliente.Message);
        Assert.False(contexto.ChangeTracker.HasChanges());
    }

    /// <summary>Asigna una propiedad de solo lectura por su campo generado; falla con un mensaje claro si no existe.</summary>
    private static void AsignarPropiedad(Presupuesto presupuesto, string propiedad, object valor)
    {
        var campo = typeof(Presupuesto)
            .GetField($"<{propiedad}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(campo);
        campo.SetValue(presupuesto, valor);
    }

    [Fact]
    public async Task Confirmar_NumeroRepetido_ConflictoDeUnicidad()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(7, Fecha, ClienteCompleto(), [Linea(articulo)]));

        // Otra grabación con el mismo número: la clave primaria es la segunda barrera de la numeración (D-07).
        var contexto = baseTemporal.CrearContexto();
        var otroCliente = new ClientePresupuesto("Otroapellido", "Otronombre", "30111222", null, null, null);
        new PresupuestoRepository(contexto).Agregar(Presupuesto.Nuevo(7, Fecha, otroCliente, [Linea(articulo)]));
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("Otroapellido", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("30111222", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Equal(Apellido, Assert.Single(Filas(cadena, SelectPresupuesto))[3]);
        Assert.Single(Filas(cadena, SelectLinea));
    }

    [Fact]
    public async Task Confirmar_ArticuloInexistente_FallaPorClaveForanea()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var registro = new RegistroCapturado();
            using var servicios = BaseTemporal.CrearServicios(directorio, registro);
            servicios.InicializarBaseDeDatos();
            var cadena = servicios.GetRequiredService<FabricaConexion>().CadenaConexion;
            using var alcance = servicios.CreateScope();
            var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
            var articulos = new ArticuloRepository(contexto);
            articulos.Agregar(Articulo.Nuevo("ARM-1", "Armazón", 1850m, 0m, 1850m));
            await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
            var codigo = (await articulos.ObtenerPorClaveAsync("ARM-1"))!.Codigo!.Value;
            while (registro.Mensajes.TryDequeue(out _))
            {
            }

            // Una línea con un código que no está en el catálogo: la clave foránea la rechaza y no es un conflicto.
            var huerfana = new LineaPresupuesto(codigo + 1000, "Artículo que no existe", 10m, 1, 0m, 10m, 10m);
            new PresupuestoRepository(contexto).Agregar(Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [huerfana]));
            var error = await Assert.ThrowsAsync<DbUpdateException>(
                () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

            var sqlite = Assert.IsType<SqliteException>(error.InnerException);
            Assert.Equal(SqliteRestriccionClaveForanea, sqlite.SqliteExtendedErrorCode);
            Assert.Empty(Filas(cadena, SelectPresupuesto));
            Assert.Empty(Filas(cadena, SelectLinea));

            // D-17: ni la excepción ni lo que EF registró en el camino fallido llevan datos del cliente, y lo pendiente se
            // descartó del contexto.
            AfirmarSinDatosDelCliente(error.ToString());
            Assert.Contains(registro.Mensajes, m => m.Contains("INSERT INTO \"Presupuesto\"", StringComparison.Ordinal));
            AfirmarSinDatosDelCliente(string.Join("\n", registro.Mensajes));
            Assert.Empty(contexto.ChangeTracker.Entries());
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    public static TheoryData<string, string, object, string> ValoresFueraDeCheck => new()
    {
        { "Presupuesto", "Dni", "123456", "CK_Presupuesto_Dni" },
        { "Presupuesto", "Dni", "123456789", "CK_Presupuesto_Dni" },
        { "Presupuesto", "Dni", "2345678A", "CK_Presupuesto_Dni" },
        { "Presupuesto", "Dni", "12.34567", "CK_Presupuesto_Dni" },
        { "Presupuesto", "Estado", "anulado", "CK_Presupuesto_Estado" },
        { "Presupuesto", "Numero", 0L, "CK_Presupuesto_Numero" },
        { "Presupuesto", "Fecha", "10/03/2026", "CK_Presupuesto_Fecha" },
        { "Presupuesto", "Apellido", "", "CK_Presupuesto_Apellido" },
        { "Presupuesto", "Nombre", new string('n', 101), "CK_Presupuesto_Nombre" },
        { "Presupuesto", "Domicilio", new string('d', 201), "CK_Presupuesto_Domicilio" },
        { "Presupuesto", "Email", new string('e', 255), "CK_Presupuesto_Email" },
        { "Presupuesto", "Telefono", new string('1', 31), "CK_Presupuesto_Telefono" },
        { "Presupuesto", "Version", -1L, "CK_Presupuesto_Version" },
        { "Presupuesto", "Version", 9_007_199_254_740_992L, "CK_Presupuesto_Version" },
        { "LineaPresupuesto", "Cantidad", 0L, "CK_LineaPresupuesto_Cantidad" },
        { "LineaPresupuesto", "PrecioUnitarioCentavos", -1L, "CK_LineaPresupuesto_PrecioUnitarioCentavos" },
        { "LineaPresupuesto", "PrecioConDescuentoCentavos", -1L, "CK_LineaPresupuesto_PrecioConDescuentoCentavos" },
        { "LineaPresupuesto", "PrecioFinalCentavos", -1L, "CK_LineaPresupuesto_PrecioFinalCentavos" },
        { "LineaPresupuesto", "DescuentoCentesimos", 10001L, "CK_LineaPresupuesto_DescuentoCentesimos" },
        { "LineaPresupuesto", "DescuentoCentesimos", -1L, "CK_LineaPresupuesto_DescuentoCentesimos" },
        { "LineaPresupuesto", "Orden", 0L, "CK_LineaPresupuesto_Orden" },
        { "LineaPresupuesto", "Descripcion", "", "CK_LineaPresupuesto_Descripcion" },
    };

    [Theory]
    [MemberData(nameof(ValoresFueraDeCheck))]
    public async Task Esquema_ChecksRechazanValoresInvalidos(string tabla, string columna, object valor, string restriccion)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m);
        var valores = tabla == "Presupuesto" ? ValoresPresupuesto(numero: 1) : ValoresLinea(numero: 1, orden: 1, codigo);
        if (tabla == "LineaPresupuesto")
        {
            InsertarCrudo(cadena, "Presupuesto", ValoresPresupuesto(numero: 1));
        }

        valores[columna] = valor;
        var error = Assert.Throws<SqliteException>(() => InsertarCrudo(cadena, tabla, valores));

        Assert.Equal(SqliteRestriccionCheck, error.SqliteExtendedErrorCode);
        Assert.Contains("CHECK constraint failed: " + restriccion, error.Message, StringComparison.Ordinal);
        Assert.Empty(Filas(cadena, SelectLinea));
    }

    [Fact]
    public async Task ObtenerUltimoNumero_Con154_Devuelve154()
    {
        using var baseTemporal = new BaseTemporal();
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(154, Fecha, ClienteCompleto(), [Linea(articulo)]));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(20, Fecha, ClienteCompleto(), [Linea(articulo)]));

        // El máximo, no el último grabado: AC-09 asigna el 155.
        var ultimo = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerUltimoNumeroAsync();

        Assert.Equal(154L, ultimo);
        Assert.Equal(155L, NumeracionPresupuesto.Siguiente(ultimo));
    }

    [Fact]
    public async Task ObtenerUltimoNumero_SinPresupuestos_Null()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m);

        // Una base con las migraciones aplicadas y la tabla de presupuestos vacía.
        Assert.Contains("Presupuesto", Tablas(cadena));
        Assert.Empty(Filas(cadena, SelectPresupuesto));

        var ultimo = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerUltimoNumeroAsync();

        // AC-10: el primero lleva el número 1.
        Assert.Null(ultimo);
        Assert.Equal(1L, NumeracionPresupuesto.Siguiente(ultimo));
    }

    [Fact]
    public async Task Agregar_SeisDatosYUnaLinea_SeLeeIgual()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticulo(baseTemporal, "ARM-1", "Armazón metálico", 1850m);
        var articulo = await LeerArticulo(baseTemporal, codigo);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);

        repositorio.Agregar(Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));

        // Agregar solo registra el alta: sin confirmar, nada llega a la base.
        Assert.Empty(Filas(cadena, SelectPresupuesto));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        // AC-01: los seis datos, leídos desde otro alcance. AC-17: los siete datos de la línea.
        var lectorContexto = baseTemporal.CrearContexto();
        var leido = await new PresupuestoRepository(lectorContexto).ObtenerAsync(1);
        Assert.NotNull(leido);
        Assert.Equal(1L, leido.Numero);
        Assert.Equal(Fecha, leido.Fecha);
        Assert.Equal(EstadoPresupuesto.Borrador, leido.Estado);
        Assert.Equal(Apellido, leido.Cliente.Apellido);
        Assert.Equal(Nombre, leido.Cliente.Nombre);
        Assert.Equal(Dni, leido.Cliente.Dni);
        Assert.Equal(Domicilio, leido.Cliente.Domicilio);
        Assert.Equal(Email, leido.Cliente.Email);
        Assert.Equal(Telefono, leido.Cliente.Telefono);
        var linea = Assert.Single(leido.Lineas);
        Assert.Equal(codigo, linea.CodigoArticulo);
        Assert.Equal("Armazón metálico", linea.Descripcion);
        Assert.Equal(1850m, linea.PrecioUnitario);
        Assert.Equal(1, linea.Cantidad);
        Assert.Equal(0m, linea.PorcentajeDescuento);
        Assert.Equal(1850m, linea.PrecioConDescuento);
        Assert.Equal(1850m, linea.PrecioFinal);

        // Sin seguimiento, y un número que no existe da null.
        Assert.Empty(lectorContexto.ChangeTracker.Entries());
        Assert.Null(await new PresupuestoRepository(lectorContexto).ObtenerAsync(2));
        Assert.Null(await new PresupuestoRepository(lectorContexto).ObtenerAsync(long.MinValue));
    }

    [Fact]
    public async Task Agregar_VariasLineas_GuardaOrdenYLeeEnElOrdenRecibido()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var primero = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        var segundo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "EST-1", "Estuche", 300m));

        // El orden de carga, no el del código; el mismo artículo puede repetirse (D-16).
        await GrabarPresupuesto(baseTemporal,
            Presupuesto.Nuevo(3, Fecha, ClienteCompleto(), [Linea(segundo), Linea(primero), Linea(segundo)]));

        Assert.Equal(
            [
                ["3", "1", segundo.Codigo!.Value.ToString(CultureInfo.InvariantCulture)],
                ["3", "2", primero.Codigo!.Value.ToString(CultureInfo.InvariantCulture)],
                ["3", "3", segundo.Codigo!.Value.ToString(CultureInfo.InvariantCulture)],
            ],
            Filas(cadena, SelectLinea).Select(f => f[..3]));
        var leido = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(3);
        Assert.Equal(["Estuche", "Armazón", "Estuche"], leido!.Lineas.Select(l => l.Descripcion));
    }

    [Fact]
    public async Task Agregar_SinOpcionales_GuardaNull()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));

        // AC-04: sin Domicilio, Email ni Teléfono; los vacíos o de espacios cuentan como ausentes (D-11).
        var cliente = new ClientePresupuesto(Apellido, Nombre, Dni, null, "   ", string.Empty);
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, cliente, [Linea(articulo)]));

        Assert.Equal(["1", "1", "1"], Assert.Single(Filas(cadena,
            "SELECT Domicilio IS NULL, Email IS NULL, Telefono IS NULL FROM Presupuesto")));
        var leido = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(1);
        Assert.Null(leido!.Cliente.Domicilio);
        Assert.Null(leido.Cliente.Email);
        Assert.Null(leido.Cliente.Telefono);
    }

    [Fact]
    public async Task Agregar_Dni23456789_GuardaSinPuntos()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));

        // AC-05: "23.456.789" se guarda 23456789, como texto; un DNI con 0 adelante conserva sus dígitos (D-09).
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        var conCero = new ClientePresupuesto(Apellido, Nombre, "01.234.567", null, null, null);
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(2, Fecha, conCero, [Linea(articulo)]));

        Assert.Equal([["23456789", "text"], ["01234567", "text"]],
            Filas(cadena, "SELECT Dni, typeof(Dni) FROM Presupuesto ORDER BY Numero"));
        var lector = new PresupuestoRepository(baseTemporal.CrearContexto());
        Assert.Equal(Dni, (await lector.ObtenerAsync(1))!.Cliente.Dni);
        Assert.Equal("01234567", (await lector.ObtenerAsync(2))!.Cliente.Dni);
    }

    [Fact]
    public async Task Agregar_Borrador_GuardaBorrador()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));

        // AC-12: el presupuesto nuevo queda en Borrador. El estado Final se guarda como 'final' (D-19); se graba en
        // Borrador y después pasa a Final, como en la aplicación (AGENTS.md).
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        await GrabarFinal(baseTemporal, Presupuesto.Nuevo(2, Fecha, ClienteCompleto(), [Linea(articulo)]));

        Assert.Equal([["borrador"], ["final"]], Filas(cadena, "SELECT Estado FROM Presupuesto ORDER BY Numero"));
        var lector = new PresupuestoRepository(baseTemporal.CrearContexto());
        Assert.Equal(EstadoPresupuesto.Borrador, (await lector.ObtenerAsync(1))!.Estado);
        Assert.Equal(EstadoPresupuesto.Final, (await lector.ObtenerAsync(2))!.Estado);
    }

    [Fact]
    public async Task Agregar_Linea1850_GuardaCentavosCantidad1YDescuento0()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticulo(baseTemporal, "ARM-1", "Armazón metálico", 1850m);
        var articulo = await LeerArticulo(baseTemporal, codigo);

        // AC-18: Cantidad 1, Descuento 0, Precio con Descuento y Precio Final iguales al Precio Unitario, en centésimos.
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));

        Assert.Equal(
            ["1", "1", codigo.ToString(CultureInfo.InvariantCulture), "Armazón metálico", "185000", "185000", "185000", "1", "0"],
            Assert.Single(Filas(cadena, SelectLinea)));
        Assert.Equal(
            ["integer", "integer", "integer", "integer", "integer"],
            Assert.Single(Filas(cadena,
                "SELECT typeof(PrecioUnitarioCentavos), typeof(PrecioConDescuentoCentavos), typeof(PrecioFinalCentavos), " +
                "typeof(Cantidad), typeof(DescuentoCentesimos) FROM LineaPresupuesto")));
    }

    /// <summary>
    /// FEAT-003b: Cantidad 3 y 10 % sobre 1.000,00 se guardan en centésimos (descuento 1000, 90000 y 270000) y se leen
    /// igual, con el total del dominio en 2.700,00.
    /// </summary>
    [Fact]
    public async Task Repositorio_LeeCantidadYDescuento()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticulo(baseTemporal, "LEN-1", "Lente bifocal", 1000m);
        var articulo = await LeerArticulo(baseTemporal, codigo);
        var resultado = LineaPresupuesto.DesdeArticulo(articulo, cantidad: 3, porcentajeDescuento: 10m);
        Assert.True(resultado.EsValida);

        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [resultado.Linea]));

        Assert.Equal(
            ["1", "1", codigo.ToString(CultureInfo.InvariantCulture), "Lente bifocal", "100000", "90000", "270000", "3",
                "1000"],
            Assert.Single(Filas(cadena, SelectLinea)));
        var leido = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(1);
        var linea = Assert.Single(leido!.Lineas);
        Assert.Equal((3, 10m, 900m, 2700m),
            (linea.Cantidad, linea.PorcentajeDescuento, linea.PrecioConDescuento, linea.PrecioFinal));
        Assert.Equal(2700m, leido.Total);
    }

    [Fact]
    public async Task Agregar_Fecha10_03_2026_GuardaTextoYLeeIgual()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));

        // AC-19: la fecha sin hora, como TEXT yyyy-MM-dd.
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));

        Assert.Equal(["2026-03-10", "text"], Assert.Single(Filas(cadena, "SELECT Fecha, typeof(Fecha) FROM Presupuesto")));
        var leido = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(1);
        Assert.Equal(new DateOnly(2026, 3, 10), leido!.Fecha);
    }

    [Fact]
    public async Task Agregar_ComandosRegistrados_SinDatosDelCliente()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var registro = new RegistroCapturado();
            using (var servicios = BaseTemporal.CrearServicios(directorio, registro))
            {
                servicios.InicializarBaseDeDatos();
                using var alcance = servicios.CreateScope();
                var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
                var articulos = new ArticuloRepository(contexto);
                articulos.Agregar(Articulo.Nuevo("ARM-1", "Armazón", 1850m, 0m, 1850m));
                await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
                var articulo = (await articulos.ObtenerPorClaveAsync("ARM-1"))!;
                var repositorio = new PresupuestoRepository(contexto);

                repositorio.Agregar(Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
                await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
                await repositorio.ObtenerAsync(1);
            }

            // D-17: EF registra el SQL con los nombres de los parámetros, sin sus valores.
            var texto = string.Join("\n", registro.Mensajes);
            Assert.Contains("INSERT INTO \"Presupuesto\"", texto, StringComparison.Ordinal);
            Assert.Contains("FROM \"Presupuesto\"", texto, StringComparison.Ordinal);
            AfirmarSinDatosDelCliente(texto);
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public void AgregarInfraestructura_RegistraRepositorioDePresupuestos()
    {
        using var baseTemporal = new BaseTemporal();
        using var alcance = baseTemporal.Servicios.CreateScope();
        using var otroAlcance = baseTemporal.Servicios.CreateScope();

        var repositorio = alcance.ServiceProvider.GetService<IPresupuestoRepository>();

        // Scoped: uno por alcance, el mismo dentro del alcance, y comparte el contexto con la unidad de trabajo.
        Assert.IsType<PresupuestoRepository>(repositorio);
        Assert.Same(repositorio, alcance.ServiceProvider.GetService<IPresupuestoRepository>());
        Assert.NotSame(repositorio, otroAlcance.ServiceProvider.GetService<IPresupuestoRepository>());
    }

    [Fact]
    public void Migracion_Presupuestos_NoTocaArticuloNiSuSecuencia()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var migrador = contexto.GetService<IMigrator>();
        // Por nombre, no por posición (D-29 de FEAT-007).
        var migraciones = contexto.Database.GetMigrations().ToList();
        var presupuestos = Migracion(migraciones, "_Presupuestos");
        var anterior = Migracion(migraciones, "_CatalogoActivo");
        Assert.Equal(migraciones.IndexOf(anterior) + 1, migraciones.IndexOf(presupuestos));

        // ADR-012: el Up solo crea tablas e índices nuevos, nada sobre Articulo; el Down borra las líneas y después
        // los presupuestos.
        var ensamblado = contexto.GetService<IMigrationsAssembly>();
        var migracion = ensamblado.CreateMigration(ensamblado.Migrations[presupuestos], contexto.Database.ProviderName!);
        Assert.All(migracion.UpOperations, operacion =>
            Assert.True(operacion is CreateTableOperation or CreateIndexOperation, operacion.GetType().Name));
        Assert.Equal(["Presupuesto", "LineaPresupuesto"],
            migracion.UpOperations.OfType<CreateTableOperation>().Select(t => t.Name));
        Assert.All(migracion.UpOperations.OfType<CreateIndexOperation>(),
            indice => Assert.Equal("LineaPresupuesto", indice.Table));
        Assert.All(migracion.DownOperations, operacion => Assert.IsType<DropTableOperation>(operacion));
        Assert.Equal(["LineaPresupuesto", "Presupuesto"],
            migracion.DownOperations.OfType<DropTableOperation>().Select(t => t.Name));

        // Una base de FEAT-002b con artículos, uno de ellos borrado.
        migrador.Migrate(anterior);
        Assert.DoesNotContain("Presupuesto", Tablas(cadena));
        InsertarArticulo(cadena, "ABC-1", "Armazón");
        InsertarArticulo(cadena, "ABC-2", "Estuche");
        InsertarArticulo(cadena, "ABC-3", "Borrado");
        Ejecutar(cadena, "DELETE FROM Articulo WHERE CodigoProveedor = 'ABC-3'");
        Ejecutar(cadena, "UPDATE Articulo SET Activo = 0, Version = 4 WHERE CodigoProveedor = 'ABC-2'");
        var foto = FotoArticulo(cadena);
        Assert.Equal([["3"]], Filas(cadena, "SELECT seq FROM sqlite_sequence WHERE name = 'Articulo'"));

        // Aplicarla deja Articulo igual: datos, SQL de la tabla, índices, triggers y secuencia.
        migrador.Migrate();
        Assert.Contains("Presupuesto", Tablas(cadena));
        Assert.Contains("LineaPresupuesto", Tablas(cadena));
        Assert.Equal(foto, FotoArticulo(cadena));

        // Revertirla también, y quita las dos tablas.
        migrador.Migrate(anterior);
        Assert.DoesNotContain("Presupuesto", Tablas(cadena));
        Assert.DoesNotContain("LineaPresupuesto", Tablas(cadena));
        Assert.Equal(foto, FotoArticulo(cadena));

        migrador.Migrate();
        Assert.Empty(contexto.Database.GetPendingMigrations());
        Assert.Equal(foto, FotoArticulo(cadena));

        // El código 3, usado y borrado, no se reutiliza.
        InsertarArticulo(cadena, "ABC-4", "Nuevo");
        Assert.Equal([["4"]], Filas(cadena, "SELECT Id FROM Articulo WHERE CodigoProveedor = 'ABC-4'"));
    }

    [Fact]
    public void Esquema_Presupuesto_ColumnasNotNullChecksYForeignKeys()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        // "nombre tipo notnull pk default", por nombre. Sin DEFAULT: el repositorio asigna todas las columnas. Version
        // (FEAT-003c) lleva DEFAULT 0 solo por el ADD COLUMN, para las filas que ya estaban (D-21 de FEAT-002b).
        Assert.Equal(
            [
                "Apellido TEXT 1 0 ", "Dni TEXT 1 0 ", "Domicilio TEXT 0 0 ", "Email TEXT 0 0 ", "Estado TEXT 1 0 ",
                "Fecha TEXT 1 0 ", "Nombre TEXT 1 0 ", "Numero INTEGER 1 1 ", "Telefono TEXT 0 0 ", "Version INTEGER 1 0 0",
            ],
            Columnas(cadena, "Presupuesto"));
        Assert.Equal(
            [
                "Cantidad INTEGER 1 0 ", "CodigoArticulo INTEGER 1 0 ", "Descripcion TEXT 1 0 ",
                "DescuentoCentesimos INTEGER 1 0 ", "Orden INTEGER 1 2 ", "PrecioConDescuentoCentavos INTEGER 1 0 ",
                "PrecioFinalCentavos INTEGER 1 0 ", "PrecioUnitarioCentavos INTEGER 1 0 ", "PresupuestoNumero INTEGER 1 1 ",
            ],
            Columnas(cadena, "LineaPresupuesto"));

        // Numero es INTEGER PRIMARY KEY sin AUTOINCREMENT (ADR-015): no hay secuencia que reiniciar.
        var sqlPresupuesto = SqlTabla(cadena, "Presupuesto");
        var sqlLinea = SqlTabla(cadena, "LineaPresupuesto");
        Assert.DoesNotContain("AUTOINCREMENT", sqlPresupuesto, StringComparison.OrdinalIgnoreCase);
        // El único DEFAULT de Presupuesto es el de Version, agregada con ADD COLUMN al final (ADR-012).
        Assert.Equal(1, Regex.Count(sqlPresupuesto, "DEFAULT", RegexOptions.IgnoreCase));
        Assert.Contains(
            ", \"Version\" INTEGER NOT NULL DEFAULT 0 CONSTRAINT \"CK_Presupuesto_Version\" CHECK",
            sqlPresupuesto, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT", sqlLinea, StringComparison.OrdinalIgnoreCase);

        // Un CHECK con nombre por columna, con la expresión de la spec.
        Assert.Equal(
            [
                ("CK_Presupuesto_Apellido", "length(\"Apellido\") BETWEEN 1 AND 100"),
                ("CK_Presupuesto_Dni", "length(\"Dni\") BETWEEN 7 AND 8 AND \"Dni\" NOT GLOB '*[^0-9]*'"),
                ("CK_Presupuesto_Domicilio", "length(\"Domicilio\") <= 200"),
                ("CK_Presupuesto_Email", "length(\"Email\") <= 254"),
                ("CK_Presupuesto_Estado", "\"Estado\" IN ('borrador', 'final')"),
                ("CK_Presupuesto_Fecha", "\"Fecha\" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'"),
                ("CK_Presupuesto_Nombre", "length(\"Nombre\") BETWEEN 1 AND 100"),
                ("CK_Presupuesto_Numero", "\"Numero\" >= 1"),
                ("CK_Presupuesto_Telefono", "length(\"Telefono\") <= 30"),
                ("CK_Presupuesto_Version", "\"Version\" BETWEEN 0 AND 9007199254740991"),
            ],
            Checks(sqlPresupuesto));
        Assert.Equal(
            [
                ("CK_LineaPresupuesto_Cantidad", "\"Cantidad\" >= 1"),
                ("CK_LineaPresupuesto_Descripcion", "length(\"Descripcion\") BETWEEN 1 AND 200"),
                ("CK_LineaPresupuesto_DescuentoCentesimos", "\"DescuentoCentesimos\" BETWEEN 0 AND 10000"),
                ("CK_LineaPresupuesto_Orden", "\"Orden\" >= 1"),
                ("CK_LineaPresupuesto_PrecioConDescuentoCentavos", "\"PrecioConDescuentoCentavos\" >= 0"),
                ("CK_LineaPresupuesto_PrecioFinalCentavos", "\"PrecioFinalCentavos\" >= 0"),
                ("CK_LineaPresupuesto_PrecioUnitarioCentavos", "\"PrecioUnitarioCentavos\" >= 0"),
            ],
            Checks(sqlLinea));

        // Dos claves foráneas con ON DELETE RESTRICT, ninguna en Presupuesto.
        Assert.Equal(
            [
                ["Articulo", "CodigoArticulo", "Id", "RESTRICT"],
                ["Presupuesto", "PresupuestoNumero", "Numero", "RESTRICT"],
            ],
            Filas(cadena,
                "SELECT \"table\", \"from\", \"to\", on_delete FROM pragma_foreign_key_list('LineaPresupuesto') " +
                "ORDER BY \"from\""));
        Assert.Empty(Filas(cadena, "SELECT * FROM pragma_foreign_key_list('Presupuesto')"));
        Assert.Equal([["1"]], Filas(cadena, "PRAGMA foreign_keys"));

        // El índice de la clave foránea al catálogo, no único.
        Assert.Equal([["IX_LineaPresupuesto_CodigoArticulo", "0"]],
            Filas(cadena,
                "SELECT name, \"unique\" FROM pragma_index_list('LineaPresupuesto') WHERE name LIKE 'IX%'"));
        Assert.Equal([["CodigoArticulo"]],
            Filas(cadena, "SELECT name FROM pragma_index_info('IX_LineaPresupuesto_CodigoArticulo')"));
        Assert.Empty(Filas(cadena, "SELECT name FROM pragma_index_list('Presupuesto') WHERE name LIKE 'IX%'"));

        // Los seis triggers de cierre de FEAT-003c (ADR-017), sobre la tabla que protege cada uno.
        Assert.Equal(TriggersDeCierre, Triggers(cadena));
    }

    /// <summary>Los triggers de cierre de la migración <c>CierrePresupuestos</c>, como (tabla, nombre), ordenados.</summary>
    private static readonly List<string[]> TriggersDeCierre =
    [
        ["LineaPresupuesto", "TR_LineaPresupuesto_FinalDelete"],
        ["LineaPresupuesto", "TR_LineaPresupuesto_FinalInsert"],
        ["LineaPresupuesto", "TR_LineaPresupuesto_FinalUpdate"],
        ["Presupuesto", "TR_Presupuesto_FechaYNumeroFijos"],
        ["Presupuesto", "TR_Presupuesto_FinalInmutable"],
        ["Presupuesto", "TR_Presupuesto_FinalNoSeBorra"],
    ];

    private static List<string[]> Triggers(string cadena) =>
        Filas(cadena,
            "SELECT tbl_name, name FROM sqlite_master WHERE type = 'trigger' " +
            "AND tbl_name IN ('Presupuesto', 'LineaPresupuesto') ORDER BY tbl_name, name");

    // ---- FEAT-003c: modificación, paso a Final, triggers de cierre y migración CierrePresupuestos ----

    private const int SqliteRestriccionTrigger = 1811; // SQLITE_CONSTRAINT_TRIGGER

    private const string SelectEncabezado =
        "SELECT Numero, Fecha, Estado, Apellido, Nombre, Dni, Domicilio, Email, Telefono, Version FROM Presupuesto " +
        "ORDER BY Numero";

    private const string MensajeEntradaConOtrosCambios =
        "El presupuesto ya tiene otros cambios registrados en el contexto.";

    // Otro cliente ficticio, para la modificación: DNI y email claramente inventados.
    private const string OtroApellido = "Otroapellido";
    private const string OtroNombre = "Otronombre";
    private const string OtroDni = "99999998";
    private const string OtroEmail = "otro.cliente@example.com";

    [Fact]
    public async Task Actualizar_Borrador_MenosLineas_ReemplazaLineasEIncrementaVersion()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var armazon = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        var estuche = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "EST-1", "Estuche", 300m));
        var lente = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "LEN-1", "Lente", 1000m));
        await GrabarPresupuesto(baseTemporal,
            Presupuesto.Nuevo(5, Fecha, ClienteCompleto(), [Linea(armazon), Linea(estuche), Linea(lente)]));

        // AC-02, AC-03: otro cliente; queda el lente primero y el armazón con cantidad 2 y 10 %, sin el estuche.
        await ModificarAsync(baseTemporal, 5, original =>
        {
            var armazonModificado = original.Lineas[0].ConCantidadYDescuento(2, 10m);
            Assert.True(armazonModificado.EsValida);
            return original.Modificar(OtroCliente(), [original.Lineas[2], armazonModificado.Linea!]);
        });

        Assert.Equal(
            [["5", "2026-03-10", "borrador", OtroApellido, OtroNombre, OtroDni, "", OtroEmail, "", "1"]],
            Filas(cadena, SelectEncabezado));
        var lenteTexto = lente.Codigo!.Value.ToString(CultureInfo.InvariantCulture);
        var armazonTexto = armazon.Codigo!.Value.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(
            [
                ["5", "1", lenteTexto, "Lente", "100000", "100000", "100000", "1", "0"],
                ["5", "2", armazonTexto, "Armazón", "185000", "166500", "333000", "2", "1000"],
            ],
            Filas(cadena, SelectLinea));
        var leido = (await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(5))!;
        Assert.Equal((1L, Fecha, EstadoPresupuesto.Borrador, 4330m), (leido.Version, leido.Fecha, leido.Estado, leido.Total));
        Assert.Equal(["Lente", "Armazón"], leido.Lineas.Select(l => l.Descripcion));
    }

    [Fact]
    public async Task Actualizar_Borrador_MasLineas_ReemplazaLineasEIncrementaVersion()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var armazon = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        var estuche = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "EST-1", "Estuche", 300m));
        var lente = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "LEN-1", "Lente", 1000m));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(5, Fecha, ClienteCompleto(), [Linea(armazon)]));

        // AC-03: el armazón pasa a cantidad 3 y se agregan dos líneas, numeradas 1..3.
        await ModificarAsync(baseTemporal, 5, original => original.Modificar(
            original.Cliente, [original.Lineas[0].ConCantidadYDescuento(3, 0m).Linea!, Linea(estuche), Linea(lente)]));

        Assert.Equal(
            [
                ["5", "1", "Armazón", "3", "555000"],
                ["5", "2", "Estuche", "1", "30000"],
                ["5", "3", "Lente", "1", "100000"],
            ],
            Filas(cadena,
                "SELECT PresupuestoNumero, Orden, Descripcion, Cantidad, PrecioFinalCentavos FROM LineaPresupuesto " +
                "ORDER BY Orden"));
        Assert.Equal([["1"]], Filas(cadena, "SELECT Version FROM Presupuesto"));

        // Otra modificación parte de la versión 1 y deja la 2.
        await ModificarAsync(baseTemporal, 5, original => original.Modificar(original.Cliente, [original.Lineas[1]]));
        Assert.Equal([["2"]], Filas(cadena, "SELECT Version FROM Presupuesto"));
        Assert.Equal([["5", "1", "Estuche"]],
            Filas(cadena, "SELECT PresupuestoNumero, Orden, Descripcion FROM LineaPresupuesto"));
        Assert.Equal(Apellido, Assert.Single(Filas(cadena, SelectEncabezado))[3]);
    }

    [Fact]
    public async Task Actualizar_VersionVieja_ConflictoConcurrencia()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var armazon = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        var estuche = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "EST-1", "Estuche", 300m));
        await GrabarPresupuesto(baseTemporal,
            Presupuesto.Nuevo(5, Fecha, ClienteCompleto(), [Linea(armazon), Linea(estuche)]));

        // Dos sesiones leen la versión 0; la primera graba (versión 1).
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        var leidoPorLaSegunda = (await repositorio.ObtenerAsync(5))!;
        await ModificarAsync(baseTemporal, 5, original => original.Modificar(original.Cliente, [original.Lineas[1]]));
        var encabezado = Filas(cadena, SelectEncabezado);
        var lineas = Filas(cadena, SelectLinea);

        // La segunda graba con la versión que leyó: conflicto, sin escribir encabezado ni líneas (D-01).
        var modificado = leidoPorLaSegunda.Modificar(
            OtroCliente(), [leidoPorLaSegunda.Lineas[0], leidoPorLaSegunda.Lineas[1], Linea(armazon)]);
        repositorio.Actualizar(leidoPorLaSegunda, modificado, leidoPorLaSegunda.Version);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Equal(MotivoConflicto.Concurrencia, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(OtroApellido, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(OtroDni, error.ToString(), StringComparison.Ordinal);
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Equal(encabezado, Filas(cadena, SelectEncabezado));
        Assert.Equal(lineas, Filas(cadena, SelectLinea));
    }

    [Fact]
    public async Task CambiarEstado_Borrador_FinalSinTocarLineas()
    {
        var directorio = BaseTemporal.CrearDirectorioTemporal();
        try
        {
            var registro = new RegistroCapturado();
            using var servicios = BaseTemporal.CrearServicios(directorio, registro);
            servicios.InicializarBaseDeDatos();
            var cadena = servicios.GetRequiredService<FabricaConexion>().CadenaConexion;
            using (var alcance = servicios.CreateScope())
            {
                var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
                var articulos = new ArticuloRepository(contexto);
                articulos.Agregar(Articulo.Nuevo("ARM-1", "Armazón", 1850m, 0m, 1850m));
                await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
                var articulo = (await articulos.ObtenerPorClaveAsync("ARM-1"))!;
                new PresupuestoRepository(contexto).Agregar(
                    Presupuesto.Nuevo(9, Fecha, ClienteCompleto(), [.. Enumerable.Repeat(Linea(articulo), 100)]));
                await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
            }

            var lineas = Filas(cadena, SelectLinea);
            Assert.Equal(100, lineas.Count);
            while (registro.Mensajes.TryDequeue(out _))
            {
            }

            // AC-04: dentro de la operación, como el caso de uso (ADR-013); solo se sigue el encabezado.
            using (var alcance = servicios.CreateScope())
            {
                var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
                var unidad = new UnidadDeTrabajoEf(contexto);
                var repositorio = new PresupuestoRepository(contexto);
                await unidad.EjecutarEnTransaccionAsync(async cancelacion =>
                {
                    var leido = (await repositorio.ObtenerAsync(9, cancelacion))!;
                    repositorio.CambiarEstado(leido.PasarAFinal(), leido.Version);
                    Assert.IsType<PresupuestoFila>(Assert.Single(contexto.ChangeTracker.Entries()).Entity);
                    await unidad.ConfirmarAsync(cancelacion);
                    return 0;
                });
            }

            Assert.Equal(
                [["9", "2026-03-10", "final", Apellido, Nombre, Dni, Domicilio, Email, Telefono, "1"]],
                Filas(cadena, SelectEncabezado));
            Assert.Equal(lineas, Filas(cadena, SelectLinea));

            // Un único UPDATE del encabezado, solo con Estado y Version; ningún comando sobre las líneas.
            var comandos = registro.Mensajes.Where(m => m.Contains("Executed DbCommand", StringComparison.Ordinal)).ToList();
            var update = Assert.Single(comandos, m => m.Contains("UPDATE \"Presupuesto\"", StringComparison.Ordinal));
            Assert.Contains("SET \"Estado\" = ", update, StringComparison.Ordinal);
            Assert.Contains("\"Version\" = ", update, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Apellido\"", update, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Fecha\"", update, StringComparison.Ordinal);
            Assert.DoesNotContain(comandos, m =>
                m.Contains("INSERT INTO \"LineaPresupuesto\"", StringComparison.Ordinal)
                || m.Contains("UPDATE \"LineaPresupuesto\"", StringComparison.Ordinal)
                || m.Contains("DELETE FROM \"LineaPresupuesto\"", StringComparison.Ordinal));
            AfirmarSinDatosDelCliente(string.Join("\n", registro.Mensajes));
        }
        finally
        {
            BaseTemporal.BorrarDirectorio(directorio);
        }
    }

    [Fact]
    public async Task CambiarEstado_VersionVieja_ConflictoConcurrencia()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var armazon = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(5, Fecha, ClienteCompleto(), [Linea(armazon)]));

        // Una sesión lee la versión 0; otra modifica el Borrador (versión 1) antes de que la primera pase a Final.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        var leido = (await repositorio.ObtenerAsync(5))!;
        await ModificarAsync(baseTemporal, 5, original => original.Modificar(
            OtroCliente(), [original.Lineas[0].ConCantidadYDescuento(2, 0m).Linea!]));
        var encabezado = Filas(cadena, SelectEncabezado);
        var lineas = Filas(cadena, SelectLinea);

        repositorio.CambiarEstado(leido.PasarAFinal(), leido.Version);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        // Nada en Final con un contenido que la operadora no vio (D-01).
        Assert.Equal(MotivoConflicto.Concurrencia, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Equal(encabezado, Filas(cadena, SelectEncabezado));
        Assert.Equal("borrador", Assert.Single(encabezado)[2]);
        Assert.Equal(lineas, Filas(cadena, SelectLinea));
    }

    [Fact]
    public void Actualizar_VersionMaxima_Rechazada()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        var linea = new LineaPresupuesto(1, "Armazón", 10m, 1, 0m, 10m, 10m);
        var original = new Presupuesto(
            5, Fecha, EstadoPresupuesto.Borrador, ClienteCompleto(), [linea], Presupuesto.VersionMaxima);
        var modificado = original.Modificar(OtroCliente(), [linea, linea]);

        // La versión siguiente no entraría en el rango de la columna: se rechaza antes de registrar nada.
        var maxima = Assert.Throws<ArgumentException>(
            () => repositorio.Actualizar(original, modificado, Presupuesto.VersionMaxima));
        Assert.Equal("versionOriginal", maxima.ParamName);
        var negativa = Assert.Throws<ArgumentException>(() => repositorio.Actualizar(original, modificado, -1));
        Assert.Equal("versionOriginal", negativa.ParamName);
        var enFinal = Assert.Throws<ArgumentException>(
            () => repositorio.CambiarEstado(original.PasarAFinal(), Presupuesto.VersionMaxima));
        Assert.Equal("versionOriginal", enFinal.ParamName);

        // Original y modificado tienen que ser el mismo presupuesto.
        var otro = Presupuesto.Nuevo(6, Fecha, OtroCliente(), [linea]);
        var otroNumero = Assert.Throws<ArgumentException>(() => repositorio.Actualizar(original, otro, 0));
        Assert.Equal("modificado", otroNumero.ParamName);
        Assert.Throws<ArgumentNullException>(() => repositorio.Actualizar(null!, modificado, 0));
        Assert.Throws<ArgumentNullException>(() => repositorio.Actualizar(original, null!, 0));

        // Mensajes fijos, sin datos del cliente, y nada registrado en el contexto.
        AfirmarSinDatosDelCliente(maxima.Message + negativa.Message + enFinal.Message + otroNumero.Message);
        Assert.DoesNotContain(OtroApellido, otroNumero.Message, StringComparison.Ordinal);
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public void CambiarEstado_PresupuestoNoFinal_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        var borrador = Presupuesto.Nuevo(
            5, Fecha, ClienteCompleto(), [new LineaPresupuesto(1, "Armazón", 10m, 1, 0m, 10m, 10m)]);

        // CambiarEstado solo registra el paso a Final que ya hizo el dominio; Final → Borrador no existe (FR-06).
        var error = Assert.Throws<ArgumentException>(() => repositorio.CambiarEstado(borrador, 0));

        Assert.Equal("presupuesto", error.ParamName);
        AfirmarSinDatosDelCliente(error.Message);
        Assert.Throws<ArgumentNullException>(() => repositorio.CambiarEstado(null!, 0));
        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CambiarEstado_EntradaSeguidaConOtrosCambios_Rechazada()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(5, Fecha, ClienteCompleto(), [Linea(articulo)]));
        var encabezado = Filas(cadena, SelectEncabezado);

        // El contexto ya sigue la fila con el apellido cambiado: CambiarEstado no puede escribir solo Estado y Version.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        var leido = (await repositorio.ObtenerAsync(5))!;
        contexto.Presupuestos.Find(5L)!.Apellido = OtroApellido;
        var conCambios = Assert.Throws<InvalidOperationException>(
            () => repositorio.CambiarEstado(leido.PasarAFinal(), leido.Version));
        Assert.Equal(MensajeEntradaConOtrosCambios, conCambios.Message);

        // Actualizar admite los datos del cliente modificados (los escribe él), pero no otra columna como la fecha.
        var conFecha = baseTemporal.CrearContexto();
        conFecha.Presupuestos.Find(5L)!.Fecha = new DateOnly(2026, 3, 11);
        var enActualizar = Assert.Throws<InvalidOperationException>(() => new PresupuestoRepository(conFecha)
            .Actualizar(leido, leido.Modificar(OtroCliente(), leido.Lineas), leido.Version));
        Assert.Equal(MensajeEntradaConOtrosCambios, enActualizar.Message);

        // Seguida como borrada, o como alta de otro presupuesto con el mismo número: también se rechaza.
        var borrado = baseTemporal.CrearContexto();
        borrado.Presupuestos.Remove(borrado.Presupuestos.Find(5L)!);
        Assert.Equal(MensajeEntradaConOtrosCambios, Assert.Throws<InvalidOperationException>(
            () => new PresupuestoRepository(borrado).CambiarEstado(leido.PasarAFinal(), leido.Version)).Message);
        var alta = baseTemporal.CrearContexto();
        new PresupuestoRepository(alta).Agregar(Presupuesto.Nuevo(6, Fecha, ClienteCompleto(), [Linea(articulo)]));
        var otro = Presupuesto.Nuevo(6, Fecha, ClienteCompleto(), [Linea(articulo)]).PasarAFinal();
        Assert.Equal(MensajeEntradaConOtrosCambios, Assert.Throws<InvalidOperationException>(
            () => new PresupuestoRepository(alta).CambiarEstado(otro, 0)).Message);

        // CambiarEstado y después Actualizar en el mismo contexto: la modificación escribiría también el Estado.
        var pasoYModificacion = baseTemporal.CrearContexto();
        var repositorioMixto = new PresupuestoRepository(pasoYModificacion);
        repositorioMixto.CambiarEstado(leido.PasarAFinal(), leido.Version);
        var actualizarTrasPaso = Assert.Throws<InvalidOperationException>(
            () => repositorioMixto.Actualizar(leido, leido.Modificar(OtroCliente(), leido.Lineas), leido.Version));
        Assert.Equal(MensajeEntradaConOtrosCambios, actualizarTrasPaso.Message);

        AfirmarSinDatosDelCliente(conCambios.ToString() + enActualizar + actualizarTrasPaso);
        Assert.DoesNotContain(OtroApellido, conCambios.ToString(), StringComparison.Ordinal);
        Assert.Equal(encabezado, Filas(cadena, SelectEncabezado));

        // Seguida sin cambios, se reutiliza y el UPDATE sigue siendo solo de Estado y Version.
        var sinCambios = baseTemporal.CrearContexto();
        Assert.NotNull(sinCambios.Presupuestos.Find(5L));
        new PresupuestoRepository(sinCambios).CambiarEstado(leido.PasarAFinal(), leido.Version);
        Assert.Equal(
            [nameof(PresupuestoFila.Estado), nameof(PresupuestoFila.Version)],
            sinCambios.ChangeTracker.Entries<PresupuestoFila>().Single().Properties
                .Where(p => p.IsModified).Select(p => p.Metadata.Name).Order(StringComparer.Ordinal));
        await new UnidadDeTrabajoEf(sinCambios).ConfirmarAsync();
        Assert.Equal(["final", Apellido, "1"],
            Assert.Single(Filas(cadena, "SELECT Estado, Apellido, Version FROM Presupuesto")));
    }

    [Fact]
    public async Task Agregar_Final_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        var contexto = baseTemporal.CrearContexto();
        var final = new Presupuesto(1, Fecha, EstadoPresupuesto.Final, ClienteCompleto(), [Linea(articulo)], version: 0);

        // AGENTS.md: un Final no se graba directo; se graba en Borrador y después pasa a Final.
        var error = Assert.Throws<ArgumentException>(() => new PresupuestoRepository(contexto).Agregar(final));

        Assert.Equal("presupuesto", error.ParamName);
        AfirmarSinDatosDelCliente(error.Message);
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Empty(Filas(cadena, SelectPresupuesto));
    }

    [Fact]
    public async Task Actualizar_OtraSesionPasoAFinal_MotivoRegistroCerrado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var armazon = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        var estuche = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "EST-1", "Estuche", 300m));
        await GrabarPresupuesto(baseTemporal,
            Presupuesto.Nuevo(5, Fecha, ClienteCompleto(), [Linea(armazon), Linea(estuche)]));

        // Una sesión lee la versión 0; otra pasa el Borrador a Final (versión 1) antes de que la primera grabe.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        var leido = (await repositorio.ObtenerAsync(5))!;
        var cierre = baseTemporal.CrearContexto();
        var repositorioCierre = new PresupuestoRepository(cierre);
        var paraCerrar = (await repositorioCierre.ObtenerAsync(5))!;
        repositorioCierre.CambiarEstado(paraCerrar.PasarAFinal(), paraCerrar.Version);
        await new UnidadDeTrabajoEf(cierre).ConfirmarAsync();
        var encabezado = Filas(cadena, SelectEncabezado);
        var lineas = Filas(cadena, SelectLinea);

        // El motivo que devuelve la base: el doble de la aplicación (PresupuestosEnMemoria) replica este mismo.
        repositorio.Actualizar(leido, leido.Modificar(OtroCliente(), [leido.Lineas[1], Linea(armazon)]), leido.Version);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => new UnidadDeTrabajoEf(contexto).ConfirmarAsync());

        Assert.Equal(MotivoConflicto.RegistroCerrado, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Equal(encabezado, Filas(cadena, SelectEncabezado));
        Assert.Equal(lineas, Filas(cadena, SelectLinea));
    }

    public static TheoryData<long[]> OrdenesConHueco => new()
    {
        new long[] { 1, 3 },
        new long[] { 2 },
        new long[] { 2, 3 },
        new long[] { 1, 2, 4 },
    };

    [Theory]
    [MemberData(nameof(OrdenesConHueco))]
    public async Task Obtener_OrdenConHueco_EstadoCorrupto(long[] ordenes)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticulo(baseTemporal, "ARM-1", "Armazón metálico", 1850m);
        InsertarCrudo(cadena, "Presupuesto", ValoresPresupuesto(numero: 7));
        foreach (var orden in ordenes)
        {
            InsertarCrudo(cadena, "LineaPresupuesto", ValoresLinea(numero: 7, orden, codigo));
        }

        // El orden de la Api es la posición + 1 (D-03): una lista que no es exactamente 1..n no se puede representar.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(7));

        Assert.Equal(MensajeFilaInvalida, error.Message);
        AfirmarSinDatosDelCliente(error.ToString());

        // La misma fila con 1..n se lee.
        Ejecutar(cadena, "DELETE FROM LineaPresupuesto");
        for (var orden = 1L; orden <= ordenes.Length; orden++)
        {
            InsertarCrudo(cadena, "LineaPresupuesto", ValoresLinea(numero: 7, orden, codigo));
        }

        var leido = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(7);
        Assert.Equal(ordenes.Length, leido!.Lineas.Count);
    }

    [Fact]
    public async Task Trigger_ModificarFinal_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarFinal(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        var encabezado = Filas(cadena, SelectEncabezado);

        // AC-07: un UPDATE directo, sin pasar por el dominio ni por el caso de uso, también se rechaza.
        AfirmarRegistroCerrado(cadena, "UPDATE Presupuesto SET Apellido = 'Otroapellido' WHERE Numero = 1");
        AfirmarRegistroCerrado(cadena, "UPDATE Presupuesto SET Version = Version + 1 WHERE Numero = 1");
        AfirmarRegistroCerrado(cadena, "UPDATE Presupuesto SET Estado = 'final' WHERE Numero = 1");

        Assert.Equal(encabezado, Filas(cadena, SelectEncabezado));
        Assert.Equal("1", Assert.Single(encabezado)[9]);
    }

    [Fact]
    public async Task Trigger_LineasDeFinal_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticulo(baseTemporal, "ARM-1", "Armazón metálico", 1850m);
        var articulo = await LeerArticulo(baseTemporal, codigo);
        await GrabarFinal(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(2, Fecha, ClienteCompleto(), [Linea(articulo)]));
        var lineas = Filas(cadena, SelectLinea);

        // INSERT, UPDATE (también moviendo la línea hacia o desde el Final) y DELETE sobre las líneas del Final.
        var insert = Assert.Throws<SqliteException>(
            () => InsertarCrudo(cadena, "LineaPresupuesto", ValoresLinea(numero: 1, orden: 2, codigo)));
        AfirmarRegistroCerrado(insert);
        AfirmarRegistroCerrado(cadena, "UPDATE LineaPresupuesto SET Cantidad = 2 WHERE PresupuestoNumero = 1");
        AfirmarRegistroCerrado(cadena,
            "UPDATE LineaPresupuesto SET PresupuestoNumero = 1, Orden = 2 WHERE PresupuestoNumero = 2");
        AfirmarRegistroCerrado(cadena,
            "UPDATE LineaPresupuesto SET PresupuestoNumero = 2, Orden = 2 WHERE PresupuestoNumero = 1");
        AfirmarRegistroCerrado(cadena, "DELETE FROM LineaPresupuesto WHERE PresupuestoNumero = 1");
        Assert.Equal(lineas, Filas(cadena, SelectLinea));

        // Las líneas del Borrador se siguen pudiendo cambiar.
        Ejecutar(cadena, "UPDATE LineaPresupuesto SET Cantidad = 2 WHERE PresupuestoNumero = 2");
        Assert.Equal([["1", "1"], ["2", "2"]],
            Filas(cadena, "SELECT PresupuestoNumero, Cantidad FROM LineaPresupuesto ORDER BY PresupuestoNumero"));
    }

    [Fact]
    public async Task Trigger_FinalABorrador_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarFinal(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));

        // AC-08: Final → Borrador no existe, ni siquiera con un UPDATE directo.
        AfirmarRegistroCerrado(cadena, "UPDATE Presupuesto SET Estado = 'borrador' WHERE Numero = 1");

        var leido = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(1);
        Assert.Equal(EstadoPresupuesto.Final, leido!.Estado);
    }

    [Fact]
    public async Task Trigger_CambiarFechaONumero_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        var encabezado = Filas(cadena, SelectEncabezado);

        // AC-01: sobre un Borrador, para que el único trigger que dispara sea el de la fecha y el número.
        AfirmarFechaONumero(cadena, "UPDATE Presupuesto SET Fecha = '2026-03-11' WHERE Numero = 1");
        AfirmarFechaONumero(cadena, "UPDATE Presupuesto SET Numero = 9 WHERE Numero = 1");
        Assert.Equal(encabezado, Filas(cadena, SelectEncabezado));

        // Reescribir la misma fecha y el mismo número no dispara (IS NOT).
        Ejecutar(cadena, "UPDATE Presupuesto SET Fecha = '2026-03-10', Numero = 1, Apellido = 'Otroapellido'");
        Assert.Equal(["1", "2026-03-10", OtroApellido],
            Assert.Single(Filas(cadena, "SELECT Numero, Fecha, Apellido FROM Presupuesto")));
    }

    [Fact]
    public async Task Trigger_BorrarFinal_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarFinal(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(2, Fecha, ClienteCompleto(), [Linea(articulo)]));

        // El trigger dispara antes que la clave foránea de las líneas: el motivo es el registro cerrado (D-08).
        AfirmarRegistroCerrado(cadena, "DELETE FROM Presupuesto WHERE Numero = 1");
        Assert.Equal([["1"], ["2"]], Filas(cadena, "SELECT Numero FROM Presupuesto ORDER BY Numero"));

        // Un Borrador sin líneas sí se borra: el trigger solo protege los Final.
        Ejecutar(cadena, "DELETE FROM LineaPresupuesto WHERE PresupuestoNumero = 2; DELETE FROM Presupuesto WHERE Numero = 2");
        Assert.Equal([["1"]], Filas(cadena, "SELECT Numero FROM Presupuesto"));
    }

    [Fact]
    public async Task Migracion_CierrePresupuestos_BorradoresExistentesConVersion0()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var migrador = contexto.GetService<IMigrator>();
        // Por nombre, no por posición (D-29 de FEAT-007).
        var migraciones = contexto.Database.GetMigrations().ToList();
        var cierre = Migracion(migraciones, "_CierrePresupuestos");
        var presupuestos = Migracion(migraciones, "_Presupuestos");
        Assert.Equal(migraciones.IndexOf(presupuestos) + 1, migraciones.IndexOf(cierre));

        // ADR-012: SQL escrito a mano; ni AddColumn ni AddCheckConstraint, que en SQLite reconstruyen la tabla.
        var ensamblado = contexto.GetService<IMigrationsAssembly>();
        var migracion = ensamblado.CreateMigration(ensamblado.Migrations[cierre], contexto.Database.ProviderName!);
        Assert.All(migracion.UpOperations, operacion => Assert.IsType<SqlOperation>(operacion));
        Assert.All(migracion.DownOperations, operacion => Assert.IsType<SqlOperation>(operacion));

        // Una base de FEAT-003b con un Borrador y un Final (grabados antes de que existieran los triggers).
        migrador.Migrate(presupuestos);
        InsertarArticulo(cadena, "ABC-1", "Armazón");
        InsertarCrudo(cadena, "Presupuesto", ValoresPresupuesto(numero: 1));
        InsertarCrudo(cadena, "LineaPresupuesto", ValoresLinea(numero: 1, orden: 1, codigo: 1));
        var final = ValoresPresupuesto(numero: 2);
        final["Estado"] = "final";
        InsertarCrudo(cadena, "Presupuesto", final);
        InsertarCrudo(cadena, "LineaPresupuesto", ValoresLinea(numero: 2, orden: 1, codigo: 1));
        InsertarCrudo(cadena, "LineaPresupuesto", ValoresLinea(numero: 2, orden: 2, codigo: 1));
        var sqlAntes = SqlTabla(cadena, "Presupuesto");
        var sqlLineaAntes = SqlTabla(cadena, "LineaPresupuesto");
        var encabezados = Filas(cadena, SelectPresupuesto);
        var lineas = Filas(cadena, SelectLinea);

        migrador.Migrate(cierre);

        // Sin reconstruir: el CREATE TABLE es el mismo, con la columna que ADD COLUMN inserta después de la última; datos
        // y líneas iguales.
        const string columnaVersion =
            ", \"Version\" INTEGER NOT NULL DEFAULT 0 CONSTRAINT \"CK_Presupuesto_Version\" CHECK " +
            "(\"Version\" BETWEEN 0 AND 9007199254740991)";
        var sqlDespues = SqlTabla(cadena, "Presupuesto");
        Assert.Contains(columnaVersion, sqlDespues, StringComparison.Ordinal);
        Assert.Equal(sqlAntes, sqlDespues.Replace(columnaVersion, string.Empty, StringComparison.Ordinal));
        Assert.Equal(sqlLineaAntes, SqlTabla(cadena, "LineaPresupuesto"));
        Assert.Equal(encabezados, Filas(cadena, SelectPresupuesto));
        Assert.Equal(lineas, Filas(cadena, SelectLinea));
        Assert.Equal([["0"], ["0"]], Filas(cadena, "SELECT Version FROM Presupuesto ORDER BY Numero"));
        Assert.Equal(TriggersDeCierre, Triggers(cadena));
        Assert.Equal(cierre, contexto.Database.GetAppliedMigrations().Last());

        // Los Final que ya estaban quedan protegidos, y los Borradores se leen con versión 0.
        AfirmarRegistroCerrado(cadena, "UPDATE Presupuesto SET Apellido = 'Otroapellido' WHERE Numero = 2");
        migrador.Migrate();
        Assert.Empty(contexto.Database.GetPendingMigrations());
        var leido = await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(1);
        Assert.Equal(0, leido!.Version);
    }

    [Fact]
    public async Task Migracion_Down_QuitaTriggersYColumna()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var articulo = await LeerArticulo(baseTemporal, await GrabarArticulo(baseTemporal, "ARM-1", "Armazón", 1850m));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        await GrabarFinal(baseTemporal, Presupuesto.Nuevo(2, Fecha, ClienteCompleto(), [Linea(articulo)]));
        var contexto = baseTemporal.CrearContexto();
        var migrador = contexto.GetService<IMigrator>();
        var presupuestos = Migracion(contexto.Database.GetMigrations().ToList(), "_Presupuestos");
        var encabezados = Filas(cadena, SelectPresupuesto);
        var lineas = Filas(cadena, SelectLinea);

        // Solo para desarrollo: quita primero los triggers (si no, DROP COLUMN fallaría) y después la columna.
        migrador.Migrate(presupuestos);

        Assert.Empty(Triggers(cadena));
        Assert.DoesNotContain("Version", Columnas(cadena, "Presupuesto").Select(c => c.Split(' ')[0]));
        Assert.DoesNotContain("CK_Presupuesto_Version", SqlTabla(cadena, "Presupuesto"), StringComparison.Ordinal);
        Assert.Equal(encabezados, Filas(cadena, SelectPresupuesto));
        Assert.Equal(lineas, Filas(cadena, SelectLinea));

        // Volver a aplicarla deja todo como antes; la versión del Final se pierde y vuelve a 0.
        migrador.Migrate();
        Assert.Equal(TriggersDeCierre, Triggers(cadena));
        Assert.Equal([["0"], ["0"]], Filas(cadena, "SELECT Version FROM Presupuesto ORDER BY Numero"));
        Assert.Equal(encabezados, Filas(cadena, SelectPresupuesto));
        Assert.Empty(contexto.Database.GetPendingMigrations());
    }

    /// <summary>La migración del ensamblado con ese sufijo, buscada por nombre (D-29 de FEAT-007).</summary>
    private static string Migracion(IReadOnlyList<string> migraciones, string sufijo) =>
        Assert.Single(migraciones, m => m.EndsWith(sufijo, StringComparison.Ordinal));

    private static ClientePresupuesto OtroCliente() => new(OtroApellido, OtroNombre, OtroDni, null, OtroEmail, null);

    /// <summary>
    /// Lee el presupuesto, arma el modificado y lo graba con la versión leída, dentro de la operación en transacción,
    /// como el caso de uso (ADR-013).
    /// </summary>
    private static async Task ModificarAsync(
        BaseTemporal baseTemporal, long numero, Func<Presupuesto, Presupuesto> modificar)
    {
        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadDeTrabajoEf(contexto);
        var repositorio = new PresupuestoRepository(contexto);
        await unidad.EjecutarEnTransaccionAsync(async cancelacion =>
        {
            var original = (await repositorio.ObtenerAsync(numero, cancelacion))!;
            repositorio.Actualizar(original, modificar(original), original.Version);
            await unidad.ConfirmarAsync(cancelacion);
            return 0;
        });
    }

    /// <summary>Graba el presupuesto en Borrador y después lo pasa a Final, como la aplicación (AGENTS.md).</summary>
    private static async Task GrabarFinal(BaseTemporal baseTemporal, Presupuesto borrador)
    {
        await GrabarPresupuesto(baseTemporal, borrador);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        var leido = (await repositorio.ObtenerAsync(borrador.Numero))!;
        repositorio.CambiarEstado(leido.PasarAFinal(), leido.Version);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    private static void AfirmarRegistroCerrado(string cadena, string sql) =>
        AfirmarRegistroCerrado(Assert.Throws<SqliteException>(() => Ejecutar(cadena, sql)));

    private static void AfirmarRegistroCerrado(SqliteException error)
    {
        Assert.Equal(SqliteRestriccionTrigger, error.SqliteExtendedErrorCode);
        Assert.Contains("'registro_cerrado'", error.Message, StringComparison.Ordinal);
    }

    private static void AfirmarFechaONumero(string cadena, string sql)
    {
        var error = Assert.Throws<SqliteException>(() => Ejecutar(cadena, sql));
        Assert.Equal(SqliteRestriccionTrigger, error.SqliteExtendedErrorCode);
        Assert.Contains("'presupuesto_fecha_numero'", error.Message, StringComparison.Ordinal);
    }

    private static ClientePresupuesto ClienteCompleto() =>
        new(Apellido, Nombre, DniConPuntos, Domicilio, Email, Telefono);

    private static LineaPresupuesto Linea(Articulo articulo)
    {
        var resultado = LineaPresupuesto.DesdeArticulo(articulo, cantidad: 1, porcentajeDescuento: 0m);
        Assert.True(resultado.EsValida);
        return resultado.Linea;
    }

    /// <summary>Ninguno de los datos del cliente ficticio aparece en el texto (D-17).</summary>
    private static void AfirmarSinDatosDelCliente(string texto)
    {
        foreach (var dato in new[] { Apellido, Nombre, Dni, DniConPuntos, Domicilio, Email, Telefono })
        {
            Assert.DoesNotContain(dato, texto, StringComparison.Ordinal);
        }
    }

    private static async Task<long> GrabarArticulo(
        BaseTemporal baseTemporal, string codigo, string descripcion, decimal precio)
    {
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);
        repositorio.Agregar(Articulo.Nuevo(codigo, descripcion, precio, 0m, precio));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        return (await repositorio.ObtenerPorClaveAsync(NormalizacionCodigo.Clave(codigo)))!.Codigo!.Value;
    }

    private static async Task<Articulo> LeerArticulo(BaseTemporal baseTemporal, long codigo) =>
        (await new ArticuloRepository(baseTemporal.CrearContexto()).ObtenerAsync(codigo))!;

    private static async Task GrabarPresupuesto(BaseTemporal baseTemporal, Presupuesto presupuesto)
    {
        var contexto = baseTemporal.CrearContexto();
        new PresupuestoRepository(contexto).Agregar(presupuesto);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    private static Dictionary<string, object> ValoresPresupuesto(long numero) => new(StringComparer.Ordinal)
    {
        ["Numero"] = numero,
        ["Fecha"] = "2026-03-10",
        ["Estado"] = "borrador",
        ["Apellido"] = Apellido,
        ["Nombre"] = Nombre,
        ["Dni"] = Dni,
        ["Domicilio"] = Domicilio,
        ["Email"] = Email,
        ["Telefono"] = Telefono,
    };

    private static Dictionary<string, object> ValoresLinea(long numero, long orden, long codigo) =>
        new(StringComparer.Ordinal)
        {
            ["PresupuestoNumero"] = numero,
            ["Orden"] = orden,
            ["CodigoArticulo"] = codigo,
            ["Descripcion"] = "Armazón metálico",
            ["PrecioUnitarioCentavos"] = 185000L,
            ["PrecioConDescuentoCentavos"] = 185000L,
            ["PrecioFinalCentavos"] = 185000L,
            ["Cantidad"] = 1L,
            ["DescuentoCentesimos"] = 0L,
        };

    /// <summary>Inserta una fila con parámetros, sin pasar por el repositorio.</summary>
    private static void InsertarCrudo(string cadena, string tabla, Dictionary<string, object> valores)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        var columnas = valores.Keys.ToList();
        comando.CommandText =
            $"INSERT INTO \"{tabla}\" ({string.Join(", ", columnas.Select(c => $"\"{c}\""))}) " +
            $"VALUES ({string.Join(", ", columnas.Select((_, i) => $"$p{i}"))})";
        for (var i = 0; i < columnas.Count; i++)
        {
            comando.Parameters.AddWithValue($"$p{i}", valores[columnas[i]]);
        }

        comando.ExecuteNonQuery();
    }

    private static void InsertarArticulo(string cadena, string codigo, string descripcion)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText =
            "INSERT INTO Articulo (CodigoProveedor, CodigoProveedorClave, Descripcion, PrecioCostoCentavos, " +
            "MargenCentesimos, PrecioVentaCentavos, Activo, Version) VALUES ($codigo, $codigo, $descripcion, 1000, 0, 1000, 1, 0)";
        comando.Parameters.AddWithValue("$codigo", codigo);
        comando.Parameters.AddWithValue("$descripcion", descripcion);
        comando.ExecuteNonQuery();
    }

    /// <summary>Todo lo que una reconstrucción de <c>Articulo</c> cambiaría: datos, SQL, índices, triggers y secuencia.</summary>
    private static List<List<string[]>> FotoArticulo(string cadena) =>
    [
        Filas(cadena, "SELECT * FROM Articulo ORDER BY Id"),
        Filas(cadena, "SELECT sql FROM sqlite_master WHERE tbl_name = 'Articulo' ORDER BY type, name"),
        Filas(cadena, "SELECT name, \"unique\" FROM pragma_index_list('Articulo') ORDER BY name"),
        Filas(cadena, "SELECT name FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'Articulo' ORDER BY name"),
        Filas(cadena, "SELECT seq FROM sqlite_sequence WHERE name = 'Articulo'"),
    ];

    private static List<(string Nombre, string Expresion)> Checks(string sql) =>
        [.. RegexCheck().Matches(sql)
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value))
            .OrderBy(c => c.Item1, StringComparer.Ordinal)];

    // CONSTRAINT "CK_x" CHECK (expresión) seguido de una coma o del cierre de la tabla.
    [GeneratedRegex("CONSTRAINT \"(CK_[^\"]+)\" CHECK \\((.+?)\\)(?=,\\s|\\s*\\)\\s*$)", RegexOptions.Singleline)]
    private static partial Regex RegexCheck();

    private static List<string> Columnas(string cadena, string tabla) =>
        [.. Filas(cadena, $"SELECT name, type, \"notnull\", pk, dflt_value FROM pragma_table_info('{tabla}')")
            .OrderBy(c => c[0], StringComparer.Ordinal)
            .Select(c => string.Join(" ", c))];

    private static string SqlTabla(string cadena, string tabla) =>
        Assert.Single(Filas(cadena, $"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{tabla}'"))[0];

    private static List<string> Tablas(string cadena) =>
        [.. Filas(cadena, "SELECT name FROM sqlite_master WHERE type = 'table'").Select(f => f[0])];

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

    // ---- Búsqueda (Block 1 de FEAT-006). Datos ficticios; los Final se graban en Borrador y después pasan a Final. ----

    private const int TamanoPagina = 50;

    [Fact]
    public async Task Buscar_ApellidoParcialSinAcentos_SoloGonzalez()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarParaBuscar(baseTemporal,
            new Sembrado(1, Fecha, "González", "Luis", "30111222", Final: true),
            new Sembrado(2, Fecha, "Gómez", "Luis", "30111223"));

        // AC-01: "ONZALEZ" plegado coincide con "González" guardado, y no con "Gómez".
        var pagina = await Buscar(baseTemporal, FiltroPresupuestos.Crear("ONZALEZ", null, null, null, null));

        var resumen = Assert.Single(pagina.Presupuestos);
        Assert.Equal(1, resumen.Numero);
        Assert.Equal("González", resumen.Apellido);
        Assert.Equal(EstadoPresupuesto.Final, resumen.Estado);
        Assert.Equal(1, pagina.Total);
    }

    [Fact]
    public async Task Buscar_NombreSinAcento_SoloMariaJose()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarParaBuscar(baseTemporal,
            new Sembrado(1, Fecha, "Ficticio", "María José", "30111222"),
            new Sembrado(2, Fecha, "Ficticio", "Ana", "30111223"));

        // AC-02.
        var pagina = await Buscar(baseTemporal, FiltroPresupuestos.Crear(null, "maria", null, null, null));

        var resumen = Assert.Single(pagina.Presupuestos);
        Assert.Equal(1, resumen.Numero);
        Assert.Equal("María José", resumen.Nombre);
        Assert.Equal(EstadoPresupuesto.Borrador, resumen.Estado);
        Assert.Equal(1, pagina.Total);
    }

    [Fact]
    public async Task Buscar_DniParcialConPuntos_Coincide()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarParaBuscar(baseTemporal,
            new Sembrado(1, Fecha, "Ficticio", "Luis", "23456789"),
            new Sembrado(2, Fecha, "Ficticio", "Luis", "30111222"));

        // AC-03: los puntos del texto buscado no cuentan.
        foreach (var dni in new[] { "3456", "23.456" })
        {
            var pagina = await Buscar(baseTemporal, FiltroPresupuestos.Crear(null, null, dni, null, null));

            var resumen = Assert.Single(pagina.Presupuestos);
            Assert.Equal(1, resumen.Numero);
            Assert.Equal("23456789", resumen.Dni);
            Assert.Equal(1, pagina.Total);
        }
    }

    [Fact]
    public async Task Buscar_ApellidoYDesde_CombinaConY()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarParaBuscar(baseTemporal,
            new Sembrado(1, new DateOnly(2026, 3, 10), "González", "Luis", "30111222"),
            new Sembrado(2, new DateOnly(2026, 3, 20), "González", "Luis", "30111223"),
            new Sembrado(3, new DateOnly(2026, 3, 25), "Gómez", "Luis", "30111224"));

        // AC-04: Apellido "González" Y Desde 15/03/2026, sin Hasta.
        var pagina = await Buscar(baseTemporal,
            FiltroPresupuestos.Crear("González", null, null, new DateOnly(2026, 3, 15), null));

        var resumen = Assert.Single(pagina.Presupuestos);
        Assert.Equal(2, resumen.Numero);
        Assert.Equal(new DateOnly(2026, 3, 20), resumen.Fecha);
        Assert.Equal(1, pagina.Total);
    }

    [Fact]
    public async Task Buscar_RangoDesdeHasta_LimitesInclusive()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarFechas(baseTemporal, 9, 10, 20, 21);

        // AC-05: los dos límites inclusive.
        var pagina = await Buscar(baseTemporal,
            FiltroPresupuestos.Crear(null, null, null, new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 20)));

        Assert.Equal([3L, 2L], Numeros(pagina));
        Assert.Equal([new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 10)], pagina.Presupuestos.Select(r => r.Fecha));
        Assert.Equal(2, pagina.Total);
    }

    [Fact]
    public async Task Buscar_SoloHasta_IncluyeEseDiaYAnteriores()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarFechas(baseTemporal, 9, 10, 11);

        // AC-06: Hasta 10/03/2026, sin Desde.
        var pagina = await Buscar(baseTemporal,
            FiltroPresupuestos.Crear(null, null, null, null, new DateOnly(2026, 3, 10)));

        Assert.Equal([2L, 1L], Numeros(pagina));
        Assert.Equal(2, pagina.Total);
    }

    [Fact]
    public async Task Buscar_SinFiltros_120_PaginasDe50Descendente()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarParaBuscar(baseTemporal,
            [.. Enumerable.Range(1, 120).Select(i => new Sembrado(i, Fecha, "Ficticio", "Luis", $"{30000000 + i}"))]);
        var sinFiltros = FiltroPresupuestos.Crear(null, null, null, null, null);

        // AC-08: por número de mayor a menor, de a 50 (FR-07, FR-08).
        var primera = await Buscar(baseTemporal, sinFiltros, pagina: 1);
        var tercera = await Buscar(baseTemporal, sinFiltros, pagina: 3);
        var posterior = await Buscar(baseTemporal, sinFiltros, pagina: 4);

        Assert.Equal(Enumerable.Range(71, 50).Reverse().Select(i => (long)i), Numeros(primera));
        Assert.Equal(Enumerable.Range(1, 20).Reverse().Select(i => (long)i), Numeros(tercera));
        Assert.Empty(posterior.Presupuestos);
        Assert.All([primera, tercera, posterior], pagina => Assert.Equal(120, pagina.Total));
    }

    [Fact]
    public async Task Buscar_TotalEsSumaDePreciosFinales()
    {
        using var baseTemporal = new BaseTemporal();
        var codigo = await GrabarArticulo(baseTemporal, "BUS-9", "Lente orgánico", 6.67m);
        var cliente = new ClientePresupuesto("Ficticiez", "Prueboncia", "23456789", null, null, "11 5555-0199");

        // Las líneas de AC-14: 10,02 + 2.700,00. Otro presupuesto con su propia línea no se suma.
        await GrabarFinal(baseTemporal, Presupuesto.Nuevo(155, Fecha, cliente,
        [
            new LineaPresupuesto(codigo, "Lente orgánico", 6.67m, 3, 50m, 3.34m, 10.02m),
            new LineaPresupuesto(codigo, "Armazón metálico", 2700m, 1, 0m, 2700m, 2700m),
        ]));
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(156, Fecha, cliente,
            [new LineaPresupuesto(codigo, "Estuche", 1m, 1, 0m, 1m, 1m)]));

        var comandos = new ComandosObservados();
        await using var contexto = ContextoCon(baseTemporal, comandos);
        var pagina = await new PresupuestoRepository(contexto)
            .BuscarAsync(FiltroPresupuestos.Crear(null, null, null, null, null), 1, TamanoPagina);

        // D-08: la suma se hace en SQL; solo la página y el conteo, sin una lectura por presupuesto ni de las líneas.
        Assert.Equal(2, comandos.Ejecutados);
        Assert.Contains("SUM(", comandos.Textos[0], StringComparison.Ordinal);
        Assert.Equal(2, pagina.Total);
        Assert.Equal([156L, 155L], Numeros(pagina));
        var resumen = pagina.Presupuestos[1];
        Assert.Equal(155, resumen.Numero);
        Assert.Equal(Fecha, resumen.Fecha);
        Assert.Equal("Ficticiez", resumen.Apellido);
        Assert.Equal("Prueboncia", resumen.Nombre);
        Assert.Equal("23456789", resumen.Dni);
        Assert.Equal(EstadoPresupuesto.Final, resumen.Estado);
        Assert.Equal(2710.02m, resumen.Total);
        Assert.Equal(1m, pagina.Presupuestos[0].Total);
        Assert.Equal(EstadoPresupuesto.Borrador, pagina.Presupuestos[0].Estado);
    }

    [Fact]
    public async Task Buscar_PorcentajeYGuionBajo_NoSonComodines()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarParaBuscar(baseTemporal,
            new Sembrado(1, Fecha, "Pérez", "Ana", "30111222"),
            new Sembrado(2, Fecha, "Gómez", "Luis", "30111223"));

        // R-01, D-05: instr y no LIKE; "%" y "_" se buscan tal cual y ningún dato guardado los tiene.
        foreach (var comodin in new[] { "%", "_", "%_%" })
        {
            foreach (var filtro in new[]
                     {
                         FiltroPresupuestos.Crear(comodin, null, null, null, null),
                         FiltroPresupuestos.Crear(null, comodin, null, null, null),
                         FiltroPresupuestos.Crear(null, null, comodin, null, null),
                     })
            {
                var comandos = new ComandosObservados();
                await using var contexto = ContextoCon(baseTemporal, comandos);
                var pagina = await new PresupuestoRepository(contexto).BuscarAsync(filtro, 1, TamanoPagina);

                Assert.Empty(pagina.Presupuestos);
                Assert.Equal(0, pagina.Total);

                // La página y el conteo filtran con instr y el texto viaja como parámetro, nunca en el SQL.
                Assert.Equal(2, comandos.Textos.Count);
                Assert.All(comandos.Textos, sql =>
                {
                    Assert.Contains("instr(", sql, StringComparison.Ordinal);
                    Assert.DoesNotContain("LIKE", sql, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain($"'{comodin}", sql, StringComparison.Ordinal);
                });
                Assert.All(comandos.Parametros, valores => Assert.Contains(comodin, valores));
            }
        }
    }

    [Fact]
    public async Task Buscar_DniConLetras_SinCoincidencias()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarParaBuscar(baseTemporal, new Sembrado(1, Fecha, "Ficticio", "Luis", "23456789"));

        // D-06: un DNI con otros caracteres no es un error; simplemente no coincide.
        var pagina = await Buscar(baseTemporal, FiltroPresupuestos.Crear(null, null, "2345a", null, null));

        Assert.Empty(pagina.Presupuestos);
        Assert.Equal(0, pagina.Total);
    }

    [Theory]
    [InlineData(0, TamanoPagina, "pagina")]
    [InlineData(-1, TamanoPagina, "pagina")]
    [InlineData(1, 0, "tamanoPagina")]
    [InlineData(1, -5, "tamanoPagina")]
    public async Task Buscar_PaginaInvalida_Rechazada(int pagina, int tamano, string parametro)
    {
        using var baseTemporal = new BaseTemporal();
        var comandos = new ComandosObservados();
        await using var contexto = ContextoCon(baseTemporal, comandos);
        var repositorio = new PresupuestoRepository(contexto);

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repositorio.BuscarAsync(FiltroPresupuestos.Crear(null, null, null, null, null), pagina, tamano));

        // Texto fijo: ni el valor recibido ni el "Actual value" que agregan los helpers de .NET.
        Assert.Equal(parametro, error.ParamName);
        Assert.Null(error.ActualValue);
        var recibido = (parametro == "pagina" ? pagina : tamano).ToString(CultureInfo.InvariantCulture);
        Assert.DoesNotContain(recibido, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, comandos.Ejecutados);
    }

    [Fact]
    public async Task Buscar_PaginaQueDesborda_Rechazada()
    {
        using var baseTemporal = new BaseTemporal();
        var comandos = new ComandosObservados();
        await using var contexto = ContextoCon(baseTemporal, comandos);
        var repositorio = new PresupuestoRepository(contexto);
        var sinFiltros = FiltroPresupuestos.Crear(null, null, null, null, null);

        // (int.MaxValue - 1) × 50 no entra en un int: se rechaza en lugar de desbordar, sin consultar (D-17).
        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repositorio.BuscarAsync(sinFiltros, int.MaxValue, TamanoPagina));

        Assert.Equal("pagina", error.ParamName);
        Assert.DoesNotContain(int.MaxValue.ToString(CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.Equal(0, comandos.Ejecutados);
        await Assert.ThrowsAsync<ArgumentNullException>(() => repositorio.BuscarAsync(null!, 1, TamanoPagina));
        Assert.Equal(0, comandos.Ejecutados);
    }

    [Fact]
    public async Task Buscar_TotalMenorQueLaPagina_SeAjusta()
    {
        using var baseTemporal = new BaseTemporal();
        await SembrarFechas(baseTemporal, 9, 10, 11);

        // El doble deja el COUNT en 0, como si una edición entre las dos lecturas sacara del filtro lo que la página
        // trajo: el total nunca queda menor que la página.
        var comandos = new ComandosObservados(conteoEnCero: true);
        await using var contexto = ContextoCon(baseTemporal, comandos);
        var pagina = await new PresupuestoRepository(contexto)
            .BuscarAsync(FiltroPresupuestos.Crear(null, null, null, null, null), 1, TamanoPagina);

        Assert.Equal(1, comandos.ConteosReemplazados);
        Assert.Equal([3L, 2L, 1L], Numeros(pagina));
        Assert.Equal(3, pagina.Total);
    }

    [Fact]
    public async Task Buscar_FechaGuardadaIlegible_ErrorSinValores()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticulo(baseTemporal, "ARM-1", "Armazón metálico", 1850m);

        // "2026-02-30" pasa el CHECK de formato pero no es una fecha real.
        var valores = ValoresPresupuesto(numero: 7);
        valores["Fecha"] = "2026-02-30";
        InsertarCrudo(cadena, "Presupuesto", valores);
        InsertarCrudo(cadena, "LineaPresupuesto", ValoresLinea(numero: 7, orden: 1, codigo));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Buscar(baseTemporal, FiltroPresupuestos.Crear(null, null, null, null, null)));

        Assert.Equal(MensajeFilaInvalida, error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("2026-02-30", error.ToString(), StringComparison.Ordinal);
        AfirmarSinDatosDelCliente(error.ToString());
    }

    /// <summary>Un presupuesto a sembrar para la búsqueda, con un cliente ficticio sin datos opcionales.</summary>
    private sealed record Sembrado(long Numero, DateOnly Fecha, string Apellido, string Nombre, string Dni, bool Final = false);

    /// <summary>
    /// Graba los presupuestos en Borrador con una línea cada uno y pasa a Final los marcados, como la aplicación
    /// (AGENTS.md).
    /// </summary>
    private static async Task SembrarParaBuscar(BaseTemporal baseTemporal, params Sembrado[] presupuestos)
    {
        var codigo = await GrabarArticulo(baseTemporal, "BUS-1", "Armazón de búsqueda", 1850m);
        var articulo = await LeerArticulo(baseTemporal, codigo);
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        foreach (var sembrado in presupuestos)
        {
            repositorio.Agregar(Presupuesto.Nuevo(
                sembrado.Numero, sembrado.Fecha,
                new ClientePresupuesto(sembrado.Apellido, sembrado.Nombre, sembrado.Dni, null, null, null),
                [Linea(articulo)]));
        }

        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        foreach (var sembrado in presupuestos.Where(p => p.Final))
        {
            var otro = baseTemporal.CrearContexto();
            var repositorioFinal = new PresupuestoRepository(otro);
            var leido = (await repositorioFinal.ObtenerAsync(sembrado.Numero))!;
            repositorioFinal.CambiarEstado(leido.PasarAFinal(), leido.Version);
            await new UnidadDeTrabajoEf(otro).ConfirmarAsync();
        }
    }

    /// <summary>Un presupuesto por día de marzo de 2026, numerados desde 1 en el orden recibido.</summary>
    private static Task SembrarFechas(BaseTemporal baseTemporal, params int[] dias) =>
        SembrarParaBuscar(baseTemporal,
            [.. dias.Select((dia, indice) =>
                new Sembrado(indice + 1, new DateOnly(2026, 3, dia), "Ficticio", "Luis", $"{30111222 + indice}"))]);

    private static Task<PaginaPresupuestos> Buscar(
        BaseTemporal baseTemporal, FiltroPresupuestos filtro, int pagina = 1) =>
        new PresupuestoRepository(baseTemporal.CrearContexto()).BuscarAsync(filtro, pagina, TamanoPagina);

    private static List<long> Numeros(PaginaPresupuestos pagina) => [.. pagina.Presupuestos.Select(r => r.Numero)];

    /// <summary>Un contexto como el de la infraestructura, con un interceptor agregado (patrón de UnidadDeTrabajoTests).</summary>
    private static OpticaDbContext ContextoCon(BaseTemporal baseTemporal, IInterceptor interceptor)
    {
        var opciones = new DbContextOptionsBuilder<OpticaDbContext>();
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().Configurar(opciones);
        opciones.AddInterceptors(interceptor);
        return new OpticaDbContext(opciones.Options);
    }

    /// <summary>
    /// Cuenta los comandos que EF Core ejecuta y guarda su SQL y los valores de sus parámetros; con
    /// <paramref name="conteoEnCero"/>, reemplaza cada comando que empieza con <c>SELECT COUNT(</c> por un <c>SELECT 0</c>
    /// (el doble de conteo).
    /// </summary>
    private sealed class ComandosObservados(bool conteoEnCero = false) : DbCommandInterceptor
    {
        public int Ejecutados => Textos.Count;

        /// <summary>El SQL de cada comando, tal como lo armó EF Core.</summary>
        public List<string> Textos { get; } = [];

        /// <summary>Los valores de los parámetros de cada comando, en el mismo orden que <see cref="Textos"/>.</summary>
        public List<List<object?>> Parametros { get; } = [];

        public int ConteosReemplazados { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Observar(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observar(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Observar(command);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Observar(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Observar(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Observar(command);
            return ValueTask.FromResult(result);
        }

        private void Observar(DbCommand comando)
        {
            Textos.Add(comando.CommandText);
            Parametros.Add([.. comando.Parameters.Cast<DbParameter>().Select(parametro => parametro.Value)]);
            if (conteoEnCero && comando.CommandText.StartsWith("SELECT COUNT(", StringComparison.Ordinal))
            {
                comando.CommandText = "SELECT 0";
                comando.Parameters.Clear();
                ConteosReemplazados++;
            }
        }
    }

    /// <summary>Todo lo que registra EF Core, en cualquier categoría y nivel, con la excepción si la hay.</summary>
    private sealed class RegistroCapturado : ILoggerProvider
    {
        public ConcurrentQueue<string> Mensajes { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Registrador(Mensajes);

        public void Dispose()
        {
        }

        private sealed class Registrador(ConcurrentQueue<string> mensajes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                mensajes.Enqueue(formatter(state, exception) + exception);
        }
    }
}
