using Microsoft.EntityFrameworkCore;
using Optica.Domain.Presupuestos;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Presupuestos sobre las tablas <c>Presupuesto</c> y <c>LineaPresupuesto</c> (FEAT-003a). Mapea filas y entidades sin
/// aritmética: la conversión a centésimos vive en <see cref="OpticaDbContext"/> (ADR-008) y la fecha usa el mapeo de
/// <see cref="DateOnly"/> de EF Core a TEXT <c>yyyy-MM-dd</c>. <see cref="Agregar"/> solo deja el alta en el contexto
/// compartido, que confirma <see cref="UnidadDeTrabajoEf"/>; un número repetido se informa ahí como conflicto de
/// unicidad, por la clave primaria (D-07, ADR-015).
/// </summary>
/// <remarks>
/// Lleva datos personales (Ley 25.326): no registra nada en el log y sus mensajes de error son fijos, sin valores (D-17).
/// </remarks>
public sealed class PresupuestoRepository(OpticaDbContext contexto) : IPresupuestoRepository
{
    private const string MensajeFilaInvalida = "El presupuesto guardado no es válido.";
    private const string MensajePresupuestoInvalido = "El presupuesto a grabar no tiene número, cliente o líneas.";

    private const string EstadoBorrador = "borrador";
    private const string EstadoFinal = "final";

    /// <summary>
    /// <c>MAX(Numero)</c>, o null si no hay presupuestos (D-07). El alta lo llama dentro de la transacción de escritura
    /// (ADR-013), así ninguna otra grabación confirma entre la lectura y la grabación.
    /// </summary>
    public Task<long?> ObtenerUltimoNumeroAsync(CancellationToken cancelacion = default) =>
        contexto.Presupuestos.AsNoTracking().MaxAsync(p => (long?)p.Numero, cancelacion);

    /// <summary>
    /// Registra el presupuesto y sus líneas con <c>Orden</c> 1..n en el orden recibido; se guarda al confirmar la unidad de
    /// trabajo.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="presupuesto"/> es null.</exception>
    /// <exception cref="ArgumentException">
    /// El presupuesto no tiene número, cliente o líneas: no salió de sus constructores. El mensaje no lleva valores.
    /// </exception>
    public void Agregar(Presupuesto presupuesto)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        if (presupuesto.Numero < 1 || presupuesto.Cliente is null || presupuesto.Lineas is not { Count: > 0 } lineas)
        {
            throw new ArgumentException(MensajePresupuestoInvalido, nameof(presupuesto));
        }

        var cliente = presupuesto.Cliente;
        contexto.Presupuestos.Add(new PresupuestoFila
        {
            Numero = presupuesto.Numero,
            Fecha = presupuesto.Fecha,
            Estado = TextoDe(presupuesto.Estado),
            Apellido = cliente.Apellido,
            Nombre = cliente.Nombre,
            Dni = cliente.Dni,
            Domicilio = cliente.Domicilio,
            Email = cliente.Email,
            Telefono = cliente.Telefono,
            Lineas = [.. lineas.Select((linea, indice) => new LineaPresupuestoFila
            {
                PresupuestoNumero = presupuesto.Numero,
                Orden = indice + 1L,
                CodigoArticulo = linea.CodigoArticulo,
                Descripcion = linea.Descripcion,
                PrecioUnitario = linea.PrecioUnitario,
                PrecioConDescuento = linea.PrecioConDescuento,
                PrecioFinal = linea.PrecioFinal,
                Cantidad = linea.Cantidad,
                PorcentajeDescuento = linea.PorcentajeDescuento,
            })],
        });
    }

    /// <summary>Sin seguimiento, con las líneas ordenadas por <c>Orden</c>; null si no existe.</summary>
    /// <exception cref="InvalidOperationException">La fila no cumple las reglas del dominio: estado corrupto.</exception>
    public async Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default)
    {
        PresupuestoFila? fila;
        try
        {
            fila = await contexto.Presupuestos.AsNoTracking()
                .Include(p => p.Lineas.OrderBy(l => l.Orden))
                .SingleOrDefaultAsync(p => p.Numero == numero, cancelacion);
        }
        catch (FormatException)
        {
            // Microsoft.Data.Sqlite no pudo leer la fecha guardada ("2026-02-30" pasa el CHECK de formato). Sin la causa:
            // su mensaje repite el texto guardado.
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        return fila is null ? null : AEntidad(fila);
    }

    private static string TextoDe(EstadoPresupuesto estado) => estado switch
    {
        EstadoPresupuesto.Borrador => EstadoBorrador,
        EstadoPresupuesto.Final => EstadoFinal,
        _ => throw new ArgumentException("El estado del presupuesto no es válido.", nameof(estado)),
    };

    /// <summary>
    /// La entidad de una fila. Una fila que eludió los CHECK o que el dominio guardaría de otra forma (un estado
    /// desconocido, un DNI o un texto que se normalizarían a otro valor, una cantidad que no entra en un <c>int</c>, un
    /// presupuesto sin líneas) no puede representarse: se informa como estado corrupto y no con el
    /// <see cref="ArgumentException"/> del dominio, que significa entrada inválida. El mensaje no lleva valores; la causa
    /// del dominio nombra el campo y no el valor, así que se puede adjuntar.
    /// </summary>
    private static Presupuesto AEntidad(PresupuestoFila fila)
    {
        var estado = fila.Estado switch
        {
            EstadoBorrador => EstadoPresupuesto.Borrador,
            EstadoFinal => EstadoPresupuesto.Final,
            _ => throw new InvalidOperationException(MensajeFilaInvalida),
        };

        Presupuesto presupuesto;
        try
        {
            var cliente = new ClientePresupuesto(
                fila.Apellido, fila.Nombre, fila.Dni, fila.Domicilio, fila.Email, fila.Telefono);
            if (!ComoSeGuarda(cliente, fila))
            {
                throw new InvalidOperationException(MensajeFilaInvalida);
            }

            presupuesto = new Presupuesto(
                fila.Numero, fila.Fecha, estado, cliente, [.. fila.Lineas.Select(ALinea)]);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }

        return presupuesto;
    }

    private static LineaPresupuesto ALinea(LineaPresupuestoFila fila)
    {
        if (fila.Cantidad is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        return new LineaPresupuesto(
            fila.CodigoArticulo, fila.Descripcion, fila.PrecioUnitario, (int)fila.Cantidad, fila.PorcentajeDescuento,
            fila.PrecioConDescuento, fila.PrecioFinal);
    }

    // El cliente normalizado tiene que ser exactamente el guardado: si no, al volver a grabarlo cambiaría.
    private static bool ComoSeGuarda(ClientePresupuesto cliente, PresupuestoFila fila) =>
        string.Equals(cliente.Apellido, fila.Apellido, StringComparison.Ordinal)
        && string.Equals(cliente.Nombre, fila.Nombre, StringComparison.Ordinal)
        && string.Equals(cliente.Dni, fila.Dni, StringComparison.Ordinal)
        && string.Equals(cliente.Domicilio, fila.Domicilio, StringComparison.Ordinal)
        && string.Equals(cliente.Email, fila.Email, StringComparison.Ordinal)
        && string.Equals(cliente.Telefono, fila.Telefono, StringComparison.Ordinal);
}
