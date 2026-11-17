using Optica.Domain.Catalogo;
using Optica.Domain.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Domain.Tests.Catalogo;

public class PlanillaCostosTests
{
    private const string TextoInexistente = "código inexistente";
    private const string TextoPrecioInvalido = "precio inválido";
    private const string TextoRepetido = "código repetido en la planilla";
    private const string TextoPrecioNoPositivo = "actualizado con precio negativo o cero";
    private const string TextoInactivo = "inactivo";

    [Fact]
    public void TextoDeCodigoNumerico_SinEscalaNiSeparadores_CulturaInvariante()
    {
        // D-10: la misma regla para el lector y para Evaluar, sin depender de la cultura de la PC.
        var cultura = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("es-AR");
        try
        {
            Assert.Equal("100", PlanillaCostos.TextoDeCodigoNumerico(100.0m));
            Assert.Equal("1200.5", PlanillaCostos.TextoDeCodigoNumerico(1200.50m));
            Assert.Equal("1500", PlanillaCostos.TextoDeCodigoNumerico(1.5E3m));
            Assert.Equal("-7", PlanillaCostos.TextoDeCodigoNumerico(-7m));
            Assert.Equal("1234567", PlanillaCostos.TextoDeCodigoNumerico(1234567m));
            Assert.Equal("0.0000000000000000000000000001", PlanillaCostos.TextoDeCodigoNumerico(0.0000000000000000000000000001m));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = cultura;
        }
    }

    [Fact]
    public void TieneFormatoEsperado_EncabezadosConMayusculasAcentosYEspacios_Acepta()
    {
        // AC-06 (FR-03): sin distinguir mayúsculas, acentos ni espacios al principio y al final.
        Assert.True(PlanillaCostos.TieneFormatoEsperado(Contenido(
            Texto(" CÓDIGO EN EL PROVEEDOR "), Texto("precio de costo nuevo"))));
        Assert.True(PlanillaCostos.TieneFormatoEsperado(Contenido(
            Texto("Codigo en el proveedor"), Texto("Precio de Costo Nuevo"))));
        Assert.True(PlanillaCostos.TieneFormatoEsperado(Contenido(
            Texto(" código en el proveedor\t"), Texto("PRECIO DE COSTO NUEVO "))));
    }

    [Fact]
    public void TieneFormatoEsperado_EncabezadosCodigoYPrecio_Rechazado()
    {
        // AC-05 (FR-03, FR-04): encabezados distintos, en otro orden, vacíos o que no son texto.
        Assert.False(PlanillaCostos.TieneFormatoEsperado(Contenido(Texto("Codigo"), Texto("Precio"))));
        Assert.False(PlanillaCostos.TieneFormatoEsperado(Contenido(
            Texto("Precio de Costo Nuevo"), Texto("Codigo en el proveedor"))));
        Assert.False(PlanillaCostos.TieneFormatoEsperado(Contenido(
            Texto("Codigo en el proveedor"), new CeldaPlanilla.Vacia())));
        Assert.False(PlanillaCostos.TieneFormatoEsperado(Contenido(
            new CeldaPlanilla.Vacia(), Texto("Precio de Costo Nuevo"))));
        Assert.False(PlanillaCostos.TieneFormatoEsperado(Contenido(
            Texto("Codigo en el proveedor"), new CeldaPlanilla.Numero(1m))));
        Assert.False(PlanillaCostos.TieneFormatoEsperado(Contenido(
            new CeldaPlanilla.Otro(), Texto("Precio de Costo Nuevo"))));
        Assert.False(PlanillaCostos.TieneFormatoEsperado(Contenido(
            Texto("Codigo en el  proveedor"), Texto("Precio de Costo Nuevo"))));
    }

    [Fact]
    public void TieneFormatoEsperado_TerceraColumna_Rechazado()
    {
        // AC-04 (FR-03, FR-04): con los dos encabezados correctos, una columna de más rechaza la planilla.
        var contenido = Contenido(
            Texto("Codigo en el proveedor"), Texto("Precio de Costo Nuevo"), tieneColumnasDeMas: true);

        Assert.False(PlanillaCostos.TieneFormatoEsperado(contenido));
    }

