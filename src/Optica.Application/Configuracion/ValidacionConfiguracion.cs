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
    public const string CampoPuntoVenta = "puntoVenta";

    public const string AccionAlicuotaFaltante = "Ingresá la alícuota de IVA.";
    public const string AccionHastaDosDecimales = AccionesNumericas.HastaDosDecimales;
    public const string AccionCondicionFiscal = "Elegí Responsable Inscripto o Monotributo.";
    public const string AccionMultiploFaltante = "Ingresá el múltiplo de redondeo comercial.";
    public const string AccionMultiploMinimo = "El valor mínimo es 0,01.";
    public const string AccionTopeNoPositivo = "El tope debe ser mayor a 0.";
    public const string AccionValorMenor = AccionesNumericas.ValorMenor;
    public const string AccionAlicuotaNoAdmitida = "La alícuota de IVA debe ser 0, 2,5, 5, 10,5, 21 o 27.";
    public const string AccionPuntoVenta = "El punto de venta debe ser un número entero de 1 a 99999.";

    /// <summary>
    /// Evalúa los campos con la política de escritura (las 6 alícuotas de ARCA, FR-26 de FEAT-007) y devuelve todos los
    /// errores, uno por campo como máximo, en el orden alícuota, condición, múltiplo, tope, punto de venta. Vacío si los
    /// datos cumplen la política.
    /// </summary>
    public static IReadOnlyList<ErrorValidacion> Validar(DatosConfiguracion datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var infracciones = PoliticaConfiguracionNegocio.EvaluarParaGrabar(
            datos.AlicuotaIva, datos.CondicionFiscal, datos.MultiploRedondeo, datos.TopeIdentificacion, datos.PuntoVenta);
        return [.. infracciones.Select(Traducir)];
    }

    /// <summary>El campo y la acción de una infracción.</summary>
    /// <exception cref="UnreachableException">
    /// La infracción no tiene traducción: el dominio sumó un valor y falta agregarlo aquí.
    /// </exception>
    public static ErrorValidacion Traducir(InfraccionConfiguracion infraccion) => infraccion switch
    {
        InfraccionConfiguracion.AlicuotaFaltante => new(CampoAlicuotaIva, AccionAlicuotaFaltante),
        // Decisión del usuario (2026-11-13): toda alícuota inválida que llega a la Api informa la lista de ARCA. Validar
        // ya no produce estas dos (solo la lectura, D-06), pero la traducción queda total.
        InfraccionConfiguracion.AlicuotaFueraDeRango
            or InfraccionConfiguracion.AlicuotaConMasDeDosDecimales
            or InfraccionConfiguracion.AlicuotaNoAdmitida => new(CampoAlicuotaIva, AccionAlicuotaNoAdmitida),
        InfraccionConfiguracion.CondicionFiscalInvalida => new(CampoCondicionFiscal, AccionCondicionFiscal),
        InfraccionConfiguracion.MultiploFaltante => new(CampoMultiploRedondeo, AccionMultiploFaltante),
        InfraccionConfiguracion.MultiploMenorAlMinimo => new(CampoMultiploRedondeo, AccionMultiploMinimo),
        InfraccionConfiguracion.MultiploConMasDeDosDecimales => new(CampoMultiploRedondeo, AccionHastaDosDecimales),
        InfraccionConfiguracion.MultiploFueraDeRango => new(CampoMultiploRedondeo, AccionValorMenor),
        InfraccionConfiguracion.TopeNoPositivo => new(CampoTopeIdentificacion, AccionTopeNoPositivo),
        InfraccionConfiguracion.TopeConMasDeDosDecimales => new(CampoTopeIdentificacion, AccionHastaDosDecimales),
        InfraccionConfiguracion.TopeFueraDeRango => new(CampoTopeIdentificacion, AccionValorMenor),
        InfraccionConfiguracion.PuntoVentaFueraDeRango => new(CampoPuntoVenta, AccionPuntoVenta),
        _ => throw new UnreachableException("Infracción de configuración sin campo ni acción asignados."),
    };
}
