using Optica.Domain.Acceso;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Agrega filas a la tabla <c>EventoAcceso</c>. Los textos de <c>Tipo</c> y <c>Resultado</c> se fijan acá (la
/// base no los restringe). Solo deja el cambio en el contexto compartido, que confirma
/// <see cref="UnidadDeTrabajoEf"/>.
/// </summary>
public sealed class RegistroEventosAccesoEf(OpticaDbContext contexto) : IRegistroEventosAcceso
{
    public void Registrar(EventoAcceso evento)
    {
        ArgumentNullException.ThrowIfNull(evento);
        contexto.EventosAcceso.Add(new EventoAccesoFila
        {
            FechaHora = evento.FechaHora,
            Tipo = TextoDe(evento.Tipo),
            Resultado = TextoDe(evento.Resultado),
        });
    }

    internal static string TextoDe(TipoEventoAcceso tipo) => tipo switch
    {
        TipoEventoAcceso.Definicion => "definicion",
        TipoEventoAcceso.Ingreso => "ingreso",
        TipoEventoAcceso.Salida => "salida",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    internal static string TextoDe(ResultadoEventoAcceso resultado) => resultado switch
    {
        ResultadoEventoAcceso.Exito => "exito",
        ResultadoEventoAcceso.Rechazo => "rechazo",
        _ => throw new ArgumentOutOfRangeException(nameof(resultado)),
    };
}
