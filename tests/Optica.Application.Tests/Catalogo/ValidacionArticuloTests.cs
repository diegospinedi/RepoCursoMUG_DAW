using System.Diagnostics;
using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;

namespace Optica.Application.Tests.Catalogo;

public class ValidacionArticuloTests
{
    private const string CaracteresInvalidos = "El texto tiene caracteres no válidos. Escribilo de nuevo.";

    public static TheoryData<InfraccionArticulo, string, string> Traducciones => new()
    {
        { InfraccionArticulo.CodigoFaltante, "codigoProveedor", "Completá el código en el proveedor." },
        { InfraccionArticulo.CodigoConCaracteresInvalidos, "codigoProveedor", CaracteresInvalidos },
        { InfraccionArticulo.CodigoDemasiadoLargo, "codigoProveedor", "Usá hasta 50 caracteres." },
        { InfraccionArticulo.DescripcionFaltante, "descripcion", "Completá la descripción." },
        { InfraccionArticulo.DescripcionConCaracteresInvalidos, "descripcion", CaracteresInvalidos },
        { InfraccionArticulo.DescripcionDemasiadoLarga, "descripcion", "Usá hasta 200 caracteres." },
        { InfraccionArticulo.CostoFaltante, "precioCosto", "Ingresá el precio de costo." },
        { InfraccionArticulo.CostoConMasDeDosDecimales, "precioCosto", "Ingresá hasta 2 decimales." },
        { InfraccionArticulo.CostoFueraDeRango, "precioCosto", "Ingresá un valor menor." },
        { InfraccionArticulo.MargenFaltante, "margenUtilidad", "Ingresá el margen de utilidad." },
        { InfraccionArticulo.MargenFueraDeRango, "margenUtilidad", "El margen debe estar entre 0 y 1.000." },
        { InfraccionArticulo.MargenConMasDeDosDecimales, "margenUtilidad", "Ingresá hasta 2 decimales." },
        {
            InfraccionArticulo.PrecioVentaFueraDeRango, "margenUtilidad",
            "El precio de venta resultante es demasiado grande. Revisá el costo o el margen."
        },
    };

    [Theory]
    [MemberData(nameof(Traducciones))]
    public void Traducir_CadaInfraccion_CampoYAccionFijos(InfraccionArticulo infraccion, string campo, string accion)
    {
        Assert.Equal(new ErrorValidacion(campo, accion), ValidacionArticulo.Traducir(infraccion));
    }

    [Fact]
    public void Traducir_TodasLasInfracciones_DevuelvenLoDeLaTabla()
    {
        // Si el dominio suma una infracción, esta prueba obliga a darle campo y acción, aquí y en la tabla.
        var esperadas = Traducciones.ToDictionary(
            fila => (InfraccionArticulo)fila[0],
            fila => new ErrorValidacion((string)fila[1], (string)fila[2]));

        var obtenidas = Enum.GetValues<InfraccionArticulo>()
            .ToDictionary(infraccion => infraccion, ValidacionArticulo.Traducir);

        Assert.Equal(esperadas, obtenidas);
    }

    [Fact]
    public void Traducir_ValorNoDefinido_FallaSinTraducir()
    {
        Assert.Throws<UnreachableException>(() => ValidacionArticulo.Traducir((InfraccionArticulo)999));
    }

    [Fact]
    public void Validar_DatosValidos_SinErrores()
    {
        Assert.Empty(ValidacionArticulo.Validar(new DatosArticulo("ABC-1", "Armazón", 1210m, 50m)));
    }

    [Fact]
    public void Validar_EscalaEnCostoYMargen_UsaLasAccionesNumericasComunes()
    {
        var errores = ValidacionArticulo.Validar(new DatosArticulo("ABC-1", "Armazón", 1.001m, 1.001m));

        Assert.Equal(
            [
                new ErrorValidacion("precioCosto", AccionesNumericas.HastaDosDecimales),
                new ErrorValidacion("margenUtilidad", AccionesNumericas.HastaDosDecimales),
            ],
            errores);
    }

    // ---- Recálculo fuera de rango (D-01, D-08, D-12, D-25 y D-26 de FEAT-002c) ----

