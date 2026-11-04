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

    [Fact]
    public void AccionesNumericas_TextosDeConfiguracion_NoCambian()
    {
        // D-12: los textos que la configuración ya mostraba, ahora compartidos desde Comun.
        Assert.Equal("Ingresá hasta 2 decimales.", AccionesNumericas.HastaDosDecimales);
        Assert.Equal("Ingresá un valor menor.", AccionesNumericas.ValorMenor);
    }
}
