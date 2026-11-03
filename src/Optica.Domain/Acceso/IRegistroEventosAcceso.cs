namespace Optica.Domain.Acceso;

/// <summary>
/// Registro de eventos de acceso. <see cref="Registrar"/> solo registra el cambio: lo confirma la unidad de
/// trabajo.
/// </summary>
public interface IRegistroEventosAcceso
{
    void Registrar(EventoAcceso evento);

    /// <summary>
    /// Las fechas UTC de los eventos <c>ingreso/rechazo</c> confirmados después del último evento de
    /// <see cref="PoliticaBloqueo.EventosQueReinician"/>, en orden de registro (no de fecha). Solo consulta: la regla del
    /// bloqueo es <see cref="PoliticaBloqueo.Evaluar"/>. Un evento guardado con un texto que esta versión no conoce no
    /// cuenta, no reinicia y no hace fallar la consulta.
    /// </summary>
    Task<IReadOnlyList<DateTime>> ObtenerRechazosIngresoDesdeUltimoReinicioAsync(CancellationToken cancelacion = default);
}
