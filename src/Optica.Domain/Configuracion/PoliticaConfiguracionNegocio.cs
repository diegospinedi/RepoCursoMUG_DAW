using Optica.Domain.Comun;

namespace Optica.Domain.Configuracion;

/// <summary>
/// Qué regla de la configuración de negocio incumple un campo. Cada valor pertenece a un solo campo: el nombre
/// empieza por el campo (alícuota, condición, múltiplo, tope o punto de venta).
/// </summary>
public enum InfraccionConfiguracion
{
    AlicuotaFaltante,
    AlicuotaFueraDeRango,
    AlicuotaConMasDeDosDecimales,
    CondicionFiscalInvalida,
    MultiploFaltante,
    MultiploMenorAlMinimo,
    MultiploConMasDeDosDecimales,
    MultiploFueraDeRango,
    TopeNoPositivo,
    TopeConMasDeDosDecimales,
    TopeFueraDeRango,

    /// <summary>La alícuota no es una de <see cref="AlicuotasAdmitidas"/>: solo al grabar (FR-26 y D-06 de FEAT-007).</summary>
    AlicuotaNoAdmitida,

    /// <summary>El punto de venta no es un entero de 1 a 99999 (FR-28 y D-33 de FEAT-007).</summary>
    PuntoVentaFueraDeRango,
}

/// <summary>
/// Reglas de los parámetros de negocio (FR-02 a FR-05, D-01 y D-02 de FEAT-001c; FR-26 y FR-28 de FEAT-007) y la
/// única traducción entre <see cref="CondicionFiscal"/> y su texto (D-07).
/// </summary>
/// <remarks>
/// Dos niveles (D-06 y D-33 de FEAT-007): <see cref="Evaluar"/> son las invariantes de lectura, que cumple toda
/// configuración guardada (alícuota de 0 a 100 y punto de venta de 1 a 99999); <see cref="EvaluarParaGrabar"/> suma la
/// regla de escritura de las 6 alícuotas de ARCA. Así una alícuota grabada antes de FEAT-007 se sigue leyendo.
/// </remarks>
public static class PoliticaConfiguracionNegocio
{
    public const decimal AlicuotaMinima = 0m;
    public const decimal AlicuotaMaxima = 100m;
    public const decimal MultiploMinimo = 0.01m;
    public const int PuntoVentaMinimo = 1;
    public const int PuntoVentaMaximo = 99999;

    /// <summary>Alias de <see cref="ReglasImporte.ImporteMaximo"/>, que es donde vive la regla (D-12 de FEAT-002a).</summary>
    public const decimal ImporteMaximo = ReglasImporte.ImporteMaximo;

    private const string TextoResponsableInscripto = "responsable_inscripto";
    private const string TextoMonotributo = "monotributo";

    /// <summary>
    /// Evalúa los cinco campos con las invariantes de lectura y devuelve sus infracciones, una por campo como máximo, en
    /// el orden alícuota, condición, múltiplo, tope, punto de venta. Por campo gana la primera regla incumplida. Un tope
    /// o un punto de venta sin valor son válidos. Nunca lanza.
    /// </summary>
    public static IReadOnlyList<InfraccionConfiguracion> Evaluar(
        decimal? alicuotaIva, string? condicionFiscal, decimal? multiploRedondeo, decimal? topeIdentificacion,
        int? puntoVenta) =>
        EvaluarCampos(alicuotaIva, condicionFiscal, multiploRedondeo, topeIdentificacion, puntoVenta, paraGrabar: false);

    /// <summary>
    /// Lo mismo que <see cref="Evaluar"/>, salvo la alícuota, que tiene que ser una de <see cref="AlicuotasAdmitidas"/>
    /// (FR-26 de FEAT-007): sin valor es <see cref="InfraccionConfiguracion.AlicuotaFaltante"/>, y cualquier otra
    /// (19, 101, -1, 21,555) es <see cref="InfraccionConfiguracion.AlicuotaNoAdmitida"/>, sin informar rango ni decimales
    /// (decisión del usuario del 2026-11-13). Nunca lanza.
    /// </summary>
    public static IReadOnlyList<InfraccionConfiguracion> EvaluarParaGrabar(
        decimal? alicuotaIva, string? condicionFiscal, decimal? multiploRedondeo, decimal? topeIdentificacion,
        int? puntoVenta) =>
        EvaluarCampos(alicuotaIva, condicionFiscal, multiploRedondeo, topeIdentificacion, puntoVenta, paraGrabar: true);

