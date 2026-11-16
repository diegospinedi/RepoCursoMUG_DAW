namespace Optica.Domain.Facturacion;

/// <summary>
/// Persistencia de las observaciones de verificación (ADR-026): una como máximo por presupuesto, y solo si tiene una
/// emisión pendiente. Se agregan y se quitan, no se modifican (D-12). <see cref="Agregar"/> y
/// <see cref="QuitarObservacion"/> solo registran el cambio: lo confirma la unidad de trabajo.
/// </summary>
/// <remarks>
/// Los nombres de <see cref="QuitarObservacion"/> y <see cref="ObtenerObservacionAsync"/> son distintos de los de
/// <see cref="IEmisionPendienteRepository"/> a propósito: un mismo doble de prueba puede implementar los dos.
/// </remarks>
public interface IObservacionEmisionRepository
{
    /// <summary>
    /// Registra la observación. Si el presupuesto ya tiene una, la confirmación falla por unicidad; si no tiene emisión
    /// pendiente, por la clave foránea. Si la del mismo presupuesto se quitó con <see cref="QuitarObservacion"/> y la baja
    /// no se confirmó, lanza <see cref="InvalidOperationException"/> sin registrar nada: reemplazarla exige confirmar la
    /// baja en el medio.
    /// </summary>
    void Agregar(ObservacionEmision observacion);

    /// <summary>
    /// Registra la baja de la observación del presupuesto, si tiene; si no, no hace nada. Hasta que la baja se confirme no
    /// se puede <see cref="Agregar"/> otra para el mismo presupuesto.
    /// </summary>
    void QuitarObservacion(long presupuestoNumero);

    /// <summary>La observación vigente del presupuesto, o null si no tiene.</summary>
    /// <exception cref="InvalidOperationException">La fila guardada no es válida. El mensaje es fijo y no lleva datos.</exception>
    Task<ObservacionEmision?> ObtenerObservacionAsync(long presupuestoNumero, CancellationToken cancelacion = default);
}
