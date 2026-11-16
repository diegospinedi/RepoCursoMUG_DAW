using Optica.Domain.Facturacion;
using Optica.Infrastructure.Persistencia;
using Optica.Pruebas.Compartido;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Spec FEAT-009, Block 6: la búsqueda de facturas sobre SQLite. Junta cada factura con su presupuesto (Apellido, Nombre y
/// DNI del cliente del presupuesto, también a Consumidor Final, FR-25), combina los filtros con "Y" (FR-18), compara
/// Apellido y Nombre con <c>instr(plegar(...))</c> (FR-19, ADR-011), DNI y número por coincidencia parcial sobre los
/// dígitos del texto visible (FR-20, D-27), y ordena por fecha, número e Id descendentes (FR-22, D-20). Datos ficticios
/// del PRD con DNI que empiezan con 00 (D-08).
/// </summary>
public sealed class BusquedaFacturasTests
{
    private static readonly DateOnly Diez = new(2026, 3, 10);
    private static readonly DateOnly Quince = new(2026, 3, 15);
    private static readonly DateOnly Veinte = new(2026, 3, 20);

    /// <summary>AC-17: con 120 facturas, la primera página tiene las 50 más recientes y la tercera, las 20 más antiguas.</summary>
    [Fact]
    public async Task Repositorio_120Facturas_Paginas50y20()
    {
        using var baseTemporal = new BaseTemporal();
        await SiembraFacturas.SembrarAsync(baseTemporal.Servicios, SiembraFacturas.DelVolumen());
        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());
        var sinFiltros = FiltroFacturas.Crear(null, null, null, null, null, null);

        var primera = await repositorio.BuscarAsync(sinFiltros, 1, 50);
        var tercera = await repositorio.BuscarAsync(sinFiltros, 3, 50);
        var cuarta = await repositorio.BuscarAsync(sinFiltros, 4, 50);

        Assert.Equal(120, primera.Total);
        Assert.Equal(Enumerable.Range(71, 50).Reverse().Select(n => (long)n), primera.Facturas.Select(f => f.PresupuestoNumero));
        Assert.Equal(SiembraFacturas.PrimeraFecha.AddDays(119), primera.Facturas[0].Fecha);
        Assert.Equal(120, tercera.Total);
        Assert.Equal(Enumerable.Range(1, 20).Reverse().Select(n => (long)n), tercera.Facturas.Select(f => f.PresupuestoNumero));
        Assert.Equal("Factura B 0003-00000001", tercera.Facturas[^1].Comprobante.ToString());
        Assert.Empty(cuarta.Facturas);
        Assert.Equal(120, cuarta.Total);
    }

    /// <summary>
    /// D-20: el número solo es único por entorno, tipo y punto de venta. Con fecha y número repetidos, el Id desempata:
    /// recorriendo las páginas no se repite ni se pierde ninguna factura.
    /// </summary>
    [Fact]
    public async Task Repositorio_EmpatesDeFechaYNumero_SinRepetirNiPerderEntrePaginas()
    {
        using var baseTemporal = new BaseTemporal();
        await SiembraFacturas.SembrarAsync(
            baseTemporal.Servicios,
            [
                Sembrada(1, Veinte, "Gómez", "Ana", "00111111", puntoVenta: 1, numero: 7),
                Sembrada(2, Veinte, "Gómez", "Ana", "00111111", puntoVenta: 2, numero: 7),
                Sembrada(3, Diez, "Gómez", "Ana", "00111111", puntoVenta: 1, numero: 9),
                Sembrada(4, Veinte, "Gómez", "Ana", "00111111", puntoVenta: 3, numero: 7),
                Sembrada(5, Veinte, "Gómez", "Ana", "00111111", puntoVenta: 4, numero: 8),
                Sembrada(6, Veinte, "Gómez", "Ana", "00111111", puntoVenta: 5, numero: 7, tipo: TipoComprobante.FacturaC),
            ]);
        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());
        var sinFiltros = FiltroFacturas.Crear(null, null, null, null, null, null);

        var recorridas = new List<long>();
        for (var pagina = 1; pagina <= 3; pagina++)
        {
            var resultado = await repositorio.BuscarAsync(sinFiltros, pagina, 2);
            Assert.Equal(6, resultado.Total);
            Assert.Equal(2, resultado.Facturas.Count);
            recorridas.AddRange(resultado.Facturas.Select(f => f.PresupuestoNumero));
        }

        // Fecha desc, número desc y, con los dos iguales, el Id (el orden de alta) desc.
        Assert.Equal([5L, 6L, 4L, 2L, 1L, 3L], recorridas);
    }

    /// <summary>
    /// AC-19 con un punto de venta de 5 dígitos: el texto visible es "12345-00000007" (D4 no recorta), y sus dígitos son
    /// los que se buscan; con 4 dígitos, "0003-00000125".
    /// </summary>
    [Fact]
    public async Task Repositorio_PuntoDeVentaDeCincoDigitos_PorNumero()
    {
        using var baseTemporal = new BaseTemporal();
        await SiembraFacturas.SembrarAsync(
            baseTemporal.Servicios,
            [
                Sembrada(1, Diez, "González", "Ana", "00111111", puntoVenta: 12345, numero: 7),
                Sembrada(2, Diez, "González", "Ana", "00111111", puntoVenta: 3, numero: 125),
                Sembrada(3, Diez, "González", "Ana", "00111111", puntoVenta: 1234, numero: 5_000_000),
            ]);
        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());

        Assert.Equal([1L], await PresupuestosPorNumero(repositorio, "12345-00000007"));
        Assert.Equal([1L], await PresupuestosPorNumero(repositorio, "1234500"));
        Assert.Equal([2L], await PresupuestosPorNumero(repositorio, "0003-00000125"));
        Assert.Equal([3L], await PresupuestosPorNumero(repositorio, "1234-05000000"));
        Assert.Equal([3L, 2L, 1L], await PresupuestosPorNumero(repositorio, "000"));
        Assert.Empty(await PresupuestosPorNumero(repositorio, "12345-0000007"));
    }

    /// <summary>AC-18: Apellido "gonzalez" con Desde 15/03/2026 encuentra solo la factura de "González" del 20/03.</summary>
    [Fact]
    public async Task Repositorio_ApellidoYDesde_SoloGonzalez20()
    {
        using var baseTemporal = new BaseTemporal();
        await SiembraFacturas.SembrarAsync(
            baseTemporal.Servicios,
            [
                Sembrada(1, Diez, "González", "Ana", "00111111", puntoVenta: 3, numero: 1),
                Sembrada(2, Veinte, "González", "Ana", "00111111", puntoVenta: 3, numero: 2),
                Sembrada(3, Veinte, "Gómez", "Ana", "00111111", puntoVenta: 3, numero: 3),
            ]);
        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());

        var resultado = await repositorio.BuscarAsync(
            FiltroFacturas.Crear("gonzalez", null, null, null, Quince, null), 1, 50);

        var unica = Assert.Single(resultado.Facturas);
        Assert.Equal((2L, Veinte, "González"), (unica.PresupuestoNumero, unica.Fecha, unica.Apellido));
        Assert.Equal(1, resultado.Total);

        // Los dos límites son inclusive (FR-21).
        var rango = await repositorio.BuscarAsync(FiltroFacturas.Crear(null, null, null, null, Diez, Veinte), 1, 50);
        Assert.Equal([3L, 2L, 1L], rango.Facturas.Select(f => f.PresupuestoNumero));
        var hasta = await repositorio.BuscarAsync(FiltroFacturas.Crear(null, null, null, null, null, Diez), 1, 50);
        Assert.Equal([1L], hasta.Facturas.Select(f => f.PresupuestoNumero));
    }

    /// <summary>AC-19: la factura 0003-00034561 se encuentra por "34561" y no aparecen las demás.</summary>
    [Fact]
    public async Task Repositorio_Numero34561()
    {
        using var baseTemporal = new BaseTemporal();
        await SiembraFacturas.SembrarAsync(
            baseTemporal.Servicios,
            [
                Sembrada(1, Diez, "González", "Ana", "00111111", puntoVenta: 3, numero: 34_561),
                Sembrada(2, Diez, "González", "Ana", "00111111", puntoVenta: 3, numero: 34_560),
                Sembrada(3, Diez, "González", "Ana", "00111111", puntoVenta: 3, numero: 125),
            ]);
        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());

        var resultado = await repositorio.BuscarAsync(FiltroFacturas.Crear(null, null, null, "34561", null, null), 1, 50);

        var unica = Assert.Single(resultado.Facturas);
        Assert.Equal("Factura B 0003-00034561", unica.Comprobante.ToString());
        Assert.Equal(1, resultado.Total);
    }

    /// <summary>AC-20: el cliente con DNI 00345678 se encuentra por "3456" y por "00.345".</summary>
    [Fact]
    public async Task Repositorio_Dni3456y00345()
    {
        using var baseTemporal = new BaseTemporal();
        await SiembraFacturas.SembrarAsync(
            baseTemporal.Servicios,
            [
                Sembrada(1, Diez, "González", "Ana", "00345678", puntoVenta: 3, numero: 1),
                Sembrada(2, Diez, "Gómez", "Ana", "00987654", puntoVenta: 3, numero: 2),
            ]);
        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());

        foreach (var dni in new[] { "3456", "00.345" })
        {
            var resultado = await repositorio.BuscarAsync(FiltroFacturas.Crear(null, null, dni, null, null, null), 1, 50);
            var unica = Assert.Single(resultado.Facturas);
            Assert.Equal((1L, "00345678"), (unica.PresupuestoNumero, unica.Dni));
        }
    }

    /// <summary>AC-21: Nombre "maria" encuentra "María José" y no "Ana".</summary>
    [Fact]
    public async Task Repositorio_NombreMaria_SoloMariaJose()
    {
        using var baseTemporal = new BaseTemporal();
        await SiembraFacturas.SembrarAsync(
            baseTemporal.Servicios,
            [
                Sembrada(1, Diez, "González", "María José", "00345678", puntoVenta: 3, numero: 1),
                Sembrada(2, Diez, "González", "Ana", "00987654", puntoVenta: 3, numero: 2),
            ]);
        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());

        var resultado = await repositorio.BuscarAsync(FiltroFacturas.Crear(null, "maria", null, null, null, null), 1, 50);

        var unica = Assert.Single(resultado.Facturas);
        Assert.Equal("María José", unica.Nombre);
    }

    /// <summary>
    /// AC-26 (FR-25): una factura emitida sin identificar al receptor, del presupuesto de "Pérez, Ana" con DNI 00123456,
    /// se encuentra por ese DNI y muestra Apellido, Nombre y DNI del cliente del presupuesto, con el total.
    /// </summary>
    [Fact]
    public async Task Repositorio_ConsumidorFinal_PorDniDelCliente()
    {
        using var baseTemporal = new BaseTemporal();
        await SiembraFacturas.SembrarAsync(
            baseTemporal.Servicios,
            [
                Sembrada(155, Veinte, "Pérez", "Ana", "00123456", puntoVenta: 3, numero: 125, identificado: false),
                Sembrada(156, Veinte, "Gómez", "Ana", "00987654", puntoVenta: 3, numero: 126),
            ]);
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);
        Assert.Equal([["155", "NULL"]], FacturasPrueba.Filas(
            cadena, "SELECT PresupuestoNumero, IFNULL(ReceptorDni, 'NULL') FROM Factura WHERE PresupuestoNumero = 155"));
        var repositorio = new FacturaRepository(baseTemporal.CrearContexto());

        var resultado = await repositorio.BuscarAsync(
            FiltroFacturas.Crear(null, null, "00123456", null, null, null), 1, 50);

        var unica = Assert.Single(resultado.Facturas);
        Assert.Equal(
            (155L, "Factura B 0003-00000125", Veinte, "Pérez", "Ana", "00123456", SiembraFacturas.TotalSembrado),
            (unica.PresupuestoNumero, unica.Comprobante.ToString(), unica.Fecha, unica.Apellido, unica.Nombre, unica.Dni,
                unica.Total));
    }

    private static FacturaSembrada Sembrada(
        long presupuesto, DateOnly fecha, string apellido, string nombre, string dni, int puntoVenta, long numero,
        bool identificado = true, TipoComprobante tipo = TipoComprobante.FacturaB) =>
        new(presupuesto, fecha, apellido, nombre, dni, puntoVenta, numero, identificado, tipo);

    private static async Task<IEnumerable<long>> PresupuestosPorNumero(FacturaRepository repositorio, string numero) =>
        (await repositorio.BuscarAsync(FiltroFacturas.Crear(null, null, null, numero, null, null), 1, 50))
            .Facturas.Select(f => f.PresupuestoNumero);
}
