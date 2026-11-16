using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Optica.Domain.Configuracion;

/// <summary>
/// Qué regla de los datos del emisor incumple un campo. Cada valor pertenece a un solo campo: el nombre empieza por el
/// campo (razón social, domicilio comercial, CUIT, Ingresos Brutos o inicio de actividades).
/// </summary>
public enum InfraccionDatosEmisor
{
    RazonSocialFaltante,
    RazonSocialConCaracteresInvalidos,
    RazonSocialDemasiadoLarga,
    DomicilioComercialFaltante,
    DomicilioComercialConCaracteresInvalidos,
    DomicilioComercialDemasiadoLargo,
    CuitFaltante,

    /// <summary>No son 11 dígitos, el verificador no corresponde o hay caracteres que no son dígitos ni guiones (AC-04).</summary>
    CuitInvalido,
    IngresosBrutosFaltante,
    IngresosBrutosConCaracteresInvalidos,
    IngresosBrutosDemasiadoLargo,
    InicioActividadesFaltante,

    /// <summary>La fecha no está escrita como <c>yyyy-MM-dd</c> o no existe en el calendario.</summary>
    InicioActividadesInvalida,

    /// <summary>La fecha es posterior a la fecha de hoy que recibe <see cref="DatosEmisor.ParaGrabar"/> (D-10, D-19).</summary>
    InicioActividadesPosteriorAHoy,
}

/// <summary>
/// Los datos de la óptica que exige el comprobante impreso (FR-01 a FR-03 de FEAT-009, ADR-028): razón social,
/// domicilio comercial, CUIT, número de Ingresos Brutos y fecha de inicio de actividades. Los cinco son obligatorios.
/// </summary>
/// <remarks>
/// Dos niveles, como <see cref="ConfiguracionNegocio"/>: <see cref="ParaGrabar"/> valida todo, incluida la fecha de
/// inicio no posterior a la fecha de hoy que pasa Application, y devuelve las infracciones por campo; <see cref="Leer"/>
/// reconstruye la fila guardada sin la regla de la fecha, para que un reloj atrasado no vuelva corrupta una fila válida
/// (D-19). Sin <c>ToString</c> con valores: la razón social y el CUIT pueden ser de una persona física (R-06).
/// </remarks>
public sealed class DatosEmisor
{
    public const int LargoMaximoRazonSocial = 120;
    public const int LargoMaximoDomicilioComercial = 200;
    public const int LargoMaximoIngresosBrutos = 30;

    /// <summary>El formato de la fecha de inicio de actividades: el del contrato de la Api y el de la base.</summary>
    public const string FormatoFecha = "yyyy-MM-dd";

    private DatosEmisor(
        string razonSocial, string domicilioComercial, Cuit cuit, string ingresosBrutos, DateOnly inicioActividades)
    {
        RazonSocial = razonSocial;
        DomicilioComercial = domicilioComercial;
        Cuit = cuit;
        IngresosBrutos = ingresosBrutos;
        InicioActividades = inicioActividades;
    }

    /// <summary>Recortada, de 1 a 120 caracteres, sin caracteres de control.</summary>
    public string RazonSocial { get; }

    /// <summary>Recortado, de 1 a 200 caracteres, sin caracteres de control.</summary>
    public string DomicilioComercial { get; }

    /// <summary>Con dígito verificador válido; se guarda sin guiones (AC-03).</summary>
    public Cuit Cuit { get; }

    /// <summary>Texto libre recortado, de 1 a 30 caracteres, sin caracteres de control (D-10).</summary>
    public string IngresosBrutos { get; }

    public DateOnly InicioActividades { get; }

