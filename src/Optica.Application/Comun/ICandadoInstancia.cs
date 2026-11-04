namespace Optica.Application.Comun;

/// <summary>
/// Candado de instancia única sobre el directorio de datos: lo toma la Api al arrancar y lo mantiene mientras corre, y
/// lo toma el comando de restablecimiento mientras dura. Mientras uno lo tiene, nadie más puede tomarlo (R-13): así el
/// comando no corre con la Api en marcha (que seguiría aceptando las cookies con las claves que tiene en memoria) y
/// dos Api no usan la misma base.
/// </summary>
public interface ICandadoInstancia
{
    /// <summary>
    /// Toma el candado y devuelve lo que hay que descartar para liberarlo, o null si otra instancia (de este proceso o
    /// de otro, incluso de otra cuenta) lo tiene. Se libera solo si el proceso termina.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">El directorio de datos no existe.</exception>
    /// <exception cref="Exception">Cualquier otra falla al abrir el candado; su texto puede traer la ruta expandida.</exception>
    IDisposable? IntentarAdquirir();
}
