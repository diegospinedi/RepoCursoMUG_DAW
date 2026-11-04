using System.Diagnostics;
using Optica.Application.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Application.Configuracion;

/// <summary>
/// Traduce las infracciones de <see cref="PoliticaConfiguracionNegocio"/> a un <see cref="ErrorValidacion"/> con el
/// campo de la Api y la acción que lo corrige. Las acciones son texto fijo: no repiten el valor recibido (R-07).
/// </summary>
public static class ValidacionConfiguracion
{
    public const string CampoAlicuotaIva = "alicuotaIva";
    public const string CampoCondicionFiscal = "condicionFiscal";
    public const string CampoMultiploRedondeo = "multiploRedondeo";
    public const string CampoTopeIdentificacion = "topeIdentificacion";

    public const string AccionAlicuotaFaltante = "Ingresá la alícuota de IVA.";
    public const string AccionAlicuotaFueraDeRango = "La alícuota de IVA debe estar entre 0 y 100.";
    public const string AccionHastaDosDecimales = "Ingresá hasta 2 decimales.";
    public const string AccionCondicionFiscal = "Elegí Responsable Inscripto o Monotributo.";
    public const string AccionMultiploFaltante = "Ingresá el múltiplo de redondeo comercial.";
    public const string AccionMultiploMinimo = "El valor mínimo es 0,01.";
    public const string AccionTopeNoPositivo = "El tope debe ser mayor a 0.";
    public const string AccionValorMenor = "Ingresá un valor menor.";

    /// <summary>
    /// Evalúa los cuatro campos con la política y devuelve todos los errores, uno por campo como máximo, en el orden
    /// alícuota, condición, múltiplo, tope. Vacío si los datos cumplen la política.
    /// </summary>
    public static IReadOnlyList<ErrorValidacion> Validar(DatosConfiguracion datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var infracciones = PoliticaConfiguracionNegocio.Evaluar(
            datos.AlicuotaIva, datos.CondicionFiscal, datos.MultiploRedondeo, datos.TopeIdentificacion);
        return [.. infracciones.Select(Traducir)];
    }

    /// <summary>El campo y la acción de una infracción.</summary>
    /// <exception cref="UnreachableException">
    /// La infracción no tiene traducción: el dominio sumó un valor y falta agregarlo aquí.
    /// </exception>
    public static ErrorValidacion Traducir(InfraccionConfiguracion infraccion) => infraccion switch
    {
        InfraccionConfiguracion.AlicuotaFaltante => new(CampoAlicuotaIva, AccionAlicuotaFaltante),
        InfraccionConfiguracion.AlicuotaFueraDeRango => new(CampoAlicuotaIva, AccionAlicuotaFueraDeRango),
        InfraccionConfiguracion.AlicuotaConMasDeDosDecimales => new(CampoAlicuotaIva, AccionHastaDosDecimales),
        InfraccionConfiguracion.CondicionFiscalInvalida => new(CampoCondicionFiscal, AccionCondicionFiscal),
        InfraccionConfiguracion.MultiploFaltante => new(CampoMultiploRedondeo, AccionMultiploFaltante),
        InfraccionConfiguracion.MultiploMenorAlMinimo => new(CampoMultiploRedondeo, AccionMultiploMinimo),
        InfraccionConfiguracion.MultiploConMasDeDosDecimales => new(CampoMultiploRedondeo, AccionHastaDosDecimales),
        InfraccionConfiguracion.MultiploFueraDeRango => new(CampoMultiploRedondeo, AccionValorMenor),
        InfraccionConfiguracion.TopeNoPositivo => new(CampoTopeIdentificacion, AccionTopeNoPositivo),
        InfraccionConfiguracion.TopeConMasDeDosDecimales => new(CampoTopeIdentificacion, AccionHastaDosDecimales),
        InfraccionConfiguracion.TopeFueraDeRango => new(CampoTopeIdentificacion, AccionValorMenor),
        _ => throw new UnreachableException("Infracción de configuración sin campo ni acción asignados."),
    };
}
