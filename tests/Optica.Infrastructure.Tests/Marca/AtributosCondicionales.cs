namespace Optica.Infrastructure.Tests.Marca;

/// <summary>
/// Una prueba que necesita crear enlaces simbólicos. Si el sistema no lo permite (por ejemplo, Windows sin el modo de
/// desarrollador ni privilegios), la prueba queda omitida con el motivo, en lugar de pasar sin haber probado nada.
/// </summary>
public sealed class FactConEnlacesSimbolicosAttribute : FactAttribute
{
    public FactConEnlacesSimbolicosAttribute()
    {
        if (!Sondeo.SePuedenCrearEnlaces.Value)
        {
            Skip = "El sistema no permite crear enlaces simbólicos: la prueba no aplica.";
        }
    }
}

/// <summary>
/// Una prueba que necesita crear archivos con nombres que Windows no acepta (<c>logo?.png</c>, <c>x:logo.png</c>,
/// <c>CON.png</c>). Donde el sistema de archivos no los permite, la prueba queda omitida.
/// </summary>
public sealed class TheoryConNombresNoValidosEnWindowsAttribute : TheoryAttribute
{
    public TheoryConNombresNoValidosEnWindowsAttribute()
    {
        if (!Sondeo.SeAceptanNombresNoValidosEnWindows.Value)
        {
            Skip = "El sistema de archivos no permite nombres no válidos en Windows: la prueba no aplica.";
        }
    }
}

/// <summary>Comprueba una sola vez, en una carpeta temporal propia, qué permite el sistema de archivos.</summary>
internal static class Sondeo
{
    public static readonly Lazy<bool> SePuedenCrearEnlaces = new(() => Probar(carpeta =>
    {
        var destino = Path.Combine(carpeta, "destino");
        File.WriteAllText(destino, "x");
        File.CreateSymbolicLink(Path.Combine(carpeta, "enlace"), destino);
    }));

    public static readonly Lazy<bool> SeAceptanNombresNoValidosEnWindows = new(() =>
        !OperatingSystem.IsWindows() && Probar(carpeta =>
        {
            foreach (var nombre in new[] { "x:a?b<c>d|e*f\"g", "CON.png" })
            {
                File.WriteAllText(Path.Combine(carpeta, nombre), "x");
            }
        }));

    private static bool Probar(Action<string> accion)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "optica-pruebas-marca", "sondeo-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(carpeta);
            accion(carpeta);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(carpeta, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Una carpeta temporal que quede no afecta a las pruebas.
            }
        }
    }
}