    private static List<InfraccionConfiguracion> EvaluarCampos(
        decimal? alicuotaIva,
        string? condicionFiscal,
        decimal? multiploRedondeo,
        decimal? topeIdentificacion,
        int? puntoVenta,
        bool paraGrabar)
    {
        var infracciones = new List<InfraccionConfiguracion>(5);

        if ((paraGrabar ? EvaluarAlicuotaParaGrabar(alicuotaIva) : EvaluarAlicuota(alicuotaIva)) is { } alicuota)
        {
            infracciones.Add(alicuota);
        }

        if (LeerCondicion(condicionFiscal) is null)
        {
            infracciones.Add(InfraccionConfiguracion.CondicionFiscalInvalida);
        }

        if (EvaluarMultiplo(multiploRedondeo) is { } multiplo)
        {
            infracciones.Add(multiplo);
        }

        if (EvaluarTope(topeIdentificacion) is { } tope)
        {
            infracciones.Add(tope);
        }

        // D-07 de FEAT-007: sin punto de venta es un valor válido; la emisión lo exige (FR-29).
        if (puntoVenta is < PuntoVentaMinimo or > PuntoVentaMaximo)
        {
            infracciones.Add(InfraccionConfiguracion.PuntoVentaFueraDeRango);
        }

        return infracciones;
    }

    /// <summary>
    /// La condición fiscal de un texto, con comparación exacta (sin ignorar mayúsculas ni espacios); null si el
    /// texto no es <c>responsable_inscripto</c> ni <c>monotributo</c>.
    /// </summary>
    public static CondicionFiscal? LeerCondicion(string? texto) => texto switch
    {
        TextoResponsableInscripto => CondicionFiscal.ResponsableInscripto,
        TextoMonotributo => CondicionFiscal.Monotributo,
        _ => null,
    };

    /// <summary>El texto de una condición fiscal: el del contrato de la Api y el de la base.</summary>
    /// <exception cref="ArgumentException">El valor no es uno de los definidos en <see cref="CondicionFiscal"/>.</exception>
    public static string TextoDe(CondicionFiscal condicion) => condicion switch
    {
        CondicionFiscal.ResponsableInscripto => TextoResponsableInscripto,
        CondicionFiscal.Monotributo => TextoMonotributo,
        _ => throw new ArgumentException("La condición fiscal no es válida.", nameof(condicion)),
    };

    private static InfraccionConfiguracion? EvaluarAlicuota(decimal? valor)
    {
        if (valor is not { } alicuota)
        {
            return InfraccionConfiguracion.AlicuotaFaltante;
        }

        if (alicuota < AlicuotaMinima || alicuota > AlicuotaMaxima)
        {
            return InfraccionConfiguracion.AlicuotaFueraDeRango;
        }

        return TieneMasDeDosDecimales(alicuota) ? InfraccionConfiguracion.AlicuotaConMasDeDosDecimales : null;
    }

    // Decisión del usuario (2026-11-13): al grabar, toda alícuota presente que no sea una de las 6 informa la lista de
    // ARCA, sin distinguir rango ni decimales; el rango y los decimales quedan para la lectura (D-06).
    private static InfraccionConfiguracion? EvaluarAlicuotaParaGrabar(decimal? valor) => valor switch
    {
        null => InfraccionConfiguracion.AlicuotaFaltante,
        { } alicuota when !AlicuotasAdmitidas.EsAdmitida(alicuota) => InfraccionConfiguracion.AlicuotaNoAdmitida,
        _ => null,
    };

    private static InfraccionConfiguracion? EvaluarMultiplo(decimal? valor)
    {
        if (valor is not { } multiplo)
        {
            return InfraccionConfiguracion.MultiploFaltante;
        }

        // D-01: el mínimo antes que la escala, así 0,001 informa el mínimo.
        if (multiplo < MultiploMinimo)
        {
            return InfraccionConfiguracion.MultiploMenorAlMinimo;
        }

        if (TieneMasDeDosDecimales(multiplo))
        {
            return InfraccionConfiguracion.MultiploConMasDeDosDecimales;
        }

        return multiplo > ImporteMaximo ? InfraccionConfiguracion.MultiploFueraDeRango : null;
    }

    private static InfraccionConfiguracion? EvaluarTope(decimal? valor)
    {
        // D-02: sin tope es un valor válido; queda el aviso de FR-08.
        if (valor is not { } tope)
        {
            return null;
        }

        if (tope <= 0m)
        {
            return InfraccionConfiguracion.TopeNoPositivo;
        }

        if (TieneMasDeDosDecimales(tope))
        {
            return InfraccionConfiguracion.TopeConMasDeDosDecimales;
        }

        return tope > ImporteMaximo ? InfraccionConfiguracion.TopeFueraDeRango : null;
    }

    private static bool TieneMasDeDosDecimales(decimal valor) => ReglasImporte.TieneMasDeDosDecimales(valor);
}
