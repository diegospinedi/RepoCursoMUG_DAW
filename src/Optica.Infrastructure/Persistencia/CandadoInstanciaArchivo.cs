using Optica.Application.Comun;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Candado de instancia única con el archivo <see cref="NombreArchivo"/> del directorio de datos, abierto con
/// <see cref="FileShare.None"/> mientras se lo tiene: lo que cuenta es el handle abierto, no que el archivo exista (queda
/// en el directorio, vacío, sin PID, usuario ni nombre de equipo). No depende del hilo, y el sistema lo libera si el
/// proceso se cae. En Windows es un bloqueo obligatorio del archivo; fuera de Windows .NET lo implementa con
/// <c>flock</c> (orientativo: solo lo respeta quien también lo pida), y en una unidad de Windows montada en WSL
/// (<c>/mnt/…</c>) no es confiable.
/// </summary>
public sealed class CandadoInstanciaArchivo(string directorio) : ICandadoInstancia
{
    public const string NombreArchivo = "optica.instancia";

    // ERROR_SHARING_VIOLATION y ERROR_LOCK_VIOLATION de Windows, y EWOULDBLOCK/EAGAIN de flock en Linux y macOS.
    private const int ViolacionUsoCompartido = unchecked((int)0x80070020);
    private const int ViolacionBloqueo = unchecked((int)0x80070021);
    private const int OcupadoLinux = 11;
    private const int OcupadoMacOs = 35;

    /// <summary>
    /// Abre (o crea, vacío) el archivo en exclusiva y devuelve su handle, que libera el candado al descartarlo. Si otra
    /// instancia lo tiene abierto (uso compartido) o no hay permiso sobre él (por ejemplo, lo tiene otra cuenta),
    /// devuelve null. <see cref="DirectoryNotFoundException"/> y cualquier otra falla se propagan: su texto trae la ruta
    /// expandida, así que quien llama informa solo el tipo.
    /// </summary>
    public IDisposable? IntentarAdquirir()
    {
        try
        {
            // Sin FileOptions.DeleteOnClose: borrar el archivo al cerrar dejaría una ventana en la que otro proceso abre
            // un archivo nuevo con el mismo nombre mientras el anterior todavía está tomado.
            return new FileStream(
                Path.Combine(directorio, NombreArchivo), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException error) when (EsUsoCompartido(error))
        {
            return null;
        }
    }

    /// <summary>Una <see cref="IOException"/> exacta (no un archivo o directorio inexistente) por uso compartido.</summary>
    private static bool EsUsoCompartido(IOException error) =>
        error.GetType() == typeof(IOException)
        && error.HResult is ViolacionUsoCompartido or ViolacionBloqueo or OcupadoLinux or OcupadoMacOs;
}

/// <summary>
/// El candado que tomó la Api al inicializar la base, guardado como singleton del contenedor: lo libera el
/// <c>Dispose</c> del host (y no lo recolecta el GC mientras la Api corre). Tomarlo dos veces con los mismos servicios
/// no hace nada.
/// </summary>
internal sealed class CandadoTomado(ICandadoInstancia candado) : IDisposable
{
    private readonly Lock _cerrojo = new();
    private IDisposable? _tomado;

    /// <exception cref="InstanciaEnMarchaException">Otra instancia tiene el candado.</exception>
    /// <exception cref="DirectorioDatosInvalidoException">
    /// No se pudo abrir el candado por otra falla; el mensaje lleva solo su tipo, sin la excepción como causa.
    /// </exception>
    public void Tomar(FabricaConexion fabrica)
    {
        lock (_cerrojo)
        {
            if (_tomado is not null)
            {
                return;
            }

            IDisposable? tomado;
            try
            {
                tomado = candado.IntentarAdquirir();
            }
            catch (Exception error)
            {
                // El texto de la excepción trae la ruta expandida: no se copia ni se adjunta.
                throw new DirectorioDatosInvalidoException(
                    fabrica.RutaVisible,
                    $"No se pudo tomar el candado de instancia '{CandadoInstanciaArchivo.NombreArchivo}' del directorio de " +
                    $"datos '{fabrica.RutaVisible}' ({error.GetType().Name}). Revisá los permisos de esa carpeta o la clave " +
                    $"de configuración '{FabricaConexion.ClaveDirectorio}'.");
            }

            _tomado = tomado ?? throw new InstanciaEnMarchaException(
                $"Otra instancia de la aplicación ya usa el directorio de datos '{fabrica.RutaVisible}', o no hay permisos " +
                $"sobre el archivo '{CandadoInstanciaArchivo.NombreArchivo}' de ese directorio.");
        }
    }

    public void Dispose()
    {
        lock (_cerrojo)
        {
            _tomado?.Dispose();
            _tomado = null;
        }
    }
}

/// <summary>
/// Otra instancia de la aplicación tiene tomado el candado del directorio de datos (o no hay permiso sobre él): la Api
/// no arranca. El mensaje nombra solo la ruta visible (sin el usuario de Windows) y nunca lleva una causa.
/// </summary>
public sealed class InstanciaEnMarchaException : Exception
{
    public InstanciaEnMarchaException(string mensaje)
        : base(mensaje)
    {
    }
}
