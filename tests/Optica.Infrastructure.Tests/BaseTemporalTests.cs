namespace Optica.Infrastructure.Tests;

/// <summary>
/// La prueba cambia la salida de error del proceso (<see cref="Console.SetError"/>), que es global: corre sola, después
/// de las colecciones en paralelo, así ninguna otra prueba escribe ahí mientras tanto.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionSalidaDeError
{
    public const string Nombre = "SalidaDeError";
}

[Collection(ColeccionSalidaDeError.Nombre)]
public sealed class BaseTemporalTests
{
    private const string Marca = "[BaseTemporal]";

    /// <summary>
    /// D-30: si el directorio de prueba no se puede borrar, el aviso nombra solo el tipo de error, como
    /// <c>FabricaApi</c>: ni la ruta ni el mensaje de la excepción, que la repite y puede llevar el nombre de usuario del
    /// sistema operativo.
    /// </summary>
    [Fact]
    public void BorrarDirectorio_NoSePuedeBorrar_AvisaSoloElTipoDeError()
    {
        var ruta = BaseTemporal.CrearDirectorioTemporal();
        var archivo = Path.Combine(ruta, "retenido.txt");
        File.WriteAllText(archivo, "x");
        var anterior = Console.Error;
        using var capturada = new StringWriter();
        try
        {
            using (Retener(ruta, archivo))
            {
                Console.SetError(capturada);
                try
                {
                    BaseTemporal.BorrarDirectorio(ruta);
                }
                finally
                {
                    Console.SetError(anterior);
                }
            }
        }
        finally
        {
            Liberar(ruta);
            BaseTemporal.BorrarDirectorio(ruta);
        }

        var aviso = Assert.Single(
            capturada.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            linea => linea.StartsWith(Marca, StringComparison.Ordinal));
        Assert.Matches(
            @"^\[BaseTemporal\] No se pudo borrar un directorio de prueba \((IOException|UnauthorizedAccessException)\)\.$",
            aviso);
        Assert.DoesNotContain(Path.GetFileName(ruta), aviso, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), aviso, StringComparison.Ordinal);
    }

    /// <summary>
    /// Impide borrar el directorio sin depender de la suerte: en Windows, un archivo abierto sin compartir; en los demás
    /// sistemas, el directorio sin permiso de escritura (no se puede quitar lo que contiene).
    /// </summary>
    private static IDisposable Retener(string ruta, string archivo)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.None);
        }

        File.SetUnixFileMode(ruta, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return new Restaurar(() => Liberar(ruta));
    }

    private static void Liberar(string ruta)
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(ruta))
        {
            File.SetUnixFileMode(ruta, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private sealed class Restaurar(Action accion) : IDisposable
    {
        public void Dispose() => accion();
    }
}
