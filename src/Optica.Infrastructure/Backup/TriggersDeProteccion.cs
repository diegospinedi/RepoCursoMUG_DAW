using System.Text;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// Un trigger de protección: su nombre en <c>sqlite_master</c> y el texto con que lo crea su migración, sin el punto y
/// coma final (lo que SQLite guarda en la columna <c>sql</c>).
/// </summary>
internal sealed record TriggerProteccion(string Nombre, string Sql);

/// <summary>
/// Los triggers de protección (registros cerrados y tablas de solo agregar) que crea cada migración, con su nombre y su
/// texto (WARN-2 de la auditoría del Block 7 de FEAT-010, H-01 del SAST). Una copia a restaurar tiene que tener todos los
/// de las migraciones de su historial, cada uno con el texto de esta tabla: sin uno de ellos, o con uno vaciado que
/// conserva el nombre, la base restaurada dejaría modificar un presupuesto en Final o una factura con CAE (AGENTS.md).
/// </summary>
/// <remarks>
/// <para>
/// Por qué una tabla escrita a mano y no algo derivado del código de las migraciones: los triggers se crean con SQL
/// crudo dentro de <c>Up</c>, y leerlo (o ejecutar las migraciones en una base de prueba en cada restauración) sería más
/// frágil y más lento que una lista. La lista no se puede desactualizar en silencio: la prueba
/// <c>TriggersDeProteccion_CadaMigracionCreaExactamenteLosDeLaTabla</c> migra una base nueva de a una migración y exige,
/// después de cada una, que los triggers de la base sean exactamente los de esta tabla para las migraciones aplicadas, con
/// el mismo texto normalizado (<see cref="Normalizar"/>). Una migración nueva que crea, borra o cambia un trigger, o que
/// lo pierde al reconstruir una tabla, hace fallar esa prueba hasta que se actualice acá.
/// </para>
/// <para>
/// Hoy ninguna migración borra ni cambia un trigger de una anterior: cada una solo crea los suyos en <c>Up</c> (y los
/// borra en su propio <c>Down</c>). Si alguna vez una lo hiciera, la prueba lo detecta y esta tabla tendría que pasar a
/// describir los triggers vigentes después de cada migración.
/// </para>
/// </remarks>
internal static class TriggersDeProteccion
{
    private const string Aborta = "BEGIN SELECT RAISE(ABORT, 'registro_cerrado'); END";

