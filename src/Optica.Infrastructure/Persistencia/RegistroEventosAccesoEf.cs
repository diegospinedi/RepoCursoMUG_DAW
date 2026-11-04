using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Optica.Domain.Acceso;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Agrega filas a la tabla <c>EventoAcceso</c> y consulta los rechazos que usa el bloqueo. Los textos de <c>Tipo</c> y
/// <c>Resultado</c> se fijan acá (la base no los restringe). <see cref="Registrar"/> solo deja el cambio en el contexto
/// compartido, que confirma <see cref="UnidadDeTrabajoEf"/>.
/// </summary>
public sealed class RegistroEventosAccesoEf(OpticaDbContext contexto) : IRegistroEventosAcceso
{
    /// <summary><c>Tipo = 'ingreso' AND Resultado = 'rechazo'</c>, con el texto de <see cref="PoliticaBloqueo.EventoQueCuenta"/>.</summary>
    private static readonly Expression<Func<EventoAccesoFila, bool>> EsEventoQueCuenta =
        ArmarCoincideCon([PoliticaBloqueo.EventoQueCuenta]);

    /// <summary>
    /// <c>(Tipo = 'ingreso' AND Resultado = 'exito') OR ...</c> con los textos de
    /// <see cref="PoliticaBloqueo.EventosQueReinician"/>.
    /// </summary>
    private static readonly Expression<Func<EventoAccesoFila, bool>> EsReinicio =
        ArmarCoincideCon(PoliticaBloqueo.EventosQueReinician);

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

    /// <summary>
    /// Una sola consulta: los <c>ingreso/rechazo</c> con <c>Id</c> mayor que el del último reinicio, ordenados por
    /// <c>Id</c>. Solo se leen las fechas de esas filas; las de textos desconocidos nunca se materializan.
    /// </summary>
    public async Task<IReadOnlyList<DateTime>> ObtenerRechazosIngresoDesdeUltimoReinicioAsync(
        CancellationToken cancelacion = default) =>
        await ConsultaRechazosIngresoDesdeUltimoReinicio().ToListAsync(cancelacion);

    /// <summary>
    /// La consulta de <see cref="ObtenerRechazosIngresoDesdeUltimoReinicioAsync"/>, sin ejecutar. Expuesta a las pruebas
    /// para revisar su plan (<c>EXPLAIN QUERY PLAN</c>): los textos van como constantes, así el SQL no lleva parámetros.
    /// </summary>
    internal IQueryable<DateTime> ConsultaRechazosIngresoDesdeUltimoReinicio()
    {
        var reinicios = contexto.EventosAcceso.Where(EsReinicio);
        return contexto.EventosAcceso
            .AsNoTracking()
            .Where(EsEventoQueCuenta)
            .Where(e => e.Id > (reinicios.Max(r => (long?)r.Id) ?? 0))
            .OrderBy(e => e.Id)
            .Select(e => e.FechaHora);
    }

    internal static string TextoDe(TipoEventoAcceso tipo) => tipo switch
    {
        TipoEventoAcceso.Definicion => "definicion",
        TipoEventoAcceso.Ingreso => "ingreso",
        TipoEventoAcceso.Salida => "salida",
        TipoEventoAcceso.CambioContrasena => "cambio_contrasena",
        TipoEventoAcceso.Restablecimiento => "restablecimiento",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    internal static string TextoDe(ResultadoEventoAcceso resultado) => resultado switch
    {
        ResultadoEventoAcceso.Exito => "exito",
        ResultadoEventoAcceso.Rechazo => "rechazo",
        ResultadoEventoAcceso.Bloqueado => "bloqueado",
        _ => throw new ArgumentOutOfRangeException(nameof(resultado)),
    };

    /// <summary>
    /// Comparaciones exactas (distinguen mayúsculas) contra los textos conocidos de los eventos dados, unidas con
    /// <c>OR</c>: usan el índice <c>IX_EventoAcceso_Tipo_Resultado</c> y dejan afuera cualquier texto desconocido.
    /// </summary>
    private static Expression<Func<EventoAccesoFila, bool>> ArmarCoincideCon(
        IEnumerable<(TipoEventoAcceso Tipo, ResultadoEventoAcceso Resultado)> eventos)
    {
        var fila = Expression.Parameter(typeof(EventoAccesoFila), "e");
        var tipo = Expression.Property(fila, nameof(EventoAccesoFila.Tipo));
        var resultado = Expression.Property(fila, nameof(EventoAccesoFila.Resultado));
        var cuerpo = eventos
            .Select(evento => (Expression)Expression.AndAlso(
                Expression.Equal(tipo, Expression.Constant(TextoDe(evento.Tipo))),
                Expression.Equal(resultado, Expression.Constant(TextoDe(evento.Resultado)))))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<EventoAccesoFila, bool>>(cuerpo, fila);
    }
}
