using System.Diagnostics;

namespace Optica.Domain.Configuracion;

/// <summary>
/// Los parámetros de negocio vigentes: alícuota de IVA, condición fiscal, múltiplo de redondeo comercial y tope de
/// identificación del receptor (sin valor mientras no se cargue). Siempre cumple
/// <see cref="PoliticaConfiguracionNegocio"/>.
/// </summary>
public sealed class ConfiguracionNegocio
{
    /// <exception cref="ArgumentException">
    /// Algún valor incumple la política, o la condición no es un valor definido. El mensaje nombra el campo y no
    /// incluye el valor recibido.
    /// </exception>
    public ConfiguracionNegocio(
        decimal alicuotaIva, CondicionFiscal condicionFiscal, decimal multiploRedondeo, decimal? topeIdentificacion)
    {
        if (!Enum.IsDefined(condicionFiscal))
        {
            throw new ArgumentException("La condición fiscal no es válida.", nameof(condicionFiscal));
        }

        var infracciones = PoliticaConfiguracionNegocio.Evaluar(
            alicuotaIva, PoliticaConfiguracionNegocio.TextoDe(condicionFiscal), multiploRedondeo, topeIdentificacion);
        if (infracciones.Count > 0)
        {
            throw Error(infracciones[0]);
        }

        AlicuotaIva = alicuotaIva;
        CondicionFiscal = condicionFiscal;
        MultiploRedondeo = multiploRedondeo;
        TopeIdentificacion = topeIdentificacion;
    }

    /// <summary>Porcentaje de 0 a 100, con hasta 2 decimales.</summary>
    public decimal AlicuotaIva { get; }

    public CondicionFiscal CondicionFiscal { get; }

    /// <summary>Importe desde 0,01, con hasta 2 decimales.</summary>
    public decimal MultiploRedondeo { get; }

    /// <summary>Importe mayor a 0 con hasta 2 decimales, o null si todavía no se cargó (D-02).</summary>
    public decimal? TopeIdentificacion { get; }

    /// <summary>Verdadero mientras el tope de identificación no tenga valor (FR-08).</summary>
    public bool FaltaTope => TopeIdentificacion is null;

    // Los mensajes no incluyen el valor recibido, como el resto de las invariantes del dominio.
    private static ArgumentException Error(InfraccionConfiguracion infraccion) => infraccion switch
    {
        InfraccionConfiguracion.AlicuotaFaltante
            or InfraccionConfiguracion.AlicuotaFueraDeRango
            or InfraccionConfiguracion.AlicuotaConMasDeDosDecimales =>
            new ArgumentException("La alícuota de IVA no es válida.", "alicuotaIva"),
        InfraccionConfiguracion.MultiploFaltante
            or InfraccionConfiguracion.MultiploMenorAlMinimo
            or InfraccionConfiguracion.MultiploConMasDeDosDecimales
            or InfraccionConfiguracion.MultiploFueraDeRango =>
            new ArgumentException("El múltiplo de redondeo comercial no es válido.", "multiploRedondeo"),
        InfraccionConfiguracion.TopeNoPositivo
            or InfraccionConfiguracion.TopeConMasDeDosDecimales
            or InfraccionConfiguracion.TopeFueraDeRango =>
            new ArgumentException("El tope de identificación del receptor no es válido.", "topeIdentificacion"),
        InfraccionConfiguracion.CondicionFiscalInvalida =>
            new ArgumentException("La condición fiscal no es válida.", "condicionFiscal"),
        _ => throw new UnreachableException("Infracción de configuración sin campo asignado."),
    };
}
