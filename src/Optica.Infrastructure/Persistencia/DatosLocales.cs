using System.Data.Common;
using Microsoft.Data.Sqlite;
using Optica.Application.Comun;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Implementa <see cref="IDatosLocales"/> sobre la <see cref="FabricaConexion"/>: el directorio de datos y la
/// descripción de las fallas de la base con <see cref="FabricaConexion.RutaVisible"/>, nunca con el texto original de
/// la excepción (puede traer la ruta con el nombre de usuario de la PC).
/// </summary>
public sealed class DatosLocales(FabricaConexion fabrica) : IDatosLocales
{
    public string Directorio => fabrica.Directorio;

    public FallaDatos? DescribirFalla(Exception error)
    {
        for (Exception? actual = error; actual is not null; actual = actual.InnerException)
        {
            if (actual is DbException)
            {
                return new FallaDatos(
                    FabricaConexion.NombreArchivo,
                    fabrica.RutaVisible,
                    actual.GetType().Name,
                    // El código numérico de SQLite no lleva datos personales; el texto de la excepción, sí puede.
                    actual is SqliteException sqlite ? sqlite.SqliteErrorCode : null,
                    FabricaConexion.ClaveDirectorio);
            }
        }

        return null;
    }
}
