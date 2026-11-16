using System.Diagnostics;
using Optica.Application.Comun;
using Optica.Domain.Configuracion;

namespace Optica.Application.Configuracion;

/// <summary>
/// Traduce las infracciones de <see cref="PoliticaConfiguracionNegocio"/> y las de <see cref="DatosEmisor"/> (FEAT-009) a
/// un <see cref="ErrorValidacion"/> con el campo de la Api y la acción que lo corrige. Las acciones son texto fijo: no repiten el valor recibido (R-07).
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

    // Datos del emisor (FEAT-009, ADR-028): los campos del cuerpo de POST /api/configuracion/emisor y sus acciones.

    public const string CampoRazonSocial = "razonSocial";
    public const string CampoDomicilioComercial = "domicilioComercial";
    public const string CampoCuit = "cuit";
    public const string CampoIngresosBrutos = "ingresosBrutos";
    public const string CampoInicioActividades = "inicioActividades";

    public const string AccionRazonSocialFaltante = "Ingresá la razón social.";
    public const string AccionRazonSocialCaracteres = "Escribí la razón social en una sola línea, sin caracteres especiales.";
    public const string AccionRazonSocialLarga = "La razón social puede tener hasta 120 caracteres.";
    public const string AccionDomicilioComercialFaltante = "Ingresá el domicilio comercial.";
    public const string AccionDomicilioComercialCaracteres =
        "Escribí el domicilio comercial en una sola línea, sin caracteres especiales.";
    public const string AccionDomicilioComercialLargo = "El domicilio comercial puede tener hasta 200 caracteres.";
    public const string AccionCuitFaltante = "Ingresá el CUIT.";
    public const string AccionCuitInvalido = "El CUIT no es válido: tienen que ser 11 dígitos, con o sin guiones.";
    public const string AccionIngresosBrutosFaltante = "Ingresá el número de Ingresos Brutos.";
    public const string AccionIngresosBrutosCaracteres =
        "Escribí el número de Ingresos Brutos en una sola línea, sin caracteres especiales.";
    public const string AccionIngresosBrutosLargo = "El número de Ingresos Brutos puede tener hasta 30 caracteres.";
    public const string AccionInicioActividadesFaltante = "Ingresá la fecha de inicio de actividades.";
    public const string AccionInicioActividadesInvalida = "La fecha de inicio de actividades no es válida.";
    public const string AccionInicioActividadesPosterior = "La fecha de inicio de actividades no puede ser posterior a hoy.";

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

    /// <summary>El campo y la acción de una infracción de los datos del emisor (FEAT-009). Las acciones no repiten valores.</summary>
    /// <exception cref="UnreachableException">
    /// La infracción no tiene traducción: el dominio sumó un valor y falta agregarlo aquí.
    /// </exception>
    public static ErrorValidacion Traducir(InfraccionDatosEmisor infraccion) => infraccion switch
    {
        InfraccionDatosEmisor.RazonSocialFaltante => new(CampoRazonSocial, AccionRazonSocialFaltante),
        InfraccionDatosEmisor.RazonSocialConCaracteresInvalidos => new(CampoRazonSocial, AccionRazonSocialCaracteres),
        InfraccionDatosEmisor.RazonSocialDemasiadoLarga => new(CampoRazonSocial, AccionRazonSocialLarga),
        InfraccionDatosEmisor.DomicilioComercialFaltante => new(CampoDomicilioComercial, AccionDomicilioComercialFaltante),
        InfraccionDatosEmisor.DomicilioComercialConCaracteresInvalidos =>
            new(CampoDomicilioComercial, AccionDomicilioComercialCaracteres),
        InfraccionDatosEmisor.DomicilioComercialDemasiadoLargo => new(CampoDomicilioComercial, AccionDomicilioComercialLargo),
        InfraccionDatosEmisor.CuitFaltante => new(CampoCuit, AccionCuitFaltante),
        InfraccionDatosEmisor.CuitInvalido => new(CampoCuit, AccionCuitInvalido),
        InfraccionDatosEmisor.IngresosBrutosFaltante => new(CampoIngresosBrutos, AccionIngresosBrutosFaltante),
        InfraccionDatosEmisor.IngresosBrutosConCaracteresInvalidos => new(CampoIngresosBrutos, AccionIngresosBrutosCaracteres),
        InfraccionDatosEmisor.IngresosBrutosDemasiadoLargo => new(CampoIngresosBrutos, AccionIngresosBrutosLargo),
        InfraccionDatosEmisor.InicioActividadesFaltante => new(CampoInicioActividades, AccionInicioActividadesFaltante),
        InfraccionDatosEmisor.InicioActividadesInvalida => new(CampoInicioActividades, AccionInicioActividadesInvalida),
        InfraccionDatosEmisor.InicioActividadesPosteriorAHoy => new(CampoInicioActividades, AccionInicioActividadesPosterior),
        _ => throw new UnreachableException("Infracción de los datos del emisor sin campo ni acción asignados."),
    };
}