    /// <summary>
    /// Los datos que se van a grabar, tal como llegan de la operadora. Evalúa los cinco campos y devuelve todas las
    /// infracciones, una por campo como máximo, en el orden razón social, domicilio, CUIT, Ingresos Brutos, inicio de
    /// actividades; por campo gana la primera regla incumplida (faltante, caracteres, largo o formato, fecha posterior a
    /// <paramref name="hoy"/>). Sin infracciones, el resultado trae la entidad con los textos recortados y el CUIT sin
    /// guiones. Nunca lanza.
    /// </summary>
    /// <param name="inicioActividades">La fecha como <c>yyyy-MM-dd</c>.</param>
    /// <param name="hoy">La fecha de hoy, que Application toma de su reloj.</param>
    public static ResultadoDatosEmisor ParaGrabar(
        string? razonSocial,
        string? domicilioComercial,
        string? cuit,
        string? ingresosBrutos,
        string? inicioActividades,
        DateOnly hoy)
    {
        var infracciones = new List<InfraccionDatosEmisor>(5);

        var textoRazonSocial = EvaluarTexto(razonSocial, LargoMaximoRazonSocial, InfraccionDatosEmisor.RazonSocialFaltante,
            InfraccionDatosEmisor.RazonSocialConCaracteresInvalidos, InfraccionDatosEmisor.RazonSocialDemasiadoLarga,
            infracciones);
        var textoDomicilio = EvaluarTexto(domicilioComercial, LargoMaximoDomicilioComercial,
            InfraccionDatosEmisor.DomicilioComercialFaltante, InfraccionDatosEmisor.DomicilioComercialConCaracteresInvalidos,
            InfraccionDatosEmisor.DomicilioComercialDemasiadoLargo, infracciones);
        var valorCuit = EvaluarCuit(cuit, infracciones);
        var textoIngresosBrutos = EvaluarTexto(ingresosBrutos, LargoMaximoIngresosBrutos,
            InfraccionDatosEmisor.IngresosBrutosFaltante, InfraccionDatosEmisor.IngresosBrutosConCaracteresInvalidos,
            InfraccionDatosEmisor.IngresosBrutosDemasiadoLargo, infracciones);
        var fecha = EvaluarFecha(inicioActividades, hoy, infracciones);

        if (infracciones.Count > 0)
        {
            return ResultadoDatosEmisor.Invalido(infracciones);
        }

        return ResultadoDatosEmisor.Valido(new DatosEmisor(
            textoRazonSocial!, textoDomicilio!, valorCuit!, textoIngresosBrutos!, fecha!.Value));
    }

    /// <summary>
    /// Los datos tal como están guardados (lectura): las reglas de <see cref="ParaGrabar"/> salvo la de la fecha
    /// posterior a hoy (D-19). El CUIT tiene que estar en forma canónica, sin guiones, como lo guarda la grabación.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Algún dato incumple las reglas. El mensaje nombra el campo y no incluye el valor recibido.
    /// </exception>
    public static DatosEmisor Leer(
        string razonSocial, string domicilioComercial, string cuit, string ingresosBrutos, DateOnly inicioActividades)
    {
        var infracciones = new List<InfraccionDatosEmisor>(4);
        var textoRazonSocial = EvaluarTexto(razonSocial, LargoMaximoRazonSocial, InfraccionDatosEmisor.RazonSocialFaltante,
            InfraccionDatosEmisor.RazonSocialConCaracteresInvalidos, InfraccionDatosEmisor.RazonSocialDemasiadoLarga,
            infracciones);
        var textoDomicilio = EvaluarTexto(domicilioComercial, LargoMaximoDomicilioComercial,
            InfraccionDatosEmisor.DomicilioComercialFaltante, InfraccionDatosEmisor.DomicilioComercialConCaracteresInvalidos,
            InfraccionDatosEmisor.DomicilioComercialDemasiadoLargo, infracciones);
        var valorCuit = Cuit.EsValidoCanonico(cuit) ? Cuit.Interpretar(cuit) : null;
        if (valorCuit is null)
        {
            infracciones.Add(InfraccionDatosEmisor.CuitInvalido);
        }

        var textoIngresosBrutos = EvaluarTexto(ingresosBrutos, LargoMaximoIngresosBrutos,
            InfraccionDatosEmisor.IngresosBrutosFaltante, InfraccionDatosEmisor.IngresosBrutosConCaracteresInvalidos,
            InfraccionDatosEmisor.IngresosBrutosDemasiadoLargo, infracciones);

        if (infracciones.Count > 0)
        {
            throw Error(infracciones[0]);
        }

        return new DatosEmisor(textoRazonSocial!, textoDomicilio!, valorCuit!, textoIngresosBrutos!, inicioActividades);
    }

    // Faltante (null o vacío después de recortar), después caracteres de control o UTF-16 inválido, y por último el
    // largo, medido en string.Length sobre el texto recortado (como PoliticaArticulo y PoliticaCliente).
    private static string? EvaluarTexto(
        string? texto,
        int largoMaximo,
        InfraccionDatosEmisor faltante,
        InfraccionDatosEmisor caracteresInvalidos,
        InfraccionDatosEmisor demasiadoLargo,
        List<InfraccionDatosEmisor> infracciones)
    {
        var recortado = texto is null ? string.Empty : RecorteTexto.Recortar(texto);
        InfraccionDatosEmisor? infraccion = recortado.Length == 0 ? faltante
            : !TieneCaracteresValidos(recortado) ? caracteresInvalidos
            : recortado.Length > largoMaximo ? demasiadoLargo
            : null;
        if (infraccion is { } valor)
        {
            infracciones.Add(valor);
            return null;
        }

        return recortado;
    }

