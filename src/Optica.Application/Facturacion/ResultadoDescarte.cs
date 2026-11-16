namespace Optica.Application.Facturacion;

/// <summary>Cómo terminó el descarte de una emisión pendiente (FR-09, FR-11, spec FEAT-008, Block 5).</summary>
public enum ResultadoDescarte
{
    /// <summary>El descarte quedó registrado y la pendiente se quitó: el presupuesto se puede volver a facturar.</summary>
    Descartada,

    /// <summary>No hay presupuesto con ese número.</summary>
    NoEncontrado,

    /// <summary>El presupuesto no tiene emisión pendiente.</summary>
    SinPendiente,

    /// <summary>
    /// La pendiente no tiene una observación vigente, o el estado actual ya no sostiene su motivo (FR-11, D-12): no se
    /// descarta.
    /// </summary>
    SinObservacion,
}