    /// <summary>Los triggers que crea cada migración; una migración que no crea triggers no figura.</summary>
    public static readonly IReadOnlyDictionary<string, TriggerProteccion[]> PorMigracion = new Dictionary<string, TriggerProteccion[]>(StringComparer.Ordinal)
    {
        ["20261111220704_CierrePresupuestos"] =
        [
            new(
                "TR_Presupuesto_FinalInmutable",
                "CREATE TRIGGER \"TR_Presupuesto_FinalInmutable\" BEFORE UPDATE ON \"Presupuesto\" " +
                "FOR EACH ROW WHEN OLD.\"Estado\" = 'final' " + Aborta),
            new(
                "TR_Presupuesto_FinalNoSeBorra",
                "CREATE TRIGGER \"TR_Presupuesto_FinalNoSeBorra\" BEFORE DELETE ON \"Presupuesto\" " +
                "FOR EACH ROW WHEN OLD.\"Estado\" = 'final' " + Aborta),
            new(
                "TR_Presupuesto_FechaYNumeroFijos",
                "CREATE TRIGGER \"TR_Presupuesto_FechaYNumeroFijos\" BEFORE UPDATE ON \"Presupuesto\" " +
                "FOR EACH ROW WHEN NEW.\"Fecha\" IS NOT OLD.\"Fecha\" OR NEW.\"Numero\" IS NOT OLD.\"Numero\" " +
                "BEGIN SELECT RAISE(ABORT, 'presupuesto_fecha_numero'); END"),
            new(
                "TR_LineaPresupuesto_FinalInsert",
                "CREATE TRIGGER \"TR_LineaPresupuesto_FinalInsert\" BEFORE INSERT ON \"LineaPresupuesto\" " +
                "FOR EACH ROW WHEN EXISTS (SELECT 1 FROM \"Presupuesto\" " +
                "WHERE \"Numero\" = NEW.\"PresupuestoNumero\" AND \"Estado\" = 'final') " + Aborta),
            new(
                "TR_LineaPresupuesto_FinalUpdate",
                "CREATE TRIGGER \"TR_LineaPresupuesto_FinalUpdate\" BEFORE UPDATE ON \"LineaPresupuesto\" " +
                "FOR EACH ROW WHEN EXISTS (SELECT 1 FROM \"Presupuesto\" " +
                "WHERE \"Numero\" IN (OLD.\"PresupuestoNumero\", NEW.\"PresupuestoNumero\") AND \"Estado\" = 'final') " +
                Aborta),
            new(
                "TR_LineaPresupuesto_FinalDelete",
                "CREATE TRIGGER \"TR_LineaPresupuesto_FinalDelete\" BEFORE DELETE ON \"LineaPresupuesto\" " +
                "FOR EACH ROW WHEN EXISTS (SELECT 1 FROM \"Presupuesto\" " +
                "WHERE \"Numero\" = OLD.\"PresupuestoNumero\" AND \"Estado\" = 'final') " + Aborta),
        ],
        ["20261113170512_Facturas"] =
        [
            new(
                "TR_Factura_Inmutable",
                "CREATE TRIGGER \"TR_Factura_Inmutable\" BEFORE UPDATE ON \"Factura\" FOR EACH ROW " + Aborta),
            new(
                "TR_Factura_NoSeBorra",
                "CREATE TRIGGER \"TR_Factura_NoSeBorra\" BEFORE DELETE ON \"Factura\" FOR EACH ROW " + Aborta),
            new(
                "TR_LineaFactura_Inmutable",
                "CREATE TRIGGER \"TR_LineaFactura_Inmutable\" BEFORE UPDATE ON \"LineaFactura\" FOR EACH ROW " + Aborta),
            new(
                "TR_LineaFactura_NoSeBorra",
                "CREATE TRIGGER \"TR_LineaFactura_NoSeBorra\" BEFORE DELETE ON \"LineaFactura\" FOR EACH ROW " + Aborta),
            new(
                "TR_LineaFactura_Cerrada",
                "CREATE TRIGGER \"TR_LineaFactura_Cerrada\" BEFORE INSERT ON \"LineaFactura\" " +
                "FOR EACH ROW WHEN (SELECT count(*) FROM \"LineaFactura\" WHERE \"FacturaId\" = NEW.\"FacturaId\") >= " +
                "(SELECT \"CantidadLineas\" FROM \"Factura\" WHERE \"Id\" = NEW.\"FacturaId\") " + Aborta),
            new(
                "TR_EmisionPendiente_Inmutable",
                "CREATE TRIGGER \"TR_EmisionPendiente_Inmutable\" BEFORE UPDATE ON \"EmisionPendiente\" FOR EACH ROW " +
                Aborta),
        ],
        ["20261114120000_RecuperacionEmisiones"] =
        [
            new(
                "TR_ObservacionEmision_Inmutable",
                "CREATE TRIGGER \"TR_ObservacionEmision_Inmutable\" BEFORE UPDATE ON \"ObservacionEmision\" FOR EACH ROW " +
                Aborta),
            new(
                "TR_DescarteEmision_SinModificar",
                "CREATE TRIGGER \"TR_DescarteEmision_SinModificar\" BEFORE UPDATE ON \"DescarteEmision\" FOR EACH ROW " +
                Aborta),
            new(
                "TR_DescarteEmision_SinBorrar",
                "CREATE TRIGGER \"TR_DescarteEmision_SinBorrar\" BEFORE DELETE ON \"DescarteEmision\" FOR EACH ROW " +
                Aborta),
        ],
        ["20261116200000_Backup"] =
        [
            new(
                "TR_BackupRealizado_Inmutable",
                "CREATE TRIGGER \"TR_BackupRealizado_Inmutable\" BEFORE UPDATE ON \"BackupRealizado\" FOR EACH ROW " +
                Aborta),
            new(
                "TR_BackupRealizado_NoSeBorra",
                "CREATE TRIGGER \"TR_BackupRealizado_NoSeBorra\" BEFORE DELETE ON \"BackupRealizado\" FOR EACH ROW " +
                Aborta),
        ],
    };

    /// <summary>Los triggers que tiene que tener una base con las <paramref name="migraciones"/> aplicadas.</summary>
    public static IEnumerable<TriggerProteccion> Esperados(IEnumerable<string> migraciones) =>
        migraciones.SelectMany(migracion => PorMigracion.TryGetValue(migracion, out var triggers) ? triggers : []);

    /// <summary>
    /// El texto de un trigger sin depender del formato: cada tramo de espacios, tabulaciones o saltos de línea queda en un
    /// espacio, sin espacios al principio ni al final ni un punto y coma final. No cambia mayúsculas ni nada más: otro
    /// cuerpo, otra condición u otra tabla siguen siendo otro texto.
    /// </summary>
    public static string Normalizar(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var resultado = new StringBuilder(sql.Length);
        var enEspacio = false;
        foreach (var caracter in sql)
        {
            if (char.IsWhiteSpace(caracter))
            {
                enEspacio = true;
                continue;
            }

            if (enEspacio && resultado.Length > 0)
            {
                resultado.Append(' ');
            }

            enEspacio = false;
            resultado.Append(caracter);
        }

        var texto = resultado.ToString();
        return texto.EndsWith(';') ? texto[..^1].TrimEnd() : texto;
    }
}
