using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Facturas sobre SQLite (Block 4 de FEAT-007): alta con sus líneas, lectura, unicidad por presupuesto y por comprobante,
/// y filas ilegibles. Todos los datos del cliente son ficticios.
/// </summary>
public class FacturaRepositoryTests
{
    private const string MensajeFilaInvalida = "La factura guardada no es válida.";

    [Fact]
    public async Task Agregar_Factura_SeLeeConSusLineasYEntorno()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        var factura = FacturasPrueba.FacturaB(presupuesto, numero: 125, EntornoArca.Homologacion, identificado: true);

        await FacturasPrueba.GrabarFactura(baseTemporal, factura);

        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());
        Assert.True(await repositorio.ExisteParaPresupuestoAsync(1));
        var leida = await repositorio.ObtenerPorPresupuestoAsync(1);

        // FR-18: tipo, punto de venta, número, fecha, concepto, receptor, importes, CAE, vencimiento, entorno y líneas.
        Assert.NotNull(leida);
        Assert.Equal(1, leida.PresupuestoNumero);
        Assert.Equal("Factura B 0003-00000125", leida.Comprobante.ToString());
        Assert.Equal(new DateOnly(2026, 10, 30), leida.Fecha);
        Assert.Equal(ConceptoFactura.Productos, leida.Concepto);
        Assert.Equal(FacturasPrueba.Dni, leida.Receptor.Dni);
        Assert.Equal(3000m, leida.Importes.Total);
        Assert.Equal(2479.34m, leida.Importes.Neto);
        Assert.Equal(520.66m, leida.Importes.Iva);
        Assert.Equal(21m, leida.Importes.Alicuota);
        Assert.Equal(FacturasPrueba.Cae, leida.Cae);
        Assert.Equal(new DateOnly(2026, 11, 9), leida.VencimientoCae);
        Assert.Equal(EntornoArca.Homologacion, leida.Entorno);
        Assert.Equal(
            presupuesto.Lineas.Select(l => (l.CodigoArticulo, l.Descripcion, l.PrecioUnitario, l.Cantidad,
                l.PorcentajeDescuento, l.PrecioConDescuento, l.PrecioFinal)),
            leida.Lineas.Select(l => (l.CodigoArticulo, l.Descripcion, l.PrecioUnitario, l.Cantidad,
                l.PorcentajeDescuento, l.PrecioConDescuento, l.PrecioFinal)));
        Assert.Equal([1, 2, 3], leida.Lineas.Select(l => l.Orden));

        // Sin líneas ni DNI: solo la identificación (FR-20).
        var comprobante = await repositorio.ObtenerComprobantePorPresupuestoAsync(1);
        Assert.Equal("Factura B 0003-00000125", comprobante?.ToString());

        // Las filas: enums en minúscula, importes en centavos y la cantidad de líneas.
        Assert.Equal(
            [["1", "b", "3", "125", "2026-10-30", "productos", FacturasPrueba.Dni, "300000", "247934", "52066", "2100",
              FacturasPrueba.Cae, "2026-11-09", "homologacion", "3"]],
            FacturasPrueba.Filas(cadena,
                "SELECT PresupuestoNumero, Tipo, PuntoVenta, Numero, Fecha, Concepto, ReceptorDni, TotalCentavos, " +
                "NetoCentavos, IvaCentavos, AlicuotaCentesimas, Cae, VencimientoCae, Entorno, CantidadLineas FROM Factura"));
        Assert.Equal(
            [["1", "100000", "1", "0", "100000", "100000"], ["2", "125000", "1", "2000", "100000", "100000"],
             ["3", "50000", "2", "0", "50000", "100000"]],
            FacturasPrueba.Filas(cadena,
                "SELECT Orden, PrecioUnitarioCentavos, Cantidad, DescuentoCentesimos, PrecioConDescuentoCentavos, " +
                "PrecioFinalCentavos FROM LineaFactura ORDER BY Orden"));

        // Un presupuesto sin factura.
        Assert.Null(await repositorio.ObtenerPorPresupuestoAsync(2));
        Assert.Null(await repositorio.ObtenerComprobantePorPresupuestoAsync(2));
        Assert.False(await repositorio.ExisteParaPresupuestoAsync(2));
    }

    [Fact]
    public async Task Agregar_FacturaCSinIdentificar_SeLeeSinAlicuotaNiDni()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 4);
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaC, 99999, new DateOnly(2026, 10, 30), ConceptoFactura.ProductosYServicios,
            ReceptorFactura.SinIdentificar, ImportesFactura.ParaFacturaC(presupuesto.Total));
        var factura = Factura.Emitida(
            presupuesto, solicitud, 99_999_999, FacturasPrueba.Cae, new DateOnly(2026, 10, 30), EntornoArca.Simulado);

        await FacturasPrueba.GrabarFactura(baseTemporal, factura);

        var leida = await new FacturaRepository(baseTemporal.CrearContexto()).ObtenerPorPresupuestoAsync(4);
        Assert.NotNull(leida);
        Assert.Equal("Factura C 99999-99999999", leida.Comprobante.ToString());
        Assert.Equal(ConceptoFactura.ProductosYServicios, leida.Concepto);
        Assert.False(leida.Receptor.EstaIdentificado);
        Assert.Null(leida.Importes.Alicuota);
        Assert.Equal(3000m, leida.Importes.Neto);
        Assert.Equal(0m, leida.Importes.Iva);
        Assert.Equal(EntornoArca.Simulado, leida.Entorno);
        Assert.Equal(
            [["c", "productos_y_servicios", "NULL", "NULL", "simulado"]],
            FacturasPrueba.Filas(cadena,
                "SELECT Tipo, Concepto, IFNULL(ReceptorDni, 'NULL'), IFNULL(AlicuotaCentesimas, 'NULL'), Entorno " +
                "FROM Factura"));
    }

    [Fact]
    public async Task Agregar_SegundaFacturaMismoPresupuesto_ConflictoDuplicado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var presupuesto = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        await FacturasPrueba.GrabarFactura(
            baseTemporal, FacturasPrueba.FacturaB(presupuesto, numero: 125, EntornoArca.Homologacion, identificado: true));
        Assert.True(await new FacturaRepository(baseTemporal.CrearContexto()).ExisteParaPresupuestoAsync(1));

        // AC-03 en la base: otra factura (otro número) para el mismo presupuesto choca con IX_Factura_PresupuestoNumero.
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(() => FacturasPrueba.GrabarFactura(
            baseTemporal, FacturasPrueba.FacturaB(presupuesto, numero: 126, EntornoArca.Homologacion, identificado: true)));

        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.Equal([["1", "125"]], FacturasPrueba.Filas(cadena, "SELECT count(*), max(Numero) FROM Factura"));
        Assert.Equal([["3"]], FacturasPrueba.Filas(cadena, "SELECT count(*) FROM LineaFactura"));
    }

    [Fact]
    public async Task Agregar_MismoComprobanteEnElMismoEntorno_ConflictoDuplicado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        var primero = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 1);
        var segundo = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 2);
        var tercero = await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 3);
        await FacturasPrueba.GrabarFactura(
            baseTemporal, FacturasPrueba.FacturaB(primero, numero: 125, EntornoArca.Homologacion, identificado: false));

        // IX_Factura_Comprobante (Entorno, Tipo, PuntoVenta, Numero): el mismo comprobante en el mismo entorno choca.
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(() => FacturasPrueba.GrabarFactura(
            baseTemporal, FacturasPrueba.FacturaB(segundo, numero: 125, EntornoArca.Homologacion, identificado: false)));
        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);

        // En otro entorno el mismo número es otro comprobante.
        await FacturasPrueba.GrabarFactura(
            baseTemporal, FacturasPrueba.FacturaB(tercero, numero: 125, EntornoArca.Simulado, identificado: false));
        Assert.Equal(
            [["1", "homologacion"], ["3", "simulado"]],
            FacturasPrueba.Filas(cadena, "SELECT PresupuestoNumero, Entorno FROM Factura ORDER BY PresupuestoNumero"));
    }

    public static TheoryData<string, object?> ValoresIlegibles => new()
    {
        { "Tipo", "a" },
        { "Entorno", "produccion_vieja" },
        { "Concepto", "servicios" },
        { "ReceptorDni", "12345" },
        { "ReceptorDni", "1234567A" },
        { "Cae", "7123456789012" },
        { "Fecha", "2026-02-30" },
        { "VencimientoCae", "2026-13-01" },
        { "PuntoVenta", 100000L },
        { "Numero", 100_000_000L },
        { "IvaCentavos", 52067L },
        { "AlicuotaCentesimas", 1900L },
        { "AlicuotaCentesimas", null },
        { "CantidadLineas", 2L },
        { "Linea.Orden", 2L },
        { "Linea.Cantidad", 0L },
        { "Linea.Cantidad", 3_000_000_000L },
        { "Linea.Descripcion", " Armazón" },
        { "Linea.PrecioFinalCentavos", 299999L },
    };

    [Theory]
    [MemberData(nameof(ValoresIlegibles))]
    public async Task Obtener_FilaIlegible_ErrorSinValores(string columna, object? valor)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        Assert.Contains(["Factura"], FacturasPrueba.Filas(cadena, "SELECT name FROM sqlite_master WHERE type = 'table'"));
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 7);
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 8);

        // Una fila válida se lee (presupuesto 7); la misma con un valor que eludió los CHECK es ilegible (presupuesto 8).
        // Se insertan directo en la tabla: la factura no admite UPDATE (FR-25).
        FacturasPrueba.InsertarFacturaCruda(
            cadena, FacturasPrueba.ValoresFactura(7, id: 1, numero: 125),
            [FacturasPrueba.ValoresLinea(facturaId: 1, orden: 1, precioFinalCentavos: 300000L)]);
        Assert.NotNull(await new FacturaRepository(baseTemporal.CrearContexto()).ObtenerPorPresupuestoAsync(7));

        var factura = FacturasPrueba.ValoresFactura(8, id: 2, numero: 126);
        var linea = FacturasPrueba.ValoresLinea(facturaId: 2, orden: 1, precioFinalCentavos: 300000L);
        if (columna.StartsWith("Linea.", StringComparison.Ordinal))
        {
            linea[columna["Linea.".Length..]] = valor;
        }
        else
        {
            factura[columna] = valor;
        }

        FacturasPrueba.InsertarFacturaCruda(cadena, factura, [linea], ignorarChecks: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new FacturaRepository(baseTemporal.CrearContexto()).ObtenerPorPresupuestoAsync(8));

        Assert.Equal(MensajeFilaInvalida, error.Message);
        var texto = error.ToString();
        Assert.DoesNotContain(FacturasPrueba.Dni, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(FacturasPrueba.Cae, texto, StringComparison.Ordinal);
        if (valor is string { Length: >= 5 } textoValor)
        {
            Assert.DoesNotContain(textoValor.Trim(), texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ObtenerComprobante_FilaIlegible_ErrorSinValores()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        Assert.Contains(["Factura"], FacturasPrueba.Filas(cadena, "SELECT name FROM sqlite_master WHERE type = 'table'"));
        await FacturasPrueba.GrabarPresupuestoFinal(baseTemporal, 7);
        var factura = FacturasPrueba.ValoresFactura(7, id: 1, numero: 125);
        factura["Tipo"] = "z";
        FacturasPrueba.InsertarFacturaCruda(
            cadena, factura, [FacturasPrueba.ValoresLinea(facturaId: 1, orden: 1, precioFinalCentavos: 300000L)],
            ignorarChecks: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new FacturaRepository(baseTemporal.CrearContexto()).ObtenerComprobantePorPresupuestoAsync(7));

        Assert.Equal(MensajeFilaInvalida, error.Message);
    }

    [Fact]
    public void Agregar_Null_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var contexto = baseTemporal.CrearContexto();

        Assert.Throws<ArgumentNullException>(() => new FacturaRepository(contexto).Agregar(null!));
        Assert.False(contexto.ChangeTracker.HasChanges());
    }
}

