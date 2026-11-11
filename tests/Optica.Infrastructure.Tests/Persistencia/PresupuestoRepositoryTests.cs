using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        "UPDATE Presupuesto SET Fecha = '2026-02-30'",
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

        // AC-12: el presupuesto nuevo queda en Borrador. El estado Final se guarda como 'final' (D-19).
        await GrabarPresupuesto(baseTemporal, Presupuesto.Nuevo(1, Fecha, ClienteCompleto(), [Linea(articulo)]));
        await GrabarPresupuesto(baseTemporal,
            new Presupuesto(2, Fecha, EstadoPresupuesto.Final, ClienteCompleto(), [Linea(articulo)]));

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
        var migraciones = contexto.Database.GetMigrations().ToList();
        var presupuestos = migraciones[^1];
        Assert.EndsWith("_Presupuestos", presupuestos, StringComparison.Ordinal);
        var anterior = migraciones[^2];
        Assert.EndsWith("_CatalogoActivo", anterior, StringComparison.Ordinal);

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

        // "nombre tipo notnull pk default", por nombre. Sin DEFAULT: el repositorio asigna todas las columnas.
        Assert.Equal(
            [
                "Apellido TEXT 1 0 ", "Dni TEXT 1 0 ", "Domicilio TEXT 0 0 ", "Email TEXT 0 0 ", "Estado TEXT 1 0 ",
                "Fecha TEXT 1 0 ", "Nombre TEXT 1 0 ", "Numero INTEGER 1 1 ", "Telefono TEXT 0 0 ",
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
        Assert.DoesNotContain("DEFAULT", sqlPresupuesto, StringComparison.OrdinalIgnoreCase);
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

        // El índice de la clave foránea al catálogo, no único; sin triggers (llegan con FEAT-003c).
        Assert.Equal([["IX_LineaPresupuesto_CodigoArticulo", "0"]],
            Filas(cadena,
                "SELECT name, \"unique\" FROM pragma_index_list('LineaPresupuesto') WHERE name LIKE 'IX%'"));
        Assert.Equal([["CodigoArticulo"]],
            Filas(cadena, "SELECT name FROM pragma_index_info('IX_LineaPresupuesto_CodigoArticulo')"));
        Assert.Empty(Filas(cadena, "SELECT name FROM pragma_index_list('Presupuesto') WHERE name LIKE 'IX%'"));
        Assert.Empty(Filas(cadena,
            "SELECT name FROM sqlite_master WHERE type = 'trigger' AND tbl_name IN ('Presupuesto', 'LineaPresupuesto')"));
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
        Filas(cadena, "SELECT name FROM sqlite_master WHERE type = 'trigger' ORDER BY name"),
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
