using System.Diagnostics;
using Optica.Application.Comun;
using Optica.Application.Configuracion;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Configuracion;

public class ValidacionConfiguracionTests
{
    public static TheoryData<InfraccionConfiguracion, string, string> Traducciones => new()
    {
        { InfraccionConfiguracion.AlicuotaFaltante, "alicuotaIva", "Ingresá la alícuota de IVA." },
        // Decisión del usuario (2026-11-13): cualquier alícuota inválida que llegue a la Api informa la lista de ARCA.
        { InfraccionConfiguracion.AlicuotaFueraDeRango, "alicuotaIva", "La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27." },
        { InfraccionConfiguracion.AlicuotaConMasDeDosDecimales, "alicuotaIva", "La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27." },
        { InfraccionConfiguracion.CondicionFiscalInvalida, "condicionFiscal", "Elegí Responsable Inscripto o Monotributo." },
        { InfraccionConfiguracion.MultiploFaltante, "multiploRedondeo", "Ingresá el múltiplo de redondeo comercial." },
        { InfraccionConfiguracion.MultiploMenorAlMinimo, "multiploRedondeo", "El valor mínimo es 0,01." },
        { InfraccionConfiguracion.MultiploConMasDeDosDecimales, "multiploRedondeo", "Ingresá hasta 2 decimales." },
        { InfraccionConfiguracion.MultiploFueraDeRango, "multiploRedondeo", "Ingresá un valor menor." },
        { InfraccionConfiguracion.TopeNoPositivo, "topeIdentificacion", "El tope debe ser mayor a 0." },
        { InfraccionConfiguracion.TopeConMasDeDosDecimales, "topeIdentificacion", "Ingresá hasta 2 decimales." },
        { InfraccionConfiguracion.TopeFueraDeRango, "topeIdentificacion", "Ingresá un valor menor." },
        { InfraccionConfiguracion.AlicuotaNoAdmitida, "alicuotaIva", "La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27." },
        {
            InfraccionConfiguracion.PuntoVentaFueraDeRango, "puntoVenta",
            "El punto de venta debe ser un número entero de 1 a 99999."
        },
    };

    [Theory]
    [MemberData(nameof(Traducciones))]
    public void Traducir_CadaInfraccion_CampoYAccionFijos(InfraccionConfiguracion infraccion, string campo, string accion)
    {
        Assert.Equal(new ErrorValidacion(campo, accion), ValidacionConfiguracion.Traducir(infraccion));
    }

    [Fact]
    public void Traducir_TodasLasInfracciones_DevuelvenLoDeLaTabla()
    {
        // Si el dominio suma una infracción, esta prueba obliga a darle campo y acción, aquí y en la tabla.
        var esperadas = Traducciones.ToDictionary(
            fila => (InfraccionConfiguracion)fila[0],
            fila => new ErrorValidacion((string)fila[1], (string)fila[2]));

        var obtenidas = Enum.GetValues<InfraccionConfiguracion>()
            .ToDictionary(infraccion => infraccion, ValidacionConfiguracion.Traducir);

        Assert.Equal(esperadas, obtenidas);
    }

    [Fact]
    public void Traducir_ValorNoDefinido_FallaSinTraducir()
    {
        Assert.Throws<UnreachableException>(() => ValidacionConfiguracion.Traducir((InfraccionConfiguracion)999));
    }

    [Fact]
    public void Validar_DatosValidos_SinErrores()
    {
        Assert.Empty(ValidacionConfiguracion.Validar(new DatosConfiguracion(21m, "monotributo", 0.01m, null)));
    }

    [Fact]
    public void Validar_Alicuota19_ErrorConLasSeisDeArca()
    {
        // AC-27: la validación de la grabación usa la regla de escritura (FR-26).
        var errores = ValidacionConfiguracion.Validar(new DatosConfiguracion(19m, "responsable_inscripto", 0.01m, null));

        Assert.Equal([new ErrorValidacion("alicuotaIva", "La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.")], errores);
    }

    [Theory]
    [InlineData("101")]
    [InlineData("50")]
    [InlineData("-1")]
    [InlineData("21.555")]
    public void Validar_AlicuotaFueraDeLasSeis_MensajeDeLasSeis(string alicuota)
    {
        var errores = ValidacionConfiguracion.Validar(new DatosConfiguracion(
            decimal.Parse(alicuota, System.Globalization.CultureInfo.InvariantCulture), "monotributo", 0.01m, null));

        Assert.Equal([new ErrorValidacion("alicuotaIva", "La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.")], errores);
    }

    [Fact]
    public void Validar_AlicuotaAusente_MensajeDeFaltante()
    {
        var errores = ValidacionConfiguracion.Validar(new DatosConfiguracion(null, "monotributo", 0.01m, null));

        Assert.Equal([new ErrorValidacion("alicuotaIva", "Ingresá la alícuota de IVA.")], errores);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000)]
    public void Validar_PuntoVentaFueraDeRango_ErrorEnElCampo(int puntoVenta)
    {
        var errores = ValidacionConfiguracion.Validar(
            new DatosConfiguracion(21m, "responsable_inscripto", 0.01m, null, puntoVenta));

        Assert.Equal(
            [new ErrorValidacion("puntoVenta", "El punto de venta debe ser un número entero de 1 a 99999.")], errores);
    }

    [Fact]
    public void Validar_PuntoVentaAusente_SinErrores()
    {
        // D-27: sin punto de venta en el pedido se conserva el guardado; no es un error.
        Assert.Empty(ValidacionConfiguracion.Validar(new DatosConfiguracion(21m, "monotributo", 0.01m, null, null)));
    }

    [Fact]
    public void Validar_EscalaEnLosTresNumericos_UnErrorPorCampo()
    {
        var errores = ValidacionConfiguracion.Validar(new DatosConfiguracion(21.005m, "monotributo", 1.555m, 1.001m));

        Assert.Equal(
            [
                new ErrorValidacion("alicuotaIva", "La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27."),
                new ErrorValidacion("multiploRedondeo", "Ingresá hasta 2 decimales."),
                new ErrorValidacion("topeIdentificacion", "Ingresá hasta 2 decimales."),
            ],
            errores);
    }
}
