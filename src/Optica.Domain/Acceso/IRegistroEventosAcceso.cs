namespace Optica.Domain.Acceso;

/// <summary>
/// Registro de eventos de acceso. <see cref="Registrar"/> solo registra el cambio: lo confirma la unidad de
/// trabajo.
/// </summary>
public interface IRegistroEventosAcceso
{
    void Registrar(EventoAcceso evento);
}
