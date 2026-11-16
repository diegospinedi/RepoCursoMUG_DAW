using System.Globalization;
using Microsoft.Data.Sqlite;
using Optica.Domain.Catalogo;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Funciones escalares que la aplicación registra en cada conexión SQLite (ADR-011) y que EF Core traduce en las
/// consultas (<see cref="OpticaDbContext"/>). <c>plegar</c> y <c>orden_articulo</c> delegan en
/// <see cref="PlegadoBusqueda"/>, la única regla de plegado; <c>digitos_comprobante</c> (FEAT-009, D-27) da los dígitos
/// del comprobante visible para la búsqueda de facturas. Existen solo en las conexiones de la aplicación: ningún objeto del esquema (índice, CHECK, trigger, vista)
/// puede usarlas, o la base dejaría de abrirse desde <c>sqlite3</c> o desde un backup (RNF-02).
/// </summary>
internal static class FuncionesSqlite
{
    /// <summary>El nombre SQL de <see cref="Plegar"/>.</summary>
    public const string NombrePlegar = "plegar";

    /// <summary>El nombre SQL de <see cref="OrdenArticulo"/>.</summary>
    public const string NombreOrdenArticulo = "orden_articulo";

    /// <summary>El nombre SQL de <see cref="DigitosComprobante"/>.</summary>
    public const string NombreDigitosComprobante = "digitos_comprobante";

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
    /// <c>digitos_comprobante(puntoVenta, numero)</c>: <see cref="Digitos"/> (D-27). Solo se traduce a SQL dentro de una
    /// consulta.
    /// </summary>
    /// <exception cref="InvalidOperationException">Se evaluó en el cliente en lugar de traducirse.</exception>
    public static string DigitosComprobante(long puntoVenta, long numero) =>
        throw SoloEnConsultas(NombreDigitosComprobante);

    /// <summary>
    /// Los dígitos del comprobante como se ve, sin el guion: el punto de venta con al menos 4 dígitos (5 si es 10000 o
    /// más) y el número con 8, como "0003-00000125" en <c>NumeroComprobante.ToString</c> (D-27). 3 y 125 dan
    /// "000300000125"; 12345 y 7 dan "1234500000007".
    /// </summary>
    internal static string Digitos(long puntoVenta, long numero) =>
        string.Create(CultureInfo.InvariantCulture, $"{puntoVenta:D4}{numero:D8}");

    /// <summary>
    /// Registra las tres funciones en la conexión, deterministas: un nulo devuelve nulo, un texto nunca lanza, porque
    /// <see cref="PlegadoBusqueda"/> es total (ADR-011), y dos enteros nunca lanzan.
    /// </summary>
    public static void Registrar(SqliteConnection conexion)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        conexion.CreateFunction<string?, string?>(
            NombrePlegar, texto => texto is null ? null : PlegadoBusqueda.Plegar(texto), isDeterministic: true);
        conexion.CreateFunction<string?, string?>(
            NombreOrdenArticulo, texto => texto is null ? null : PlegadoBusqueda.ClaveOrden(texto), isDeterministic: true);
        conexion.CreateFunction<long?, long?, string?>(
            NombreDigitosComprobante,
            (puntoVenta, numero) => puntoVenta is { } pv && numero is { } n ? Digitos(pv, n) : null,
            isDeterministic: true);
    }

    private static InvalidOperationException SoloEnConsultas(string funcion) =>
        new($"La función '{funcion}' solo se puede usar en consultas de EF Core contra SQLite.");
}