    [Fact]
    public void Evaluar_CodigoInexistente_ListaRazon()
    {
        // AC-02 (FR-12, FR-13): las válidas se aplican aunque otra fila no exista en el catálogo.
        var catalogo = Catalogo(Articulo(1L, "ABC-1"), Articulo(2L, "ABC-2"));

        var resultados = PlanillaCostos.Evaluar(
            [Fila(2, "ABC-1", 2420m), Fila(3, "ZZZ-9", 100m), Fila(4, "ABC-2", 2420m)], catalogo, Vigente());

        Assert.Equal([2, 3, 4], resultados.Select(resultado => resultado.Numero));
        Assert.True(resultados[0].EsAplicada);
        Assert.True(resultados[2].EsAplicada);
        Assert.False(resultados[1].EsAplicada);
        Assert.Equal(RazonNoAplicada.CodigoInexistente, resultados[1].Razon);
        Assert.Equal(TextoInexistente, resultados[1].Razon!.Value.Texto());
        Assert.Equal("ZZZ-9", resultados[1].Codigo);
        Assert.Null(resultados[1].Articulo);
        Assert.Null(resultados[0].Razon);
    }

    [Fact]
    public void Evaluar_CodigoVacioInvalidoOLargo_CodigoInexistente()
    {
        // D-10: vacío, solo espacios, UTF-16 inválido, más de 50 caracteres u otro tipo de celda; sin excepción.
        var largo = new string('A', PoliticaArticulo.LargoMaximoCodigo + 1);
        var catalogo = Catalogo(Articulo(1L, "ABC-1"));
        var filas = new[]
        {
            new FilaPlanilla(2, new CeldaPlanilla.Vacia(), new CeldaPlanilla.Numero(10m)),
            new FilaPlanilla(3, Texto("  \t"), new CeldaPlanilla.Numero(10m)),
            new FilaPlanilla(4, Texto("AB\uD800"), new CeldaPlanilla.Numero(10m)),
            new FilaPlanilla(5, Texto(largo), new CeldaPlanilla.Numero(10m)),
            new FilaPlanilla(6, new CeldaPlanilla.Otro(), new CeldaPlanilla.Numero(10m)),
            new FilaPlanilla(7, Texto("\uDC00"), new CeldaPlanilla.Texto("abc")),
        };

        var resultados = PlanillaCostos.Evaluar(filas, catalogo, Vigente());

        Assert.Equal(6, resultados.Count);
        Assert.All(resultados, resultado =>
        {
            Assert.False(resultado.EsAplicada);
            Assert.Equal(RazonNoAplicada.CodigoInexistente, resultado.Razon);
        });
    }

    [Fact]
    public void Evaluar_CodigoNumerico_UsaTextoInvariante()
    {
        // D-10: un código numérico se toma como texto invariante sin ceros de escala: 100.0 es "100".
        var catalogo = Catalogo(Articulo(1L, "12345"), Articulo(2L, "100"));
        var filas = new[]
        {
            new FilaPlanilla(2, new CeldaPlanilla.Numero(12345m), new CeldaPlanilla.Numero(2420m)),
            new FilaPlanilla(3, new CeldaPlanilla.Numero(100.0m), new CeldaPlanilla.Numero(2420m)),
        };

        var resultados = PlanillaCostos.Evaluar(filas, catalogo, Vigente());

        Assert.All(resultados, resultado => Assert.True(resultado.EsAplicada));
        Assert.Equal("12345", resultados[0].Codigo);
        Assert.Equal("100", resultados[1].Codigo);
        Assert.Equal("12345", resultados[0].Articulo!.CodigoProveedor);
        Assert.Equal("100", resultados[1].Articulo!.CodigoProveedor);
    }

    [Fact]
    public void Evaluar_FilaNula_LanzaArgumentNull()
    {
        var error = Assert.Throws<ArgumentNullException>(() => PlanillaCostos.Evaluar(
            [Fila(2, "ABC-1", 2420m), null!], Catalogo(Articulo(1L, "ABC-1")), Vigente()));

        Assert.Equal("filas", error.ParamName);
    }

    [Fact]
    public void Evaluar_PrecioVacioYTexto_PrecioInvalido()
    {
        // AC-03 (FR-08, D-04, D-09): vacío, "abc", "2420" escrito como texto y otro tipo son precio inválido.
        var catalogo = Catalogo(
            Articulo(1L, "ABC-1"), Articulo(2L, "ABC-2"), Articulo(3L, "ABC-3"), Articulo(4L, "ABC-4"),
            Articulo(5L, "ABC-5"));
        var filas = new[]
        {
            new FilaPlanilla(2, Texto("ABC-1"), new CeldaPlanilla.Vacia()),
            new FilaPlanilla(3, Texto("ABC-2"), Texto("abc")),
            new FilaPlanilla(4, Texto("ABC-3"), Texto("2420")),
            new FilaPlanilla(5, Texto("ABC-4"), new CeldaPlanilla.Otro()),
            Fila(6, "ABC-5", 2420m),
        };

        var resultados = PlanillaCostos.Evaluar(filas, catalogo, Vigente());

        Assert.All(resultados.Take(4), resultado =>
        {
            Assert.False(resultado.EsAplicada);
            Assert.Equal(RazonNoAplicada.PrecioInvalido, resultado.Razon);
            Assert.Equal(TextoPrecioInvalido, resultado.Razon!.Value.Texto());
        });
        Assert.True(resultados[4].EsAplicada);
        Assert.Equal(2420m, resultados[4].Articulo!.PrecioCosto);
    }

