namespace Optica.Domain.Presupuestos;

/// <summary>
/// Un resultado de la búsqueda de presupuestos (FR-09 de FEAT-006): número, fecha, Apellido, Nombre, DNI, estado y total.
/// El total es la suma de los Precio Final de sus líneas, calculada en la consulta (D-08).
/// </summary>
/// <remarks>
/// Lleva datos personales (Ley 25.326): es una clase y no un record, así <see cref="object.ToString"/> no muestra
/// Apellido, Nombre ni DNI.
/// </remarks>
public sealed class ResumenPresupuesto
{
    /// <exception cref="ArgumentNullException">Apellido, Nombre o DNI es null.</exception>
    public ResumenPresupuesto(
        long numero, DateOnly fecha, string apellido, string nombre, string dni, EstadoPresupuesto estado,
        decimal total)
    {
        ArgumentNullException.ThrowIfNull(apellido);
        ArgumentNullException.ThrowIfNull(nombre);
        ArgumentNullException.ThrowIfNull(dni);

        Numero = numero;
        Fecha = fecha;
        Apellido = apellido;
        Nombre = nombre;
        Dni = dni;
        Estado = estado;
        Total = total;
    }

    /// <summary>El número del presupuesto.</summary>
    public long Numero { get; }

    /// <summary>La fecha del presupuesto, sin hora.</summary>
    public DateOnly Fecha { get; }

    /// <summary>El Apellido del cliente, como se guarda (sin plegar).</summary>
    public string Apellido { get; }

    /// <summary>El Nombre del cliente, como se guarda (sin plegar).</summary>
    public string Nombre { get; }

    /// <summary>El DNI como se guarda: solo dígitos.</summary>
    public string Dni { get; }

    /// <summary>Borrador o Final.</summary>
    public EstadoPresupuesto Estado { get; }

    /// <summary>La suma de los Precio Final de las líneas.</summary>
    public decimal Total { get; }
}