    private static Cuit? EvaluarCuit(string? texto, List<InfraccionDatosEmisor> infracciones)
    {
        if (texto is null || RecorteTexto.Recortar(texto).Length == 0)
        {
            infracciones.Add(InfraccionDatosEmisor.CuitFaltante);
            return null;
        }

        var cuit = Cuit.Interpretar(texto);
        if (cuit is null)
        {
            infracciones.Add(InfraccionDatosEmisor.CuitInvalido);
        }

        return cuit;
    }

    private static DateOnly? EvaluarFecha(string? texto, DateOnly hoy, List<InfraccionDatosEmisor> infracciones)
    {
        if (texto is null || RecorteTexto.Recortar(texto).Length == 0)
        {
            infracciones.Add(InfraccionDatosEmisor.InicioActividadesFaltante);
            return null;
        }

        if (!DateOnly.TryParseExact(
                RecorteTexto.Recortar(texto), FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            infracciones.Add(InfraccionDatosEmisor.InicioActividadesInvalida);
            return null;
        }

        if (fecha > hoy)
        {
            infracciones.Add(InfraccionDatosEmisor.InicioActividadesPosteriorAHoy);
            return null;
        }

        return fecha;
    }

    // Sin caracteres de control (saltos de línea incluidos: los datos van en una línea del comprobante) y UTF-16 válido:
    // cada sustituto alto seguido de uno bajo, ningún sustituto bajo suelto.
    private static bool TieneCaracteresValidos(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            var caracter = texto[i];
            if (char.IsControl(caracter) || char.IsLowSurrogate(caracter))
            {
                return false;
            }

            if (char.IsHighSurrogate(caracter))
            {
                if (i + 1 >= texto.Length || !char.IsLowSurrogate(texto[i + 1]))
                {
                    return false;
                }

                i++;
            }
        }

        return true;
    }

    // Los mensajes no incluyen el valor recibido, como el resto de las invariantes del dominio.
    private static ArgumentException Error(InfraccionDatosEmisor infraccion) => infraccion switch
    {
        InfraccionDatosEmisor.RazonSocialFaltante
            or InfraccionDatosEmisor.RazonSocialConCaracteresInvalidos
            or InfraccionDatosEmisor.RazonSocialDemasiadoLarga =>
            new ArgumentException("La razón social no es válida.", "razonSocial"),
        InfraccionDatosEmisor.DomicilioComercialFaltante
            or InfraccionDatosEmisor.DomicilioComercialConCaracteresInvalidos
            or InfraccionDatosEmisor.DomicilioComercialDemasiadoLargo =>
            new ArgumentException("El domicilio comercial no es válido.", "domicilioComercial"),
        InfraccionDatosEmisor.CuitFaltante or InfraccionDatosEmisor.CuitInvalido =>
            new ArgumentException("El CUIT no es válido.", "cuit"),
        InfraccionDatosEmisor.IngresosBrutosFaltante
            or InfraccionDatosEmisor.IngresosBrutosConCaracteresInvalidos
            or InfraccionDatosEmisor.IngresosBrutosDemasiadoLargo =>
            new ArgumentException("El número de Ingresos Brutos no es válido.", "ingresosBrutos"),
        _ => throw new UnreachableException("Infracción de lectura de los datos del emisor sin campo asignado."),
    };
}

/// <summary>
/// El resultado de <see cref="DatosEmisor.ParaGrabar"/>: los datos válidos, o todas las infracciones, una por campo, sin
/// entidad.
/// </summary>
public sealed class ResultadoDatosEmisor
{
    private ResultadoDatosEmisor(DatosEmisor? datos, IReadOnlyList<InfraccionDatosEmisor> infracciones)
    {
        Datos = datos;
        Infracciones = infracciones;
    }

    /// <summary>Los datos listos para grabar; null si alguno incumple las reglas.</summary>
    public DatosEmisor? Datos { get; }

    /// <summary>Las infracciones, una por campo como máximo; vacío si los datos son válidos.</summary>
    public IReadOnlyList<InfraccionDatosEmisor> Infracciones { get; }

    /// <summary>Verdadero si no hay infracciones: <see cref="Datos"/> tiene la entidad.</summary>
    [MemberNotNullWhen(true, nameof(Datos))]
    public bool EsValido => Datos is not null;

    internal static ResultadoDatosEmisor Valido(DatosEmisor datos) => new(datos, []);

    internal static ResultadoDatosEmisor Invalido(List<InfraccionDatosEmisor> infracciones) => new(null, [.. infracciones]);
}
