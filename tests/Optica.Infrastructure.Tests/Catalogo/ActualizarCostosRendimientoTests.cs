using System.Diagnostics;
using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;
using Optica.Infrastructure.Persistencia;
using Optica.Infrastructure.Planillas;
using Optica.Pruebas.Compartido;
using Xunit.Abstractions;

namespace Optica.Infrastructure.Tests.Catalogo;

/// <summary>
/// AC-13 (NFR-01, R-04 de FEAT-011) sobre SQLite real en un directorio temporal: con 10.000 artículos sembrados, una
/// planilla .xlsx de 10.000 filas armada con el SDK de OpenXml pasa por el lector real, el dominio y el repositorio real.
/// Mide el total del caso de uso (lectura incluida) y, aparte, el tiempo dentro de la transacción de escritura, que es
/// el que bloquea las demás escrituras (la espera de SQLite es de 30 s). Datos ficticios.
/// </summary>
public sealed class ActualizarCostosRendimientoTests(ITestOutputHelper salida)
{
    private static readonly TimeSpan LimiteTotal = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan LimiteTransaccion = TimeSpan.FromSeconds(10);

    private const string SelectArticulos =
        "SELECT CodigoProveedor, Descripcion, PrecioCostoCentavos, MargenCentesimos, PrecioVentaCentavos, Activo, " +
        "Version FROM Articulo ORDER BY Id";

    [Fact]
    [Trait("Categoria", "Volumen")]
    public async Task Planilla10000Filas_Catalogo10000_MenosDe120SegundosY10EnTransaccion()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = baseTemporal.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion;
        var sembrados = SiembraCatalogo.Generar();
        SiembraCatalogo.Sembrar(cadena, sembrados);