    private const string InicioFueraDeRango = "No se grabó el cambio: el precio de venta de ";
    private const string FinFueraDeRango = "Revisá esos artículos o el múltiplo";
    private const string AgregadoInactivo = "; un artículo inactivo hay que reactivarlo para editarlo";

    private static Articulo Grabado(long codigo, bool activo = true) =>
        new(codigo, $"PRV-{codigo}", "Armazón", 100m, 50m, 150m, activo, 0);

    private static ErrorValidacion ErrorMultiplo(string accion) => new("multiploRedondeo", accion);

    [Fact]
    public void FueraDeRango_ListaDesordenada_CodigosEnOrdenNumerico()
    {
        var error = ValidacionArticulo.PrecioVentaRecalculadoFueraDeRango(
            [Grabado(12), Grabado(3), Grabado(100), Grabado(7)]);

        Assert.Equal(
            ErrorMultiplo(
                InicioFueraDeRango + "4 artículos sería demasiado grande (códigos 3, 7, 12 y 100). " + FinFueraDeRango + "."),
            error);
    }

    [Fact]
    public void FueraDeRango_InactivoDespuesDelDecimo_AgregaElAvisoSinMarca()
    {
        var articulos = Enumerable.Range(1, 10).Select(codigo => Grabado(codigo)).Append(Grabado(11, activo: false)).ToList();

        var error = ValidacionArticulo.PrecioVentaRecalculadoFueraDeRango(articulos);

        // D-26: el aviso va aunque ningún código nombrado lleve la marca.
        Assert.Equal(
            ErrorMultiplo(
                InicioFueraDeRango + "11 artículos sería demasiado grande (códigos 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 y 1 más). " +
                FinFueraDeRango + AgregadoInactivo + "."),
            error);
        Assert.DoesNotContain(", inactivo", error.Accion, StringComparison.Ordinal);
    }

    [Fact]
    public void FueraDeRango_VariosInactivos_MarcaCadaUno()
    {
        var error = ValidacionArticulo.PrecioVentaRecalculadoFueraDeRango(
            [Grabado(42, activo: false), Grabado(15, activo: false), Grabado(27)]);

        Assert.Equal(
            ErrorMultiplo(
                InicioFueraDeRango + "3 artículos sería demasiado grande (códigos 15, inactivo, 27 y 42, inactivo). " +
                FinFueraDeRango + AgregadoInactivo + "."),
            error);
    }

    [Fact]
    public void FueraDeRango_UnoSoloInactivo_CodigoEnSingularConMarca()
    {
        var error = ValidacionArticulo.PrecioVentaRecalculadoFueraDeRango([Grabado(42, activo: false)]);

        Assert.Equal(
            ErrorMultiplo(
                InicioFueraDeRango + "1 artículo sería demasiado grande (código 42, inactivo). " +
                FinFueraDeRango + AgregadoInactivo + "."),
            error);
    }

    [Fact]
    public void FueraDeRango_MasDe1000_CantidadesConPuntoDeMilesYCodigosSin()
    {
        // 1.250 artículos con códigos de 1000 a 2249: los nombrados (1000 a 1009) pasan de 999.
        var articulos = Enumerable.Range(1000, 1250).Select(codigo => Grabado(codigo)).ToList();

        var error = ValidacionArticulo.PrecioVentaRecalculadoFueraDeRango(articulos);

        // D-27: las cantidades llevan punto de miles; los códigos, que son identificadores, no.
        Assert.Equal(
            ErrorMultiplo(
                InicioFueraDeRango + "1.250 artículos sería demasiado grande (códigos 1000, 1001, 1002, 1003, 1004, " +
                "1005, 1006, 1007, 1008, 1009 y 1.240 más). " + FinFueraDeRango + "."),
            error);
        Assert.DoesNotContain("1.00", error.Accion, StringComparison.Ordinal);
    }

    [Fact]
    public void AccionesNumericas_TextosDeConfiguracion_NoCambian()
    {
        // D-12: los textos que la configuración ya mostraba, ahora compartidos desde Comun.
        Assert.Equal("Ingresá hasta 2 decimales.", AccionesNumericas.HastaDosDecimales);
        Assert.Equal("Ingresá un valor menor.", AccionesNumericas.ValorMenor);
    }
}
