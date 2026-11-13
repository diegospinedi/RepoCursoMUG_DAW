using System.Text;
using Optica.Domain.Catalogo;

namespace Optica.Domain.Presupuestos;

/// <summary>
/// El filtro de la búsqueda de presupuestos (FR-01 a FR-07 de FEAT-006). Apellido y Nombre quedan plegados con
/// <see cref="PlegadoBusqueda.Plegar"/>, la misma regla que aplica <c>plegar</c> en la base (FR-03, D-05, ADR-011); el
/// DNI queda sin puntos, guiones ni espacios (FR-04, D-06). Un texto que queda vacío después de normalizar es un filtro
/// no cargado (null); sin ninguno cargado, la búsqueda trae todos (FR-07).
/// </summary>
/// <remarks>
/// Lleva datos personales (Ley 25.326): no sobrescribe <see cref="object.ToString"/> ni se registra en el log.
/// </remarks>
public sealed class FiltroPresupuestos
{
    private const string MensajeRangoInvalido = "La fecha Desde no puede ser posterior a Hasta.";

    private FiltroPresupuestos(string? apellido, string? nombre, string? dni, DateOnly? desde, DateOnly? hasta)
    {
        Apellido = apellido;
        Nombre = nombre;
        Dni = dni;
        Desde = desde;
        Hasta = hasta;
    }

    /// <summary>El Apellido plegado, o null si no se cargó.</summary>
    public string? Apellido { get; }

    /// <summary>El Nombre plegado, o null si no se cargó.</summary>
    public string? Nombre { get; }

    /// <summary>
    /// El DNI sin puntos, guiones ni espacios, o null si no se cargó. Puede tener otros caracteres: entonces no coincide
    /// con ningún presupuesto, sin error (D-06).
    /// </summary>
    public string? Dni { get; }

    /// <summary>El límite inferior de la fecha, inclusive (FR-05), o null.</summary>
    public DateOnly? Desde { get; }

    /// <summary>El límite superior de la fecha, inclusive (FR-05), o null.</summary>
    public DateOnly? Hasta { get; }

    /// <summary>El filtro con los textos normalizados; ningún dato es obligatorio.</summary>
    /// <exception cref="FiltroInvalidoException">
    /// <paramref name="desde"/> es posterior a <paramref name="hasta"/> (FR-06). El mensaje es fijo y no lleva valores.
    /// </exception>
    public static FiltroPresupuestos Crear(
        string? apellido, string? nombre, string? dni, DateOnly? desde, DateOnly? hasta)
    {
        if (desde is { } inicio && hasta is { } fin && inicio > fin)
        {
            throw new FiltroInvalidoException(MensajeRangoInvalido);
        }

        return new FiltroPresupuestos(Plegado(apellido), Plegado(nombre), NormalizarDni(dni), desde, hasta);
    }

    private static string? Plegado(string? texto) =>
        texto is null ? null : VacioANulo(PlegadoBusqueda.Plegar(texto));

    // D-06: se quitan los separadores que la operadora escribe; cualquier otro carácter queda.
    private static string? NormalizarDni(string? texto)
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

/// <summary>
/// El filtro de la búsqueda de presupuestos no cumple una regla del dominio (FR-06). El mensaje es fijo: no copia las
/// fechas ni los textos recibidos.
/// </summary>
public sealed class FiltroInvalidoException(string mensaje) : Exception(mensaje);
