namespace Optica.Application.Comun;

/// <summary>
/// Dónde viven los datos locales de la aplicación y cómo describir una falla de la base sin datos personales. Lo usa
/// la Api (claves de la sesión, registro de errores y el comando de restablecimiento) sin conocer la base ni su
/// tecnología.
/// </summary>
public interface IDatosLocales
{
    /// <summary>Ruta absoluta del directorio de datos (<c>Optica:Datos:Directorio</c>), fuera del árbol del código.</summary>
    string Directorio { get; }

    /// <summary>
    /// La carpeta de las claves de sesión (Data Protection), dentro de <see cref="Directorio"/>: la única fuente de esa
    /// ruta, para la Api y para <see cref="RenovarClaves"/>.
    /// </summary>
    string DirectorioClaves { get; }

    /// <summary>
    /// <see cref="Directorio"/> como se muestra en mensajes y en el log, sin el nombre de usuario de la PC.
    /// </summary>
    string RutaVisible { get; }

    /// <summary>
    /// Verdadero si existen el directorio de datos y la base. Solo mira: no crea el directorio, la base ni sus
    /// archivos <c>-wal</c> y <c>-shm</c>.
    /// </summary>
    bool BaseExiste { get; }

    /// <summary>
    /// Verdadero si la base existente no tiene aplicadas todas las migraciones de esta versión (o no tiene historial de
    /// migraciones). Solo lee: no crea la base ni migra.
    /// </summary>
    /// <exception cref="Exception">La base no se pudo abrir o leer (por ejemplo, no existe o está dañada).</exception>
    bool MigracionesPendientes();

    /// <summary>
    /// Vacía <see cref="DirectorioClaves"/>: toda cookie de sesión emitida con esas claves deja de valer, y la Api crea
    /// claves nuevas al arrancar. Solo vale con la Api detenida (guarda las claves en memoria). Sin la carpeta, no hace
    /// nada.
    /// </summary>
    /// <exception cref="IOException">No se pudo borrar alguna clave.</exception>
    /// <exception cref="UnauthorizedAccessException">Sin permiso para borrar alguna clave.</exception>
    void RenovarClaves();

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
