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
        { InfraccionConfiguracion.AlicuotaFueraDeRango, "alicuotaIva", "La alícuota de IVA debe estar entre 0 y 100." },
        { InfraccionConfiguracion.AlicuotaConMasDeDosDecimales, "alicuotaIva", "Ingresá hasta 2 decimales." },
        { InfraccionConfiguracion.CondicionFiscalInvalida, "condicionFiscal", "Elegí Responsable Inscripto o Monotributo." },
        { InfraccionConfiguracion.MultiploFaltante, "multiploRedondeo", "Ingresá el múltiplo de redondeo comercial." },
        { InfraccionConfiguracion.MultiploMenorAlMinimo, "multiploRedondeo", "El valor mínimo es 0,01." },
        { InfraccionConfiguracion.MultiploConMasDeDosDecimales, "multiploRedondeo", "Ingresá hasta 2 decimales." },
        { InfraccionConfiguracion.MultiploFueraDeRango, "multiploRedondeo", "Ingresá un valor menor." },
        { InfraccionConfiguracion.TopeNoPositivo, "topeIdentificacion", "El tope debe ser mayor a 0." },
        { InfraccionConfiguracion.TopeConMasDeDosDecimales, "topeIdentificacion", "Ingresá hasta 2 decimales." },
        { InfraccionConfiguracion.TopeFueraDeRango, "topeIdentificacion", "Ingresá un valor menor." },
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
    public void Validar_EscalaEnLosTresNumericos_UnErrorPorCampo()
    {
        var errores = ValidacionConfiguracion.Validar(new DatosConfiguracion(21.005m, "monotributo", 1.555m, 1.001m));

        Assert.Equal(
            [
                new ErrorValidacion("alicuotaIva", "Ingresá hasta 2 decimales."),
                new ErrorValidacion("multiploRedondeo", "Ingresá hasta 2 decimales."),
                new ErrorValidacion("topeIdentificacion", "Ingresá hasta 2 decimales."),
            ],
            errores);
    }
}
