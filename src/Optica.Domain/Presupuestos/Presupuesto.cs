using Optica.Domain.Comun;

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
    /// <param name="version">
    /// La versión para la concurrencia optimista, de 0 a <see cref="VersionMaxima"/>; la incrementa la persistencia
    /// (D-01).
    /// </param>
    /// <exception cref="ArgumentNullException">El cliente o la lista de líneas es null.</exception>
    /// <exception cref="ArgumentException">
    /// El número es menor que 1, la fecha es la por defecto de <see cref="DateOnly"/>, el estado no existe, la versión
    /// no está entre 0 y <see cref="VersionMaxima"/>, no hay
    /// líneas o alguna es null, o el total supera <see cref="ReglasImporte.ImporteMaximo"/>. El mensaje nombra el
    /// campo y no incluye el valor recibido.
    /// </exception>
    public Presupuesto(
        long numero, DateOnly fecha, EstadoPresupuesto estado, ClientePresupuesto cliente,
        IReadOnlyList<LineaPresupuesto> lineas, long version)
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

        if (version is < 0 or > VersionMaxima)
        {
            throw new ArgumentException("La versión del presupuesto no es válida.", nameof(version));
        }

        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(lineas);
        if (lineas.Count == 0)
        {
            throw new ArgumentException("El presupuesto necesita al menos una línea.", nameof(lineas));
        }

        var total = 0m;
        foreach (var linea in lineas)
        {
            if (linea is null)
            {
                throw new ArgumentException("Las líneas del presupuesto no son válidas.", nameof(lineas));
            }

            // Cada Precio Final ya es como máximo ImporteMaximo: la suma parcial no pasa de 2 × ImporteMaximo, que
            // entra en decimal sin desbordar, porque se corta en cuanto supera el límite.
            total += linea.PrecioFinal;
            if (total > ReglasImporte.ImporteMaximo)
            {
                throw new ArgumentException("El total del presupuesto es demasiado alto.", nameof(lineas));
            }
        }

        Numero = numero;
        Fecha = fecha;
        Estado = estado;
        Cliente = cliente;
        Lineas = [.. lineas];
        Total = total;
        Version = version;
    }

    /// <summary>
    /// La mayor versión admitida, 2^53 − 1: el mayor entero que JavaScript representa sin perder precisión (D-01). A
    /// diferencia de <c>Articulo</c>, que solo rechaza negativos y deja el tope a la aplicación, el presupuesto valida
    /// el rango completo en el dominio (D-11).
    /// </summary>
    public const long VersionMaxima = 9_007_199_254_740_991;

    private const string MensajeFinalNoModificable = "Un presupuesto en Final no se puede modificar.";
    private const string MensajeYaEnFinal = "El presupuesto ya está en Final.";

    /// <summary>
    /// Un presupuesto con el mismo número, fecha, estado <see cref="EstadoPresupuesto.Borrador"/> y versión, y el
    /// cliente y las líneas recibidos (FR-01, FR-02). No cambia este presupuesto. La versión la incrementa quien
    /// persiste.
    /// </summary>
    /// <exception cref="InvalidOperationException">El presupuesto está en Final (FR-05): no se modifica.</exception>
    /// <exception cref="ArgumentNullException">El cliente o la lista de líneas es null.</exception>
    /// <exception cref="ArgumentException">Igual que el constructor: sin líneas, alguna null o total excedido.</exception>
    public Presupuesto Modificar(ClientePresupuesto cliente, IReadOnlyList<LineaPresupuesto> lineas)
    {
        if (Estado == EstadoPresupuesto.Final)
        {
            throw new InvalidOperationException(MensajeFinalNoModificable);
        }

        return new Presupuesto(Numero, Fecha, EstadoPresupuesto.Borrador, cliente, lineas, Version);
    }

    /// <summary>
    /// El mismo presupuesto en <see cref="EstadoPresupuesto.Final"/>, con su número, fecha, versión, cliente y líneas
    /// (FR-03). No cambia este presupuesto. No existe la operación inversa (FR-06).
    /// </summary>
    /// <exception cref="InvalidOperationException">El presupuesto ya está en Final.</exception>
    public Presupuesto PasarAFinal()
    {
        if (Estado == EstadoPresupuesto.Final)
        {
            throw new InvalidOperationException(MensajeYaEnFinal);
        }

        return new Presupuesto(Numero, Fecha, EstadoPresupuesto.Final, Cliente, Lineas, Version);
    }

    /// <summary>
    /// Un presupuesto grabado por primera vez: queda en <see cref="EstadoPresupuesto.Borrador"/> (FR-08) con versión 0.
    /// </summary>
    /// <exception cref="ArgumentException">Igual que el constructor.</exception>
    public static Presupuesto Nuevo(
        long numero, DateOnly fecha, ClientePresupuesto cliente, IReadOnlyList<LineaPresupuesto> lineas) =>
        new(numero, fecha, EstadoPresupuesto.Borrador, cliente, lineas, version: 0);

    /// <summary>La versión para la concurrencia optimista, de 0 a <see cref="VersionMaxima"/> (D-01).</summary>
    public long Version { get; }

    /// <summary>Desde 1 (FR-06).</summary>
    public long Numero { get; }

    /// <summary>La fecha local del día en que se grabó por primera vez, sin hora (FR-15, D-06).</summary>
    public DateOnly Fecha { get; }

    public EstadoPresupuesto Estado { get; }

    public ClientePresupuesto Cliente { get; }

    /// <summary>Al menos una línea (FR-07), en el orden en que se cargaron.</summary>
    public IReadOnlyList<LineaPresupuesto> Lineas { get; }

    /// <summary>
    /// La suma de los Precios Finales ya redondeados de las líneas, con IVA incluido (FR-06, D-05). No se guarda: se
    /// calcula al construir el presupuesto. Nunca supera <see cref="ReglasImporte.ImporteMaximo"/>.
    /// </summary>
    public decimal Total { get; }
}
