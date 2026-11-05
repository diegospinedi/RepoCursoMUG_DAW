using Microsoft.Data.Sqlite;
using Optica.Domain.Catalogo;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Funciones escalares que la aplicación registra en cada conexión SQLite (ADR-011) y que EF Core traduce en las
/// consultas (<see cref="OpticaDbContext"/>). Las dos delegan en <see cref="PlegadoBusqueda"/>, la única regla de
/// plegado. Existen solo en las conexiones de la aplicación: ningún objeto del esquema (índice, CHECK, trigger, vista)
/// puede usarlas, o la base dejaría de abrirse desde <c>sqlite3</c> o desde un backup (RNF-02).
/// </summary>
internal static class FuncionesSqlite
{
    /// <summary>El nombre SQL de <see cref="Plegar"/>.</summary>
    public const string NombrePlegar = "plegar";

    /// <summary>El nombre SQL de <see cref="OrdenArticulo"/>.</summary>
    public const string NombreOrdenArticulo = "orden_articulo";

    /// <summary>
    /// <c>plegar(texto)</c>: <see cref="PlegadoBusqueda.Plegar"/> (D-04). Solo se traduce a SQL dentro de una consulta.
    /// </summary>
    /// <exception cref="InvalidOperationException">Se evaluó en el cliente en lugar de traducirse.</exception>
    public static string Plegar(string texto) => throw SoloEnConsultas(NombrePlegar);

    /// <summary>
    /// <c>orden_articulo(texto)</c>: <see cref="PlegadoBusqueda.ClaveOrden"/> (D-15). Solo se traduce a SQL dentro de una
    /// consulta.
    /// </summary>
    /// <exception cref="InvalidOperationException">Se evaluó en el cliente en lugar de traducirse.</exception>
    public static string OrdenArticulo(string texto) => throw SoloEnConsultas(NombreOrdenArticulo);

    /// <summary>
    /// Registra las dos funciones en la conexión, deterministas: un nulo devuelve nulo y un texto nunca lanza, porque
    /// <see cref="PlegadoBusqueda"/> es total (ADR-011).
    /// </summary>
    public static void Registrar(SqliteConnection conexion)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        conexion.CreateFunction<string?, string?>(
            NombrePlegar, texto => texto is null ? null : PlegadoBusqueda.Plegar(texto), isDeterministic: true);
        conexion.CreateFunction<string?, string?>(
            NombreOrdenArticulo, texto => texto is null ? null : PlegadoBusqueda.ClaveOrden(texto), isDeterministic: true);
    }

    private static InvalidOperationException SoloEnConsultas(string funcion) =>
        new($"La función '{funcion}' solo se puede usar en consultas de EF Core contra SQLite.");
}
