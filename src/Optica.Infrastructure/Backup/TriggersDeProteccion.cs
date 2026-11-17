namespace Optica.Infrastructure.Backup;

/// <summary>
/// Los triggers de protección (registros cerrados y tablas de solo agregar) que crea cada migración, por su nombre en
/// <c>sqlite_master</c> (WARN-2 de la auditoría del Block 7 de FEAT-010). Una copia a restaurar tiene que tener todos los
/// de las migraciones de su historial: sin uno de ellos, la base restaurada dejaría modificar un presupuesto en Final o
/// una factura con CAE (AGENTS.md).
/// </summary>
/// <remarks>
/// <para>
/// Por qué una tabla escrita a mano y no algo derivado del código de las migraciones: los triggers se crean con SQL
/// crudo dentro de <c>Up</c>, y leerlo (o ejecutar las migraciones en una base de prueba en cada restauración) sería más
/// frágil y más lento que una lista. La lista no se puede desactualizar en silencio: la prueba
/// <c>TriggersDeProteccion_CadaMigracionCreaExactamenteLosDeLaTabla</c> migra una base nueva de a una migración y exige,
/// después de cada una, que los triggers de la base sean exactamente los de esta tabla para las migraciones aplicadas. Una
/// migración nueva que crea o borra un trigger, o que lo pierde al reconstruir una tabla, hace fallar esa prueba hasta que
/// se agregue acá.
/// </para>
/// <para>
/// Hoy ninguna migración borra un trigger de una anterior: cada una solo crea los suyos en <c>Up</c> (y los borra en su
/// propio <c>Down</c>). Si alguna vez una lo hiciera, la prueba lo detecta y esta tabla tendría que pasar a describir los
/// triggers vigentes después de cada migración.
/// </para>
/// </remarks>
internal static class TriggersDeProteccion
{
    /// <summary>Los triggers que crea cada migración; una migración que no crea triggers no figura.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> PorMigracion = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["20261111220704_CierrePresupuestos"] =
        [
            "TR_Presupuesto_FinalInmutable",
            "TR_Presupuesto_FinalNoSeBorra",
            "TR_Presupuesto_FechaYNumeroFijos",
            "TR_LineaPresupuesto_FinalInsert",
            "TR_LineaPresupuesto_FinalUpdate",
            "TR_LineaPresupuesto_FinalDelete",
        ],
        ["20261113170512_Facturas"] =
        [
            "TR_Factura_Inmutable",
            "TR_Factura_NoSeBorra",
            "TR_LineaFactura_Inmutable",
            "TR_LineaFactura_NoSeBorra",
            "TR_LineaFactura_Cerrada",
            "TR_EmisionPendiente_Inmutable",
        ],
        ["20261114120000_RecuperacionEmisiones"] =
        [
            "TR_ObservacionEmision_Inmutable",
            "TR_DescarteEmision_SinModificar",
            "TR_DescarteEmision_SinBorrar",
        ],
        ["20261116200000_Backup"] =
        [
            "TR_BackupRealizado_Inmutable",
            "TR_BackupRealizado_NoSeBorra",
        ],
    };

    /// <summary>Los triggers que tiene que tener una base con las <paramref name="migraciones"/> aplicadas.</summary>
    public static IEnumerable<string> Esperados(IEnumerable<string> migraciones) =>
        migraciones.SelectMany(migracion => PorMigracion.TryGetValue(migracion, out var triggers) ? triggers : []);
}