    [Fact]
    public void Evaluar_PrecioFueraDeRangoOVentaFueraDeRango_PrecioInvalido()
    {
        // D-09: costo fuera de ReglasImporte.ImporteMaximo, o venta recalculada fuera de rango; nunca un desborde.
        var catalogo = Catalogo(Articulo(1L, "ABC-1"), Articulo(2L, "ABC-2"), Articulo(3L, "ABC-3"), Articulo(4L, "ABC-4"));
        var filas = new[]
        {
            Fila(2, "ABC-1", ReglasImporte.ImporteMaximo + 0.01m),
            Fila(3, "ABC-2", -ReglasImporte.ImporteMaximo - 0.01m),
            Fila(4, "ABC-3", decimal.MaxValue),
            Fila(5, "ABC-4", ReglasImporte.ImporteMaximo),
        };

        var resultados = PlanillaCostos.Evaluar(filas, catalogo, Vigente());

        Assert.All(resultados, resultado =>
        {
            Assert.False(resultado.EsAplicada);
            Assert.Equal(RazonNoAplicada.PrecioInvalido, resultado.Razon);
        });
    }

    [Fact]
    public void Evaluar_PrecioConTresDecimales_RedondeaAlCentavo()
    {
        // D-03: al centavo con la mitad alejándose del cero; la fila se aplica.
        var catalogo = Catalogo(Articulo(1L, "ABC-1"), Articulo(2L, "ABC-2"));

        var resultados = PlanillaCostos.Evaluar(
            [Fila(2, "ABC-1", 1234.565m), Fila(3, "ABC-2", -0.005m)], catalogo, Vigente());

        Assert.True(resultados[0].EsAplicada);
        Assert.Equal(1234.57m, resultados[0].Articulo!.PrecioCosto);
        Assert.True(resultados[1].EsAplicada);
        Assert.Equal(-0.01m, resultados[1].Articulo!.PrecioCosto);
        Assert.True(resultados[1].PrecioNoPositivo);
    }

    [Fact]
    public void Evaluar_PrecioNegativoYCero_AplicaYMarca()
    {
        // AC-08 (FR-09, FR-14): se aplican y llevan la indicación de precio negativo o cero.
        var catalogo = Catalogo(Articulo(1L, "ABC-1"), Articulo(2L, "ABC-2"), Articulo(3L, "ABC-3"));

        var resultados = PlanillaCostos.Evaluar(
            [Fila(2, "ABC-1", -100m), Fila(3, "ABC-2", 0m), Fila(4, "ABC-3", 2420m)], catalogo, Vigente());

        Assert.True(resultados[0].EsAplicada);
        Assert.Equal(-100m, resultados[0].Articulo!.PrecioCosto);
        Assert.Equal(-150m, resultados[0].Articulo!.PrecioVenta);
        Assert.True(resultados[0].PrecioNoPositivo);
        Assert.Equal([TextoPrecioNoPositivo], resultados[0].Indicaciones);
        Assert.True(resultados[1].EsAplicada);
        Assert.Equal(0m, resultados[1].Articulo!.PrecioCosto);
        Assert.Equal(0m, resultados[1].Articulo!.PrecioVenta);
        Assert.True(resultados[1].PrecioNoPositivo);
        Assert.Equal([TextoPrecioNoPositivo], resultados[1].Indicaciones);
        Assert.False(resultados[2].PrecioNoPositivo);
        Assert.Empty(resultados[2].Indicaciones);
    }

    [Fact]
    public void Evaluar_CodigoEnMinusculasYConEspacios_Aplica()
    {
        // AC-09 y AC-10 (FR-07): la clave de ADR-009 ignora mayúsculas y espacios en los extremos.
        foreach (var codigo in new[] { "abc-1", " ABC-1 ", " abc-1\t" })
        {
            var resultados = PlanillaCostos.Evaluar(
                [Fila(2, codigo, 2420m)], Catalogo(Articulo(1L, "ABC-1")), Vigente());

            var resultado = Assert.Single(resultados);
            Assert.True(resultado.EsAplicada);
            Assert.Null(resultado.Razon);
            Assert.Equal(2420m, resultado.Articulo!.PrecioCosto);
            Assert.Equal("ABC-1", resultado.Articulo.CodigoProveedor);
            Assert.Equal(NormalizacionCodigo.Recortar(codigo), resultado.Codigo);
        }
    }

