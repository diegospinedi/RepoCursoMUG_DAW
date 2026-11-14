using System.Diagnostics;

namespace Optica.Domain.Configuracion;

/// <summary>
/// Los parámetros de negocio vigentes: alícuota de IVA, condición fiscal, múltiplo de redondeo comercial, tope de
/// identificación del receptor y punto de venta de ARCA (estos dos, sin valor mientras no se carguen). Siempre cumple
/// las invariantes de lectura de <see cref="PoliticaConfiguracionNegocio.Evaluar"/>; la que se graba se arma con
/// <see cref="ParaGrabar"/>, que además exige una de las alícuotas de ARCA (D-33 de FEAT-007).
/// </summary>
public sealed class ConfiguracionNegocio
{
    /// <summary>
    /// La configuración tal como está guardada (lectura). Admite cualquier alícuota de 0 a 100 aunque ARCA no la acepte
    /// (D-06 de FEAT-007): lo informa <see cref="AlicuotaAdmitidaPorArca"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Algún valor incumple la política, o la condición no es un valor definido. El mensaje nombra el campo y no
    /// incluye el valor recibido.
    /// </exception>
    public ConfiguracionNegocio(
        decimal alicuotaIva, CondicionFiscal condicionFiscal, decimal multiploRedondeo, decimal? topeIdentificacion,
        int? puntoVenta)
        : this(alicuotaIva, condicionFiscal, multiploRedondeo, topeIdentificacion, puntoVenta, paraGrabar: false)
    {
    }

    private ConfiguracionNegocio(
        decimal alicuotaIva,
        CondicionFiscal condicionFiscal,
        decimal multiploRedondeo,
        decimal? topeIdentificacion,
        int? puntoVenta,
        bool paraGrabar)
    {
        if (!Enum.IsDefined(condicionFiscal))
        {
            throw new ArgumentException("La condición fiscal no es válida.", nameof(condicionFiscal));
        }

        var condicion = PoliticaConfiguracionNegocio.TextoDe(condicionFiscal);
        var infracciones = paraGrabar
            ? PoliticaConfiguracionNegocio.EvaluarParaGrabar(
                alicuotaIva, condicion, multiploRedondeo, topeIdentificacion, puntoVenta)
            : PoliticaConfiguracionNegocio.Evaluar(alicuotaIva, condicion, multiploRedondeo, topeIdentificacion, puntoVenta);
        if (infracciones.Count > 0)
        {
            throw Error(infracciones[0]);
        }

        AlicuotaIva = alicuotaIva;
        CondicionFiscal = condicionFiscal;
        MultiploRedondeo = multiploRedondeo;
        TopeIdentificacion = topeIdentificacion;
        PuntoVenta = puntoVenta;
    }

    /// <summary>
    /// La configuración que se va a grabar: las invariantes de lectura más la regla de las 6 alícuotas de ARCA (FR-26 y
    /// D-33 de FEAT-007).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Algún valor incumple la política de escritura, o la condición no es un valor definido. El mensaje nombra el campo
    /// y no incluye el valor recibido.
    /// </exception>
    public static ConfiguracionNegocio ParaGrabar(
        decimal alicuotaIva, CondicionFiscal condicionFiscal, decimal multiploRedondeo, decimal? topeIdentificacion,
        int? puntoVenta) =>
        new(alicuotaIva, condicionFiscal, multiploRedondeo, topeIdentificacion, puntoVenta, paraGrabar: true);

    /// <summary>Porcentaje de 0 a 100, con hasta 2 decimales.</summary>
    public decimal AlicuotaIva { get; }

    public CondicionFiscal CondicionFiscal { get; }

    /// <summary>Importe desde 0,01, con hasta 2 decimales.</summary>
    public decimal MultiploRedondeo { get; }

    /// <summary>Importe mayor a 0 con hasta 2 decimales, o null si todavía no se cargó (D-02).</summary>
    public decimal? TopeIdentificacion { get; }

    /// <summary>
    /// El punto de venta habilitado para web services, de 1 a 99999, o null mientras no se cargue (FR-28 y D-07 de
    /// FEAT-007).
    /// </summary>
    public int? PuntoVenta { get; }

    /// <summary>Verdadero mientras el tope de identificación no tenga valor (FR-08).</summary>
    public bool FaltaTope => TopeIdentificacion is null;

    /// <summary>Verdadero mientras el punto de venta no tenga valor: no se puede emitir (FR-29 de FEAT-007).</summary>
    public bool FaltaPuntoVenta => PuntoVenta is null;

    /// <summary>
    /// Falso si la alícuota guardada no es una de <see cref="AlicuotasAdmitidas"/> (grabada antes de FR-26): la
    /// Factura B no se emite hasta elegir una válida (FR-27 de FEAT-007).
    /// </summary>
    public bool AlicuotaAdmitidaPorArca => AlicuotasAdmitidas.EsAdmitida(AlicuotaIva);

    /// <summary>
    /// Verdadero si respecto de <paramref name="anterior"/> cambia la alícuota, la condición fiscal o el múltiplo: los
    /// parámetros que disparan el recálculo del catálogo (FR-01 de FEAT-002c, D-07). El tope y el punto de venta no lo
    /// disparan.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="anterior"/> es null.</exception>
    public bool CambiaParametrosDePrecioRespectoDe(ConfiguracionNegocio anterior)
    {
        ArgumentNullException.ThrowIfNull(anterior);

        return AlicuotaIva != anterior.AlicuotaIva
            || CondicionFiscal != anterior.CondicionFiscal
            || MultiploRedondeo != anterior.MultiploRedondeo;
    }

    // Los mensajes no incluyen el valor recibido, como el resto de las invariantes del dominio.
    private static ArgumentException Error(InfraccionConfiguracion infraccion) => infraccion switch
    {
        InfraccionConfiguracion.AlicuotaFaltante
            or InfraccionConfiguracion.AlicuotaFueraDeRango
            or InfraccionConfiguracion.AlicuotaConMasDeDosDecimales
            or InfraccionConfiguracion.AlicuotaNoAdmitida =>
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
        InfraccionConfiguracion.PuntoVentaFueraDeRango =>
            new ArgumentException("El punto de venta no es válido.", "puntoVenta"),
        _ => throw new UnreachableException("Infracción de configuración sin campo asignado."),
    };
}
