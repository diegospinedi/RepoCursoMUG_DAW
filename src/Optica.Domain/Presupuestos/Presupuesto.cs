namespace Optica.Domain.Presupuestos;

/// <summary>Los dos estados del presupuesto (FR-08). FEAT-003a solo graba <see cref="Borrador"/>.</summary>
public enum EstadoPresupuesto
{
    Borrador,
    Final,
}

/// <summary>La regla de numeración de los presupuestos (FR-06, D-02, D-07).</summary>
public static class NumeracionPresupuesto
{
    /// <summary>1 si no hay ningún presupuesto; si no, el último número más 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">El último número es menor que 1: ningún presupuesto lo tiene.</exception>
    /// <exception cref="InvalidOperationException">El último número es <see cref="long.MaxValue"/>: no hay siguiente.</exception>
    public static long Siguiente(long? ultimo)
    {
        if (ultimo is not { } numero)
        {
            return 1;
        }

        if (numero < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ultimo), "El último número de presupuesto no es válido.");
        }

        if (numero == long.MaxValue)
        {
            throw new InvalidOperationException("No hay un número de presupuesto siguiente.");
        }

        return numero + 1;
    }
}

/// <summary>
/// Un presupuesto con su número, su fecha, su estado, su cliente y al menos una línea (FR-01, FR-06 a FR-08, FR-15).
/// Lleva datos personales a través de <see cref="Cliente"/>: no sobrescribe <see cref="object.ToString"/> (D-17).
/// </summary>
public sealed class Presupuesto
{
    /// <summary>Reconstruye un presupuesto guardado; el alta usa <see cref="Nuevo"/>.</summary>
    /// <exception cref="ArgumentNullException">El cliente o la lista de líneas es null.</exception>
    /// <exception cref="ArgumentException">
    /// El número es menor que 1, la fecha es la por defecto de <see cref="DateOnly"/>, el estado no existe, o no hay líneas o alguna es null. El mensaje nombra el campo y
    /// no incluye el valor recibido.
    /// </exception>
    public Presupuesto(
        long numero, DateOnly fecha, EstadoPresupuesto estado, ClientePresupuesto cliente,
        IReadOnlyList<LineaPresupuesto> lineas)
    {
        if (numero < 1)
        {
            throw new ArgumentException("El número de presupuesto no es válido.", nameof(numero));
        }

        if (fecha == default)
        {
            throw new ArgumentException("La fecha del presupuesto no es válida.", nameof(fecha));
        }

        if (!Enum.IsDefined(estado))
        {
            throw new ArgumentException("El estado del presupuesto no es válido.", nameof(estado));
        }

        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(lineas);
        if (lineas.Count == 0)
        {
            throw new ArgumentException("El presupuesto necesita al menos una línea.", nameof(lineas));
        }

        foreach (var linea in lineas)
        {
            if (linea is null)
            {
                throw new ArgumentException("Las líneas del presupuesto no son válidas.", nameof(lineas));
            }
        }

        Numero = numero;
        Fecha = fecha;
        Estado = estado;
        Cliente = cliente;
        Lineas = [.. lineas];
    }

    /// <summary>Un presupuesto grabado por primera vez: queda en <see cref="EstadoPresupuesto.Borrador"/> (FR-08).</summary>
    /// <exception cref="ArgumentException">Igual que el constructor.</exception>
    public static Presupuesto Nuevo(
        long numero, DateOnly fecha, ClientePresupuesto cliente, IReadOnlyList<LineaPresupuesto> lineas) =>
        new(numero, fecha, EstadoPresupuesto.Borrador, cliente, lineas);

    /// <summary>Desde 1 (FR-06).</summary>
    public long Numero { get; }

    /// <summary>La fecha local del día en que se grabó por primera vez, sin hora (FR-15, D-06).</summary>
    public DateOnly Fecha { get; }

    public EstadoPresupuesto Estado { get; }

    public ClientePresupuesto Cliente { get; }

    /// <summary>Al menos una línea (FR-07), en el orden en que se cargaron.</summary>
    public IReadOnlyList<LineaPresupuesto> Lineas { get; }
}
