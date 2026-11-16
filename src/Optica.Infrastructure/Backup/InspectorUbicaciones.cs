using Optica.Application.Backup;

namespace Optica.Infrastructure.Backup;

/// <summary>
/// <see cref="IInspectorUbicaciones"/> sobre el sistema de archivos de la PC (FR-02, FR-03 de FEAT-010; D-07, D-17,
/// D-18). Toda ruta se normaliza con <see cref="Path.GetFullPath(string)"/> antes de compararla; las reglas puras
/// (<see cref="MismaUnidadEntre"/>, <see cref="DentroDeAlguna"/>, <see cref="SuperpuestasEntre"/>,
/// <see cref="EsUncODispositivo"/>, <see cref="EsRaizDeUnidad"/>) son estáticas y se prueban con listas de unidades falsas
/// de Windows y de Linux; la lista de unidades de la instancia también es inyectable.
/// </summary>
/// <remarks>
/// <para>
/// La unidad es la letra en Windows (como <see cref="Path.GetPathRoot(string)"/>, sin distinguir mayúsculas) y, sin
/// letras (Linux: pruebas y e2e), el punto de montaje de prefijo más largo de <see cref="DriveInfo.GetDrives"/>. Se
/// rechazan las rutas UNC, las de dispositivo (<c>\\?\</c>, <c>\\.\</c>), las unidades de red y, cerrado por defecto, las
/// que no están en la lista de unidades o son de tipo desconocido. Limitaciones
/// documentadas (D-17): una unidad creada con <c>subst</c>, un enlace simbólico o una unión de NTFS se toman por lo que
/// dice la ruta, no por el disco al que apuntan.
/// </para>
/// <para>
/// Ningún método lanza ni registra la ruta (D-12): ante un error de E/S responden el valor que rechaza.
/// </para>
/// </remarks>
public sealed class InspectorUbicaciones : IInspectorUbicaciones
{
    private const string PrefijoSonda = ".optica-sonda-";

    private readonly IFuenteCarpetasNube _fuente;
    private readonly Func<IReadOnlyList<UnidadDisco>> _unidades;

    /// <summary>Con las unidades de la PC (<see cref="DriveInfo.GetDrives"/>), leídas en cada llamada.</summary>
    public InspectorUbicaciones(IFuenteCarpetasNube fuente)
        : this(fuente, UnidadesDeLaPc)
    {
    }

    /// <param name="fuente">Las carpetas de nube conocidas.</param>
    /// <param name="unidades">Las unidades con su punto de montaje y su tipo; inyectable para las pruebas.</param>
    public InspectorUbicaciones(IFuenteCarpetasNube fuente, Func<IReadOnlyList<UnidadDisco>> unidades)
    {
        _fuente = fuente ?? throw new ArgumentNullException(nameof(fuente));
        _unidades = unidades ?? throw new ArgumentNullException(nameof(unidades));
    }

    /// <summary>
    /// <see cref="Path.GetFullPath(string)"/> sin separador final (<see cref="Path.TrimEndingDirectorySeparator(string)"/>,
    /// que conserva el de la raíz de una unidad); null si la ruta está en blanco o no se puede normalizar.
    /// </summary>
    public string? Normalizar(string ruta) =>
        Completa(ruta) is { } completa ? Path.TrimEndingDirectorySeparator(completa) : null;

    /// <summary>Normaliza las dos y aplica <see cref="SuperpuestasEntre"/>; si alguna no se puede normalizar, verdadero.</summary>
    public bool Superpuestas(string rutaA, string rutaB) =>
        Normalizar(rutaA) is not { } a || Normalizar(rutaB) is not { } b || SuperpuestasEntre(a, b);

    /// <summary>
    /// Verdadero si las dos rutas, ya normalizadas, son la misma carpeta o una está dentro de la otra, comparando por
    /// segmentos en los dos sentidos con las reglas de <see cref="DentroDeAlguna"/> (D-19).
    /// </summary>
    public static bool SuperpuestasEntre(string rutaA, string rutaB) => DentroDe(rutaA, rutaB) || DentroDe(rutaB, rutaA);

    /// <summary>
    /// Verdadero si la ruta es la raíz de una unidad (<c>C:\</c>, <c>C:</c>, <c>/</c>). Una carpeta de nube así se
    /// descarta: haría pasar por nube a toda la unidad.
    /// </summary>
    public static bool EsRaizDeUnidad(string ruta) =>
        !string.IsNullOrWhiteSpace(ruta) && Segmentos(ruta).Length == (Letra(ruta) is not null ? 1 : 0);

