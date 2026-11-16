using System.Text;
using Optica.Domain.Presupuestos;

namespace Optica.Domain.Facturacion;

/// <summary>
/// El filtro de la búsqueda de facturas (FR-17 a FR-21 de FEAT-009). Apellido y Nombre quedan plegados con
/// <see cref="PlegadoCliente.Plegar"/>, la misma regla que aplica <c>plegar</c> en la base (FR-19, ADR-011, D-13); el DNI
/// y el número de comprobante quedan sin puntos, guiones ni espacios (FR-20). Un texto que queda vacío después de
/// normalizar es un filtro no cargado (null); sin ninguno cargado, la búsqueda trae todas (FR-22).
/// </summary>
/// <remarks>
/// Lleva datos personales (Ley 25.326): no sobrescribe <see cref="object.ToString"/> ni se registra en el log (R-01).
/// </remarks>
public sealed class FiltroFacturas
{
    private const string MensajeRangoInvalido = "La fecha Desde no puede ser posterior a Hasta.";

    private FiltroFacturas(
        string? apellido, string? nombre, string? dni, string? numero, DateOnly? desde, DateOnly? hasta)
    {
        Apellido = apellido;
        Nombre = nombre;
        Dni = dni;
        Numero = numero;
        Desde = desde;
        Hasta = hasta;
    }

    /// <summary>El Apellido plegado, o null si no se cargó.</summary>
    public string? Apellido { get; }

    /// <summary>El Nombre plegado, o null si no se cargó.</summary>
    public string? Nombre { get; }

    /// <summary>
    /// El DNI sin puntos, guiones ni espacios, o null si no se cargó. Puede tener otros caracteres: entonces no coincide
    /// con ninguna factura, sin error.
    /// </summary>
    public string? Dni { get; }

    /// <summary>
    /// El número de comprobante sin puntos, guiones ni espacios, o null si no se cargó. Se busca dentro de los dígitos
    /// del texto visible: "0003-00034561" da "000300034561" (D-27).
    /// </summary>
    public string? Numero { get; }

    /// <summary>El límite inferior de la fecha, inclusive (FR-21), o null.</summary>
    public DateOnly? Desde { get; }

    /// <summary>El límite superior de la fecha, inclusive (FR-21), o null.</summary>
    public DateOnly? Hasta { get; }

    /// <summary>El filtro con los textos normalizados; ningún dato es obligatorio.</summary>
    /// <exception cref="FiltroFacturasInvalidoException">
    /// <paramref name="desde"/> es posterior a <paramref name="hasta"/> (FR-21, AC-22). El mensaje es fijo y no lleva
    /// valores.
    /// </exception>
    public static FiltroFacturas Crear(
        string? apellido, string? nombre, string? dni, string? numero, DateOnly? desde, DateOnly? hasta)
    {
        if (desde is { } inicio && hasta is { } fin && inicio > fin)
        {
            throw new FiltroFacturasInvalidoException(MensajeRangoInvalido);
        }

        return new FiltroFacturas(
            Plegado(apellido), Plegado(nombre), SinSeparadores(dni), SinSeparadores(numero), desde, hasta);
    }

    private static string? Plegado(string? texto) =>
        texto is null ? null : VacioANulo(PlegadoCliente.Plegar(texto));

    // FR-20: se quitan los separadores que la operadora escribe; cualquier otro carácter queda.
    private static string? SinSeparadores(string? texto)
    {
        if (texto is null)
        {
            return null;
        }

        var resultado = new StringBuilder(texto.Length);
        foreach (var caracter in texto)
        {
            if (caracter is not ('.' or '-') && !char.IsWhiteSpace(caracter))
            {
                resultado.Append(caracter);
            }
        }

        return VacioANulo(resultado.ToString());
    }

    private static string? VacioANulo(string texto) => texto.Length == 0 ? null : texto;
}