        // Costo nuevo: el sembrado + 10,25. Los códigos van en minúsculas con espacios en la mitad de las filas (FR-07).
        var costosNuevos = sembrados.Select(s => s.PrecioCosto + 10.25m).ToList();
        var archivo = Planilla(sembrados.Select((s, i) => (
            i % 2 == 0 ? s.CodigoProveedor : " " + s.CodigoProveedor.ToLowerInvariant() + " ",
            costosNuevos[i])).ToList());
        salida.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"Planilla de {SiembraCatalogo.Volumen} filas: {archivo.Length} bytes."));

        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadCronometrada(new UnidadDeTrabajoEf(contexto));
        var caso = new ActualizarCostosPorPlanilla(
            new LectorPlanillaCostosXlsx(), new ConfiguracionNegocioRepository(contexto), new ArticuloRepository(contexto),
            unidad, TimeProvider.System, NullLoggerFactory.Instance);

        var reloj = Stopwatch.StartNew();
        var resultado = await caso.EjecutarAsync(archivo);
        reloj.Stop();
        salida.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Total: {reloj.Elapsed.TotalMilliseconds:F0} ms; dentro de la transacción: " +
            $"{unidad.EnTransaccion.TotalMilliseconds:F0} ms."));

        var aplicada = Assert.IsType<ResultadoPlanillaCostos.Aplicada>(resultado);
        Assert.Equal(SiembraCatalogo.Volumen, aplicada.CantidadFilasLeidas);
        Assert.Equal(SiembraCatalogo.Volumen, aplicada.CantidadAplicadas);
        Assert.Empty(aplicada.NoAplicadas);
        Assert.Equal(1_000, aplicada.ParaRevisar.Count);
        Assert.All(aplicada.ParaRevisar, fila => Assert.True(fila.Inactivo));
        Assert.Equal(1, unidad.Operaciones);
        Assert.True(
            reloj.Elapsed < LimiteTotal,
            string.Create(CultureInfo.InvariantCulture, $"La carga tardó {reloj.Elapsed.TotalMilliseconds:F0} ms."));
        Assert.True(
            unidad.EnTransaccion < LimiteTransaccion,
            string.Create(
                CultureInfo.InvariantCulture,
                $"La transacción tardó {unidad.EnTransaccion.TotalMilliseconds:F0} ms."));

        // Cada artículo con su costo nuevo y la venta de la calculadora, el mismo estado y la versión 1.
        var vigente = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);
        var filas = Filas(cadena);
        Assert.Equal(sembrados.Count, filas.Count);
        for (var i = 0; i < filas.Count; i++)
        {
            var venta = CalculadoraPrecioVenta.Calcular(costosNuevos[i], sembrados[i].MargenUtilidad, vigente)
                .Precio!.PrecioVenta;
            Assert.Equal(
                new[]
                {
                    sembrados[i].CodigoProveedor,
                    sembrados[i].Descripcion,
                    Texto(SiembraCatalogo.Centesimos(costosNuevos[i])),
                    Texto(SiembraCatalogo.Centesimos(sembrados[i].MargenUtilidad)),
                    Texto(SiembraCatalogo.Centesimos(venta)),
                    sembrados[i].Activo ? "1" : "0",
                    "1",
                },
                filas[i]);
        }
    }

    private static string Texto(long valor) => valor.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Un .xlsx como el que guarda Excel: los encabezados y los códigos en la tabla de cadenas compartidas, los precios
    /// como celdas numéricas con cultura invariante.
    /// </summary>
    private static byte[] Planilla(IReadOnlyList<(string Codigo, decimal Precio)> filas)
    {
        using var memoria = new MemoryStream();
        using (var documento = SpreadsheetDocument.Create(memoria, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var libro = documento.AddWorkbookPart();
            libro.Workbook = new Workbook(new Sheets());
            var compartidas = libro.AddNewPart<SharedStringTablePart>();
            var tabla = new SharedStringTable(
                new SharedStringItem(new Text(PlanillaCostos.EncabezadoCodigo)),
                new SharedStringItem(new Text(PlanillaCostos.EncabezadoPrecio)));
            var datos = new SheetData(new Row(Compartida("A1", 0), Compartida("B1", 1)) { RowIndex = 1 });
            for (var i = 0; i < filas.Count; i++)
            {
                var numero = (uint)i + 2;
                tabla.Append(new SharedStringItem(new Text(filas[i].Codigo)
                {
                    Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve,
                }));
                datos.Append(new Row(
                    Compartida(string.Create(CultureInfo.InvariantCulture, $"A{numero}"), i + 2),
                    new Cell
                    {
                        CellReference = string.Create(CultureInfo.InvariantCulture, $"B{numero}"),
                        CellValue = new CellValue(filas[i].Precio.ToString(CultureInfo.InvariantCulture)),
                    })
                { RowIndex = numero });
            }

            compartidas.SharedStringTable = tabla;
            var hoja = libro.AddNewPart<WorksheetPart>();
            hoja.Worksheet = new Worksheet(datos);
            libro.Workbook.Sheets!.Append(new Sheet { Id = libro.GetIdOfPart(hoja), SheetId = 1, Name = "Costos" });
        }

        return memoria.ToArray();
    }

    private static Cell Compartida(string referencia, int indice) => new()
    {
        CellReference = referencia,
        DataType = CellValues.SharedString,
        CellValue = new CellValue(indice.ToString(CultureInfo.InvariantCulture)),
    };

    private static List<string[]> Filas(string cadena)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = SelectArticulos;
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

    /// <summary>
    /// Decorador de la unidad de trabajo que cronometra <see cref="EjecutarEnTransaccionAsync"/>: el tiempo con el
    /// bloqueo de escritura tomado (desde <c>BEGIN IMMEDIATE</c> hasta el commit o la reversión).
    /// </summary>
    private sealed class UnidadCronometrada(IUnidadDeTrabajo real) : IUnidadDeTrabajo
    {
        public TimeSpan EnTransaccion { get; private set; }

        public int Operaciones { get; private set; }

        public async Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default)
        {
            Operaciones++;
            var reloj = Stopwatch.StartNew();
            try
            {
                return await real.EjecutarEnTransaccionAsync(operacion, cancelacion);
            }
            finally
            {
                EnTransaccion += reloj.Elapsed;
            }
        }

        public Task ConfirmarAsync(CancellationToken cancelacion = default) => real.ConfirmarAsync(cancelacion);
    }
}