    /// <summary>
    /// Verdadero si la ruta empieza con dos separadores (<c>\\servidor\recurso</c>, <c>//servidor/recurso</c>,
    /// <c>\\?\</c>, <c>\\.\</c>): UNC o de dispositivo (D-17).
    /// </summary>
    public static bool EsUncODispositivo(string ruta) =>
        ruta is { Length: >= 2 } && EsSeparador(ruta[0]) && EsSeparador(ruta[1]);

    /// <summary>
    /// Verdadero si las dos rutas, ya normalizadas, están en la misma unidad: con letra, la misma letra sin distinguir
    /// mayúsculas (la lista no hace falta); sin letras, el mismo punto de montaje de prefijo más largo entre
    /// <paramref name="unidades"/>, por segmentos. Una con letra y otra sin letra están en unidades distintas. Si alguna
    /// no cae en ninguna unidad conocida, verdadero: el lado seguro de FR-02.
    /// </summary>
    public static bool MismaUnidadEntre(string rutaA, string rutaB, IEnumerable<string> unidades)
    {
        var lista = unidades as IReadOnlyCollection<string> ?? [.. unidades];
        var unidadA = UnidadDe(rutaA, lista);
        var unidadB = UnidadDe(rutaB, lista);
        if (unidadA is null || unidadB is null)
        {
            return true;
        }

        var comparacion = Letra(unidadA) is not null ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(unidadA, unidadB, comparacion);
    }

    /// <summary>
    /// Verdadero si la ruta, ya normalizada, es alguna de las carpetas o está dentro de ella, comparando segmento a
    /// segmento: con letra de unidad, separados por <c>\</c> o <c>/</c> y sin distinguir mayúsculas; sin letra, separados
    /// por <c>/</c> y distinguiendo mayúsculas. <c>/srv/nubeX</c> no está dentro de <c>/srv/nube</c>. Las carpetas vacías o
    /// en blanco se ignoran.
    /// </summary>
    public static bool DentroDeAlguna(string ruta, IEnumerable<string> carpetas) =>
        carpetas.Any(carpeta => !string.IsNullOrWhiteSpace(carpeta) && DentroDe(ruta, carpeta));

    public bool EsLocal(string ruta)
    {
        if (EsUncODispositivo(ruta) || Normalizar(ruta) is not { } normalizada || EsUncODispositivo(normalizada))
        {
            return false;
        }

        // Cerrado por defecto: sin la unidad en la lista, o con un tipo que no se conoce, no se puede evaluar.
        var unidades = Unidades();
        var unidad = UnidadDe(normalizada, [.. unidades.Select(u => u.Montaje)]);
        if (unidad is null)
        {
            return false;
        }

        var encontrada = unidades.FirstOrDefault(u => string.Equals(
            Path.TrimEndingDirectorySeparator(u.Montaje), Path.TrimEndingDirectorySeparator(unidad), Comparacion(unidad)));
        return encontrada is not null
            && encontrada.Tipo is not (DriveType.Network or DriveType.Unknown or DriveType.NoRootDirectory);
    }

    public bool MismaUnidad(string rutaA, string rutaB) =>
        Normalizar(rutaA) is not { } a || Normalizar(rutaB) is not { } b
        || MismaUnidadEntre(a, b, Unidades().Select(u => u.Montaje));

    public bool EnNubeConocida(string ruta)
    {
        if (Normalizar(ruta) is not { } normalizada)
        {
            return false;
        }

        IReadOnlyList<string> carpetas;
        try
        {
            carpetas = _fuente.Carpetas();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }

        var normalizadas = carpetas
            .Where(carpeta => !string.IsNullOrWhiteSpace(carpeta) && Path.IsPathFullyQualified(carpeta))
            .Select(Normalizar)
            .OfType<string>()
            // Una carpeta de nube que es la raíz de una unidad (OneDrive=C:\) haría pasar por nube a toda la unidad.
            .Where(carpeta => !EsRaizDeUnidad(carpeta));
        return DentroDeAlguna(normalizada, normalizadas);
    }

    public bool Existe(string ruta) => Normalizar(ruta) is { } normalizada && Directory.Exists(normalizada);