/// <summary>Datos y ayudas compartidas por las pruebas de facturación sobre SQLite. Cliente ficticio.</summary>
internal static class FacturasPrueba
{
    /// <summary>DNI que no se emite (ceros a la izquierda) y que la regla de 7 u 8 dígitos admite.</summary>
    public const string Dni = "00718294";
    public const string Cae = "71234567890123";

    public static string CadenaDe(BaseTemporal baseTemporal) =>
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion;

    /// <summary>
    /// Graba un presupuesto de 3 líneas con total 3.000 en Borrador y lo pasa a Final, como la app (AGENTS.md). Devuelve
    /// el presupuesto leído en Final.
    /// </summary>
    public static async Task<Presupuesto> GrabarPresupuestoFinal(BaseTemporal baseTemporal, long numero)
    {
        var contextoArticulo = baseTemporal.CrearContexto();
        var articulos = new ArticuloRepository(contextoArticulo);
        var codigoProveedor = string.Create(CultureInfo.InvariantCulture, $"ARM-{numero}");
        articulos.Agregar(Articulo.Nuevo(codigoProveedor, "Armazón metálico", 1000m, 0m, 1000m));
        await new UnidadDeTrabajoEf(contextoArticulo).ConfirmarAsync();
        var codigo = (await articulos.ObtenerPorClaveAsync(NormalizacionCodigo.Clave(codigoProveedor)))!.Codigo!.Value;

        var cliente = new ClientePresupuesto("Ficticiez Quimera", "Prueboncia", Dni, null, null, null);
        var borrador = Presupuesto.Nuevo(
            numero, new DateOnly(2026, 10, 29), cliente,
            [
                new LineaPresupuesto(codigo, "Armazón metálico", 1000m, 1, 0m, 1000m, 1000m),
                new LineaPresupuesto(codigo, "Cristal orgánico", 1250m, 1, 20m, 1000m, 1000m),
                new LineaPresupuesto(codigo, "Estuche", 500m, 2, 0m, 500m, 1000m),
            ]);
        var contextoAlta = baseTemporal.CrearContexto();
        new PresupuestoRepository(contextoAlta).Agregar(borrador);
        await new UnidadDeTrabajoEf(contextoAlta).ConfirmarAsync();

        var contextoFinal = baseTemporal.CrearContexto();
        var presupuestos = new PresupuestoRepository(contextoFinal);
        var leido = (await presupuestos.ObtenerAsync(numero))!;
        presupuestos.CambiarEstado(leido.PasarAFinal(), leido.Version);
        await new UnidadDeTrabajoEf(contextoFinal).ConfirmarAsync();

        return (await new PresupuestoRepository(baseTemporal.CrearContexto()).ObtenerAsync(numero))!;
    }

