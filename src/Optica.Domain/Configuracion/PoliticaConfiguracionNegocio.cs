using Optica.Domain.Comun;

namespace Optica.Domain.Configuracion;

/// <summary>
/// Qué regla de la configuración de negocio incumple un campo. Cada valor pertenece a un solo campo: el nombre
/// empieza por el campo (alícuota, condición, múltiplo o tope).
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
}

/// <summary>
/// Reglas de los parámetros de negocio (FR-02 a FR-05, D-01 y D-02 de FEAT-001c) y la única traducción entre
/// <see cref="CondicionFiscal"/> y su texto (D-07).
/// </summary>
public static class PoliticaConfiguracionNegocio
{
    public const decimal AlicuotaMinima = 0m;
    public const decimal AlicuotaMaxima = 100m;
    public const decimal MultiploMinimo = 0.01m;

    /// <summary>Alias de <see cref="ReglasImporte.ImporteMaximo"/>, que es donde vive la regla (D-12 de FEAT-002a).</summary>
    public const decimal ImporteMaximo = ReglasImporte.ImporteMaximo;

    private const string TextoResponsableInscripto = "responsable_inscripto";
    private const string TextoMonotributo = "monotributo";

    /// <summary>
    /// Evalúa los cuatro campos y devuelve sus infracciones, una por campo como máximo, en el orden alícuota,
    /// condición, múltiplo, tope. Por campo gana la primera regla incumplida. Nunca lanza.
    /// </summary>
    public static IReadOnlyList<InfraccionConfiguracion> Evaluar(
        decimal? alicuotaIva, string? condicionFiscal, decimal? multiploRedondeo, decimal? topeIdentificacion)
    {
        var infracciones = new List<InfraccionConfiguracion>(4);

        if (EvaluarAlicuota(alicuotaIva) is { } alicuota)
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
