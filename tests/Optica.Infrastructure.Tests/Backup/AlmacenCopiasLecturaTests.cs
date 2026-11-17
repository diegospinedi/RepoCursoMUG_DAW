using Optica.Infrastructure.Backup;

namespace Optica.Infrastructure.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 6 (FR-09, AC-18): <see cref="AlmacenCopiasArchivo.SePuedeLeer"/> sobre carpetas temporales
/// propias. Nunca lanza: una carpeta inexistente (pendrive desconectado), sin permiso o una ruta inválida responden
/// falso.
/// </summary>
public sealed class AlmacenCopiasLecturaTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "optica-pruebas-almacen-lectura", Guid.NewGuid().ToString("N"));

    private readonly AlmacenCopiasArchivo _almacen = new();

    public AlmacenCopiasLecturaTests() => Directory.CreateDirectory(_raiz);

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach (var carpeta in Directory.EnumerateDirectories(_raiz))
            {
                File.SetUnixFileMode(carpeta, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(_raiz, recursive: true);
    }

    [Fact]
    public void SePuedeLeer_CarpetaExistente_Verdadero()
    {
        var vacia = Directory.CreateDirectory(Path.Combine(_raiz, "Vacia")).FullName;
        var conArchivos = Directory.CreateDirectory(Path.Combine(_raiz, "ConArchivos")).FullName;
        File.WriteAllText(Path.Combine(conArchivos, "optica-2026-10-29.db"), "copia");

        Assert.True(_almacen.SePuedeLeer(vacia));
        Assert.True(_almacen.SePuedeLeer(conArchivos));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0invalida")]
    public void SePuedeLeer_RutaInvalida_FalsoSinLanzar(string ruta) => Assert.False(_almacen.SePuedeLeer(ruta));

    [Fact]
    public void SePuedeLeer_CarpetaInexistente_Falso()
    {
        // Pendrive desconectado: la carpeta ya no existe.
        Assert.False(_almacen.SePuedeLeer(Path.Combine(_raiz, "Desconectado", "Backups")));

        // Un archivo con ese nombre tampoco es una carpeta legible.
        var archivo = Path.Combine(_raiz, "archivo.txt");
        File.WriteAllText(archivo, "no es una carpeta");
        Assert.False(_almacen.SePuedeLeer(archivo));
    }

    [Fact]
    public void SePuedeLeer_SinPermiso_Falso()
    {
        if (OperatingSystem.IsWindows())
        {
            // Quitar la lectura en Windows exige ACL; el caso queda cubierto en Linux.
            return;
        }

        var carpeta = Directory.CreateDirectory(Path.Combine(_raiz, "SinPermiso")).FullName;
        File.SetUnixFileMode(carpeta, UnixFileMode.None);
        if (SeListaIgual(carpeta))
        {
            // Como root el modo 000 no impide listar: el caso no probaría nada.
            return;
        }

        Assert.False(_almacen.SePuedeLeer(carpeta));
    }

    private static bool SeListaIgual(string carpeta)
    {
        try
        {
            using var entradas = Directory.EnumerateFileSystemEntries(carpeta).GetEnumerator();
            entradas.MoveNext();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
