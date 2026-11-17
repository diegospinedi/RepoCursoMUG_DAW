namespace Optica.Application.Backup;

/// <summary>
/// Un backup a la vez en el proceso (D-22 de FEAT-010): el programador y el arranque podrían pedirlo juntos, y dos copias
/// simultáneas escribirían el mismo temporal. La aplicación es de instancia única (candado de FEAT-001b), así que alcanza
/// con un candado en memoria; la implementación vive en Infrastructure y es singleton.
/// </summary>
public interface ICandadoBackup
{
    /// <summary>
    /// Toma el turno sin esperar. Devuelve lo que hay que descartar para liberarlo, o null si otro backup lo tiene: el
    /// segundo pedido no hace nada. Descartar el turno más de una vez no libera el de otro.
    /// </summary>
    IDisposable? IntentarTomar();
}
