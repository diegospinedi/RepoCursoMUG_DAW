using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// La Api no arranca: falta <c>optica.db</c> y la carpeta de datos tiene una restauración sin terminar (el temporal
/// <see cref="RestauradorSqlite.NombreTemporal"/> o la carpeta <see cref="RestauradorSqlite.CarpetaAnterior"/>). Crear una
/// base vacía en ese momento escondería la base real, que está en el temporal o en la carpeta anterior (WARN-3 del Block 7
/// de FEAT-010). El mensaje no lleva rutas y remite a la guía.
/// </summary>
public sealed class RestauracionSinTerminarException : Exception
{
    /// <summary>El mensaje fijo, sin rutas.</summary>
    public const string Texto =
        "Falta la base " + FabricaConexion.NombreArchivo + " y la carpeta de datos tiene una restauración sin terminar " +
        "(el archivo " + RestauradorSqlite.NombreTemporal + " o la carpeta " + RestauradorSqlite.CarpetaAnterior + "). No " +
        "se creó una base nueva: seguí docs/instalacion.md, sección 10, para volver la base a su lugar.";

    public RestauracionSinTerminarException()
        : base(Texto)
    {
    }

    /// <summary>
    /// Lanza si en <paramref name="directorio"/> falta <c>optica.db</c> y hay un temporal de restauración o una carpeta
    /// anterior. Solo mira: no crea ni borra nada.
    /// </summary>
    /// <exception cref="RestauracionSinTerminarException">Hay una restauración sin terminar.</exception>
    public static void Comprobar(string directorio)
    {
        if (File.Exists(Path.Combine(directorio, FabricaConexion.NombreArchivo)))
        {
            return;
        }

        if (File.Exists(Path.Combine(directorio, RestauradorSqlite.NombreTemporal))
            || Directory.Exists(Path.Combine(directorio, RestauradorSqlite.CarpetaAnterior)))
        {
            throw new RestauracionSinTerminarException();
        }
    }
}
