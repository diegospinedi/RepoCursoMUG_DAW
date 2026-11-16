using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Spec FEAT-009, Block 6: el caso de uso de la búsqueda de facturas arma el filtro (plegado y normalizado en el dominio,
/// D-13) y pide la página de 50 (FR-22); Desde posterior a Hasta no busca y responde el error junto a <c>desde</c>
/// (AC-22). Datos ficticios del PRD con DNI que empiezan con 00 (D-08).
/// </summary>
public sealed class BuscarFacturasTests
{
    [Fact]
    public async Task BuscarFacturas_FiltroNormalizadoYPaginaDe50()
    {
        var resumen = new ResumenFactura(
            155, new NumeroComprobante(TipoComprobante.FacturaB, 3, 125), new DateOnly(2026, 3, 20), "González",
            "María José", "00345678", 1815m);
        var repositorio = new RepositorioDeBusqueda(new PaginaFacturas([resumen], 51));

        var resultado = await new BuscarFacturas(repositorio).EjecutarAsync(
            " GONZÁLEZ ", "maría", "00.345", "0003-00000125", new DateOnly(2026, 3, 15), null, 2);

        // El filtro llega plegado y sin separadores; la página pedida y el tamaño fijo de 50.
        var filtro = Assert.IsType<FiltroFacturas>(repositorio.Filtro);
        Assert.Equal("gonzalez", filtro.Apellido);
        Assert.Equal("maria", filtro.Nombre);
        Assert.Equal("00345", filtro.Dni);
        Assert.Equal("000300000125", filtro.Numero);
        Assert.Equal(new DateOnly(2026, 3, 15), filtro.Desde);
        Assert.Null(filtro.Hasta);
        Assert.Equal((2, 50), (repositorio.Pagina, repositorio.Tamano));

        // AC-23: el comprobante como texto y el presupuesto de origen; los datos del cliente del presupuesto.
        Assert.True(resultado.EsValido);
        Assert.Equal((2, 50, 51), (resultado.Pagina, resultado.TamanoPagina, resultado.Total));
        var listada = Assert.Single(resultado.Facturas);
        Assert.Equal(
            (155L, "Factura B 0003-00000125", new DateOnly(2026, 3, 20), "González", "María José", "00345678", 1815m),
            (listada.Presupuesto, listada.Comprobante, listada.Fecha, listada.Apellido, listada.Nombre, listada.Dni,
                listada.Total));

        // R-01: ni el resultado ni la factura listada muestran los datos del cliente en ToString.
        Assert.DoesNotContain("00345678", resultado.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("González", listada.ToString(), StringComparison.Ordinal);

        // AC-22: Desde posterior a Hasta no llega al repositorio y el error va junto a Desde.
        var sinBuscar = new RepositorioDeBusqueda(new PaginaFacturas([], 0));
        var invalido = await new BuscarFacturas(sinBuscar).EjecutarAsync(
            null, null, null, null, new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 10), 1);

        Assert.False(invalido.EsValido);
        var error = Assert.Single(invalido.Errores);
        Assert.Equal(("desde", "La fecha Desde no puede ser posterior a Hasta."), (error.Campo, error.Accion));
        Assert.Null(sinBuscar.Filtro);
    }

    /// <summary>
    /// La validación de cada texto que usa la Api en el borde, alineada con el molde de presupuestos
    /// (<c>ValidacionPresupuesto.AgregarErrorDeTexto</c>): primero UTF-16 inválido, después el largo medido con el recorte
    /// de <c>NormalizacionCodigo.Recortar</c> (que también quita U+FEFF), a través de <c>PlegadoCliente</c>.
    /// </summary>
    [Fact]
    public void ErrorDeTexto_RecorteDelMoldeYUtf16Invalido()
    {
        Assert.Null(BuscarFacturas.ErrorDeTexto(null, "apellido"));
        Assert.Null(BuscarFacturas.ErrorDeTexto(" ﻿" + new string('a', 100) + "﻿ ", "apellido"));

        var largo = BuscarFacturas.ErrorDeTexto(new string('a', 101), "numero");
        Assert.Equal(("numero", "Usá hasta 100 caracteres."), (largo?.Campo, largo?.Accion));

        // Un sustituto suelto se rechaza antes del largo, con la acción del molde y sin repetir el valor.
        var invalido = BuscarFacturas.ErrorDeTexto("Zq7\uD800" + new string('a', 200), "dni");
        Assert.Equal(
            ("dni", "El texto tiene caracteres no válidos. Escribilo de nuevo."), (invalido?.Campo, invalido?.Accion));
    }

    /// <summary>Solo la búsqueda: anota el filtro, la página y el tamaño, y devuelve la página preparada.</summary>
    private sealed class RepositorioDeBusqueda(PaginaFacturas respuesta) : IFacturaRepository
    {
        public FiltroFacturas? Filtro { get; private set; }

        public int Pagina { get; private set; }

        public int Tamano { get; private set; }

        public Task<PaginaFacturas> BuscarAsync(
            FiltroFacturas filtro, int pagina, int tamanoPagina, CancellationToken cancelacion = default)
        {
            Filtro = filtro;
            Pagina = pagina;
            Tamano = tamanoPagina;
            return Task.FromResult(respuesta);
        }

        public void Agregar(Factura factura) => throw new NotSupportedException();

        public Task<Factura?> ObtenerPorPresupuestoAsync(long presupuestoNumero, CancellationToken cancelacion = default) =>
            throw new NotSupportedException();

        public Task<NumeroComprobante?> ObtenerComprobantePorPresupuestoAsync(
            long presupuestoNumero, CancellationToken cancelacion = default) =>
            throw new NotSupportedException();

        public Task<bool> ExisteParaPresupuestoAsync(long presupuestoNumero, CancellationToken cancelacion = default) =>
            throw new NotSupportedException();
    }
}