    /// <summary>Una Factura B al 21 % del presupuesto, punto de venta 3, del 30/10/2026, con el CAE de prueba.</summary>
    public static Factura FacturaB(Presupuesto presupuesto, long numero, EntornoArca entorno, bool identificado)
    {
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaB, 3, new DateOnly(2026, 10, 30), ConceptoFactura.Productos,
            identificado ? ReceptorFactura.ConDni(presupuesto.Cliente.Dni) : ReceptorFactura.SinIdentificar,
            ImportesFactura.ParaFacturaB(presupuesto.Total, 21m));
        return Factura.Emitida(presupuesto, solicitud, numero, Cae, new DateOnly(2026, 11, 9), entorno);
    }

    public static async Task GrabarFactura(BaseTemporal baseTemporal, Factura factura)
    {
        var contexto = baseTemporal.CrearContexto();
        new FacturaRepository(contexto).Agregar(factura);
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    /// <summary>Las columnas de una Factura B válida de 3.000 al 21 %, con una sola línea.</summary>
    public static Dictionary<string, object?> ValoresFactura(long presupuestoNumero, long id, long numero) =>
        new(StringComparer.Ordinal)
    {
        ["Id"] = id,
        ["PresupuestoNumero"] = presupuestoNumero,
        ["Tipo"] = "b",
        ["PuntoVenta"] = 3L,
        ["Numero"] = numero,
        ["Fecha"] = "2026-10-30",
        ["Concepto"] = "productos",
        ["ReceptorDni"] = Dni,
        ["TotalCentavos"] = 300000L,
        ["NetoCentavos"] = 247934L,
        ["IvaCentavos"] = 52066L,
        ["AlicuotaCentesimas"] = 2100L,
        ["Cae"] = Cae,
        ["VencimientoCae"] = "2026-11-09",
        ["Entorno"] = "homologacion",
        ["CantidadLineas"] = 1L,
    };

    public static Dictionary<string, object?> ValoresLinea(long facturaId, long orden, long precioFinalCentavos) =>
        new(StringComparer.Ordinal)
        {
            ["FacturaId"] = facturaId,
            ["Orden"] = orden,
            ["CodigoArticulo"] = 1L,
            ["Descripcion"] = "Armazón metálico",
            ["PrecioUnitarioCentavos"] = precioFinalCentavos,
            ["PrecioConDescuentoCentavos"] = precioFinalCentavos,
            ["PrecioFinalCentavos"] = precioFinalCentavos,
            ["Cantidad"] = 1L,
            ["DescuentoCentesimos"] = 0L,
        };

    /// <summary>
    /// Inserta la factura y sus líneas con parámetros, sin pasar por el repositorio; con
    /// <paramref name="ignorarChecks"/>, sin los CHECK (para simular una fila que los eludió).
    /// </summary>
    public static void InsertarFacturaCruda(
        string cadena, Dictionary<string, object?> factura, IEnumerable<Dictionary<string, object?>> lineas,
        bool ignorarChecks = false)
    {
        var sinGrupo = new SqliteConnectionStringBuilder(cadena) { Pooling = false }.ToString();
        using var conexion = new SqliteConnection(sinGrupo);
        conexion.Open();
        if (ignorarChecks)
        {
            using var pragma = conexion.CreateCommand();
            pragma.CommandText = "PRAGMA ignore_check_constraints = ON";
            pragma.ExecuteNonQuery();
        }

        Insertar(conexion, "Factura", factura);
        foreach (var linea in lineas)
        {
            Insertar(conexion, "LineaFactura", linea);
        }
    }

    public static void Insertar(SqliteConnection conexion, string tabla, Dictionary<string, object?> valores)
    {
        using var comando = conexion.CreateCommand();
        var columnas = valores.Keys.ToList();
        comando.CommandText =
            $"INSERT INTO \"{tabla}\" ({string.Join(", ", columnas.Select(c => $"\"{c}\""))}) " +
            $"VALUES ({string.Join(", ", columnas.Select((_, i) => $"$p{i}"))})";
        for (var i = 0; i < columnas.Count; i++)
        {
            comando.Parameters.AddWithValue($"$p{i}", valores[columnas[i]] ?? DBNull.Value);
        }

        comando.ExecuteNonQuery();
    }

    public static void Ejecutar(string cadena, string sentencia)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = sentencia;
        comando.ExecuteNonQuery();
    }

    public static List<string[]> Filas(string cadena, string consulta)
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
}
