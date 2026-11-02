namespace Optica.Application.Comun;

/// <summary>
/// Dónde viven los datos locales de la aplicación y cómo describir una falla de la base sin datos personales. Lo usa
/// la Api (claves de la sesión y registro de errores) sin conocer la base ni su tecnología.
/// </summary>
public interface IDatosLocales
{
    /// <summary>Ruta absoluta del directorio de datos (<c>Optica:Datos:Directorio</c>), fuera del árbol del código.</summary>
    string Directorio { get; }

    /// <summary>
    /// Si el error, o alguna de sus causas, es una falla de la base, la describe para el log sin el texto original
    /// (que puede traer la ruta con el nombre de usuario de la PC); si no, devuelve null.
    /// </summary>
    FallaDatos? DescribirFalla(Exception error);
}

/// <summary>
/// Una falla de la base contada sin datos personales: el archivo, el directorio como se muestra en el log, el tipo de
/// error, el código numérico de error del motor de la base (null si no lo informa) y la clave de configuración a
/// revisar.
/// </summary>
public sealed record FallaDatos(string Archivo, string Directorio, string TipoError, int? CodigoError, string Clave);