    public bool PuedeEscribir(string ruta)
    {
        if (Normalizar(ruta) is not { } carpeta || !Directory.Exists(carpeta))
        {
            return false;
        }

        // Un byte en un archivo de nombre único que se borra al cerrarlo; si el cierre no lo borró, se intenta otra vez,
        // solo si esta sonda llegó a crearse (nunca se borra un archivo que no creó).
        var sonda = Path.Combine(carpeta, PrefijoSonda + Guid.NewGuid().ToString("N") + ".tmp");
        var creada = false;
        try
        {
            using (var archivo = new FileStream(
                sonda, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose))
            {
                creada = true;
                archivo.WriteByte(0);
                archivo.Flush(flushToDisk: true);
            }

            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
        finally
        {
            if (creada)
            {
                BorrarSiQuedo(sonda);
            }
        }
    }

    private static void BorrarSiQuedo(string sonda)
    {
        try
        {
            if (File.Exists(sonda))
            {
                File.Delete(sonda);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Una sonda que no se pudo borrar no cambia el resultado; su nombre no coincide con el de ninguna copia.
        }
    }

    /// <summary>La ruta absoluta normalizada, o null si no se puede normalizar.</summary>
    private static string? Completa(string ruta)
    {
        try
        {
            return string.IsNullOrWhiteSpace(ruta) ? null : Path.GetFullPath(ruta);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException
            or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Las unidades del proveedor; si el proveedor falla, ninguna (y nada se toma como local).</summary>
    private List<UnidadDisco> Unidades()
    {
        try
        {
            return [.. _unidades().Where(unidad => unidad is not null && !string.IsNullOrWhiteSpace(unidad.Montaje))];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Punto de montaje y tipo de cada unidad de la PC; las que no se pueden leer se omiten.</summary>
    private static IReadOnlyList<UnidadDisco> UnidadesDeLaPc()
    {
        DriveInfo[] unidades;
        try
        {
            unidades = DriveInfo.GetDrives();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var resultado = new List<UnidadDisco>(unidades.Length);
        foreach (var unidad in unidades)
        {
            try
            {
                resultado.Add(new UnidadDisco(unidad.Name, unidad.DriveType));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Una unidad que no responde no se usa para decidir.
            }
        }

        return resultado;
    }

    /// <summary>
    /// La unidad de una ruta normalizada: con letra, <c>X:\</c> en mayúscula; sin letra, el punto de montaje de prefijo
    /// más largo (por segmentos) entre <paramref name="unidades"/>; null si ninguno la contiene.
    /// </summary>
    private static string? UnidadDe(string ruta, IReadOnlyCollection<string> unidades)
    {
        if (Letra(ruta) is { } letra)
        {
            return letra + ":\\";
        }

        return unidades
            .Where(unidad => !string.IsNullOrWhiteSpace(unidad) && Letra(unidad) is null && DentroDe(ruta, unidad))
            .OrderByDescending(unidad => Segmentos(unidad).Length)
            .FirstOrDefault();
    }

    private static StringComparison Comparacion(string? unidad) =>
        unidad is not null && Letra(unidad) is not null ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool DentroDe(string ruta, string carpeta)
    {
        var conLetra = Letra(ruta) is not null;
        if (conLetra != (Letra(carpeta) is not null))
        {
            return false;
        }

        var comparacion = conLetra ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var segmentosRuta = Segmentos(ruta);
        var segmentosCarpeta = Segmentos(carpeta);
        if (segmentosCarpeta.Length > segmentosRuta.Length)
        {
            return false;
        }

        for (var i = 0; i < segmentosCarpeta.Length; i++)
        {
            if (!string.Equals(segmentosRuta[i], segmentosCarpeta[i], comparacion))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Los segmentos de una ruta: con letra de unidad, separados por <c>\</c> o <c>/</c> (el primero es <c>X:</c>); sin
    /// letra, por <c>/</c> (en Linux <c>\</c> es un carácter válido de un nombre). Los separadores repetidos se ignoran.
    /// </summary>
    private static string[] Segmentos(string ruta) =>
        Letra(ruta) is not null
            ? ruta.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            : ruta.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>La letra de unidad en mayúscula si la ruta empieza con <c>X:</c>; null si no.</summary>
    private static char? Letra(string ruta) =>
        ruta is { Length: >= 2 } && char.IsAsciiLetter(ruta[0]) && ruta[1] == ':' ? char.ToUpperInvariant(ruta[0]) : null;

    private static bool EsSeparador(char caracter) => caracter is '\\' or '/';
}

/// <summary>
/// Una unidad de la PC: su punto de montaje (<c>C:\</c> en Windows, <c>/dev/shm</c> en Linux) y su tipo
/// (<see cref="DriveInfo.DriveType"/>).
/// </summary>
public sealed record UnidadDisco(string Montaje, DriveType Tipo);