    [Fact]
    public void Evaluar_CodigoDuplicadoConOtraCaja_AmbasRechazadas()
    {
        // AC-11 (FR-10) y D-11: todas las filas de una clave repetida, aunque el precio esté mal o el código no exista.
        var catalogo = Catalogo(Articulo(1L, "ABC-1"), Articulo(2L, "ABC-2"));
        var filas = new[]
        {
            Fila(2, "ABC-1", 2420m),
            Fila(3, "abc-1", 2500m),
            new FilaPlanilla(4, Texto(" ABC-1"), Texto("abc")),
            Fila(5, "ZZZ-9", 1m),
            Fila(6, "zzz-9", 2m),
            Fila(7, "ABC-2", 2420m),
        };

        var resultados = PlanillaCostos.Evaluar(filas, catalogo, Vigente());

        Assert.All(resultados.Take(5), resultado =>
        {
            Assert.False(resultado.EsAplicada);
            Assert.Null(resultado.Articulo);
            Assert.Equal(RazonNoAplicada.CodigoRepetido, resultado.Razon);
            Assert.Equal(TextoRepetido, resultado.Razon!.Value.Texto());
        });
        Assert.True(resultados[5].EsAplicada);
    }

    [Fact]
    public void Evaluar_ArticuloInactivo_AplicaYMarcaInactivo()
    {
        // AC-12 (FR-11, FR-15, D-12): costo y venta nuevos, sigue inactivo, margen y descripción iguales.
        var inactivo = Articulo(1L, "ABC-1", activo: false);

        var resultado = Assert.Single(PlanillaCostos.Evaluar(
            [Fila(2, "ABC-1", 2420m)], Catalogo(inactivo), Vigente()));

        Assert.True(resultado.EsAplicada);
        Assert.True(resultado.Cambia);
        Assert.True(resultado.Inactivo);
        Assert.False(resultado.PrecioNoPositivo);
        Assert.Equal([TextoInactivo], resultado.Indicaciones);
        Assert.False(resultado.Articulo!.Activo);
        Assert.Equal(2420m, resultado.Articulo.PrecioCosto);
        Assert.Equal(3630m, resultado.Articulo.PrecioVenta);
        Assert.Equal(inactivo.MargenUtilidad, resultado.Articulo.MargenUtilidad);
        Assert.Equal(inactivo.Descripcion, resultado.Articulo.Descripcion);
        Assert.Equal(inactivo.Version, resultado.Articulo.Version);

        var negativo = Assert.Single(PlanillaCostos.Evaluar(
            [Fila(2, "ABC-1", -1m)], Catalogo(inactivo), Vigente()));
        Assert.Equal([TextoPrecioNoPositivo, TextoInactivo], negativo.Indicaciones);
    }

    [Fact]
    public void Evaluar_CostoYVentaIguales_AplicadaSinCambio()
    {
        // D-13: costo y venta iguales a los guardados (aunque con otra escala) cuentan como aplicada sin cambio.
        var guardado = Articulo(1L, "ABC-1");

        var resultados = PlanillaCostos.Evaluar(
            [Fila(2, "ABC-1", 1210.00m)], Catalogo(guardado), Vigente());

        var resultado = Assert.Single(resultados);
        Assert.True(resultado.EsAplicada);
        Assert.False(resultado.Cambia);
        Assert.Null(resultado.Razon);
        Assert.Equal(1210m, resultado.Articulo!.PrecioCosto);
        Assert.Equal(1815m, resultado.Articulo.PrecioVenta);
    }

    private static Articulo Articulo(long codigo, string codigoProveedor, bool activo = true) =>
        new(codigo, codigoProveedor, "Lente", 1210m, 50m, 1815m, activo, version: 3L);

    private static Dictionary<string, Articulo> Catalogo(params Articulo[] articulos) =>
        articulos.ToDictionary(articulo => articulo.CodigoProveedorClave, StringComparer.Ordinal);

    private static ConfiguracionNegocio Vigente() =>
        new(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);

    private static CeldaPlanilla Texto(string valor) => new CeldaPlanilla.Texto(valor);

    private static FilaPlanilla Fila(int numero, string codigo, decimal precio) =>
        new(numero, Texto(codigo), new CeldaPlanilla.Numero(precio));

    private static ContenidoPlanilla Contenido(
        CeldaPlanilla encabezadoA, CeldaPlanilla encabezadoB, bool tieneColumnasDeMas = false) =>
        new(encabezadoA, encabezadoB, tieneColumnasDeMas, []);
}
