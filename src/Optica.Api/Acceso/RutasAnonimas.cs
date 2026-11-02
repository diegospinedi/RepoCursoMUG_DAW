namespace Optica.Api.Acceso;

/// <summary>Una ruta que responde sin sesión: método HTTP y ruta exacta.</summary>
public sealed record RutaAnonima(string Metodo, string Ruta);

/// <summary>
/// La lista blanca de rutas anónimas (R-01). Todo endpoint que no figura acá exige sesión por la política de
/// autorización por defecto; una prueba recorre los endpoints registrados y falla si alguno fuera de esta lista
/// responde sin sesión, y otra falla si la lista cambia.
/// </summary>
public static class RutasAnonimas
{
    public static IReadOnlyList<RutaAnonima> Todas { get; } =
    [
        new("GET", "/api/acceso/estado"),
        new("POST", "/api/acceso/definir"),
        new("POST", "/api/acceso/ingresar"),
        // Las dos de marca las implementa el Block 4; la pantalla de ingreso las necesita sin sesión.
        new("GET", "/api/marca"),
        new("GET", "/api/marca/logo"),
    ];

    /// <summary>Indica si el método y la ruta están en la lista blanca (sin distinguir mayúsculas).</summary>
    public static bool Contiene(string metodo, string ruta)
    {
        var normalizada = "/" + ruta.Trim('/');
        return Todas.Any(r =>
            string.Equals(r.Metodo, metodo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Ruta, normalizada, StringComparison.OrdinalIgnoreCase));
    }
}
