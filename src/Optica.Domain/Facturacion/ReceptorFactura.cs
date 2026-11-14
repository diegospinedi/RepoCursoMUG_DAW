namespace Optica.Domain.Facturacion;

/// <summary>
/// El receptor de una factura a consumidor final: sin identificar si el total no supera el tope (FR-10) o con el DNI
/// del cliente si lo supera (FR-11). El DNI es un dato personal (Ley 25.326): no sobrescribe
/// <see cref="object.ToString"/> ni se registra en el log (D-17 de FEAT-003a). Es una clase y no un <c>record</c> a
/// propósito: el <c>ToString</c> generado de un <c>record</c> mostraría el DNI.
/// </summary>
public sealed class ReceptorFactura
{
    private ReceptorFactura(string? dni)
    {
        Dni = dni;
    }

    /// <summary>Consumidor Final sin identificar (FR-10).</summary>
    public static ReceptorFactura SinIdentificar { get; } = new(null);

    /// <summary>El DNI del cliente: 7 u 8 dígitos ASCII, sin puntos; null si el receptor no se identifica.</summary>
    public string? Dni { get; }

    /// <summary>Verdadero si el receptor se identifica con su DNI (FR-11).</summary>
    public bool EstaIdentificado => Dni is not null;

    /// <summary>Consumidor Final identificado con su DNI (FR-11).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="dni"/> es null.</exception>
    /// <exception cref="ArgumentException">
    /// El DNI no son 7 u 8 dígitos ASCII (ya sin puntos, como lo guarda el presupuesto). El mensaje no incluye el valor.
    /// </exception>
    public static ReceptorFactura ConDni(string dni)
    {
        ArgumentNullException.ThrowIfNull(dni);
        if (dni.Length is < 7 or > 8 || !dni.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("El DNI del receptor no es válido.", nameof(dni));
        }

        return new ReceptorFactura(dni);
    }
}
