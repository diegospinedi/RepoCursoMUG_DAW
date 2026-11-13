using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Optica.Api.Tests;

/// <summary>
/// Lo que se publica para instalar en la PC del local (<c>dotnet publish</c>, configuración Release) no lleva la ruta
/// absoluta del repositorio en los ensamblados ni en los PDB: esa ruta incluye la carpeta del usuario de quien compiló
/// y aparecería en las trazas de excepción. <c>Directory.Build.props</c> la reemplaza por <c>/_/</c> en Release; las
/// compilaciones Debug (depuración local y cobertura) conservan las rutas reales.
/// <para>
/// Hace un <c>dotnet publish</c> completo y es la prueba más lenta de la solución. Para las corridas rápidas durante el
/// desarrollo se puede excluir con <c>dotnet test Optica.sln --filter "Categoria!=Publicacion"</c> (también en
/// <c>e2e/README.md</c>); la suite completa, sin filtro, la incluye siempre.
/// </para>
/// </summary>
public sealed class PublicacionSinRutasTests : IDisposable
{
    private readonly string _salida = Path.Combine(Path.GetTempPath(), "optica-pruebas-publicacion", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_salida))
        {
            Directory.Delete(_salida, recursive: true);
        }
    }

    [Fact]
    [Trait("Categoria", "Publicacion")]
    public void Publicacion_Release_SinLaRutaDelRepositorioEnEnsambladosNiPdb()
    {
        var repositorio = Repositorio.Raiz();
        Publicar(repositorio);

        // Cada proyecto de src/ publica su ensamblado y su PDB (sin contar a mano cuántos son).
        var proyectos = Directory.GetDirectories(Path.Combine(repositorio, "src"), "Optica.*")
            .Select(Path.GetFileName)
            .ToList();
        Assert.NotEmpty(proyectos);
        var ensamblados = proyectos.Select(p => Path.Combine(_salida, p + ".dll")).ToList();
        var pdbs = proyectos.Select(p => Path.Combine(_salida, p + ".pdb")).ToList();
        foreach (var archivo in ensamblados.Concat(pdbs))
        {
            Assert.True(File.Exists(archivo), $"La publicación no tiene {Path.GetFileName(archivo)}.");
        }

        // La fuente Lato de QuestPDF viaja con la publicación: el PDF del presupuesto no usa fuentes del sistema
        // (D-14 de FEAT-006).
        Assert.True(
            File.Exists(Path.Combine(_salida, "QuestPDF.Fonts.Lato.br")),
            "La publicación no tiene la fuente QuestPDF.Fonts.Lato.br.");

        var prohibidas = RutasProhibidas(repositorio);
        var textosProhibidos = TextosProhibidos();
        foreach (var archivo in ensamblados.Concat(pdbs))
        {
            // Source Link guarda la URL del repositorio, que lleva el usuario de GitHub de quien compiló; el nombre de
            // usuario de la PC tampoco puede quedar. El mensaje no repite el texto encontrado.
            var bytes = File.ReadAllBytes(archivo);
            foreach (var (texto, descripcion) in textosProhibidos)
            {
                Assert.False(
                    ContieneSinMayusculas(bytes, texto),
                    $"{Path.GetFileName(archivo)} contiene {descripcion}.");
            }
        }

        foreach (var ensamblado in ensamblados)
        {
            // La ruta del PDB que queda en el ensamblado (entrada CodeView del directorio de depuración).
            using (var lector = new PEReader(File.OpenRead(ensamblado)))
            {
                var codeView = lector.ReadDebugDirectory().Single(e => e.Type == DebugDirectoryEntryType.CodeView);
                var rutaPdb = lector.ReadCodeViewDebugDirectoryData(codeView).Path;
                AfirmarSinRutas(prohibidas, rutaPdb, $"{Path.GetFileName(ensamblado)} (ruta del PDB)");
                Assert.StartsWith("/_/", rutaPdb, StringComparison.Ordinal);
            }

            // Cualquier otra aparición en los bytes del ensamblado (por ejemplo, rutas de [CallerFilePath]).
            var bytes = File.ReadAllBytes(ensamblado);
            foreach (var prohibida in prohibidas)
            {
                Assert.True(
                    Buscar(bytes, Encoding.UTF8.GetBytes(prohibida)) < 0 && Buscar(bytes, Encoding.Unicode.GetBytes(prohibida)) < 0,
                    $"{Path.GetFileName(ensamblado)} contiene la ruta del repositorio.");
            }
        }

        foreach (var pdb in pdbs)
        {
            // En un PDB portable el nombre de cada documento se guarda en partes: hay que leerlo, no buscar bytes.
            using var proveedor = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdb));
            var lector = proveedor.GetMetadataReader();
            var documentos = lector.Documents.Select(d => lector.GetString(lector.GetDocument(d).Name)).ToList();
            Assert.NotEmpty(documentos);
            foreach (var documento in documentos)
            {
                AfirmarSinRutas(prohibidas, documento, $"{Path.GetFileName(pdb)} (documento)");
            }

            Assert.Contains(documentos, d => d.StartsWith("/_/src/", StringComparison.Ordinal));

            // El resto del PDB (por ejemplo, el JSON de Source Link, que mapea rutas locales a URL) tampoco.
            var bytes = File.ReadAllBytes(pdb);
            foreach (var prohibida in prohibidas)
            {
                Assert.True(
                    Buscar(bytes, Encoding.UTF8.GetBytes(prohibida)) < 0,
                    $"{Path.GetFileName(pdb)} contiene una ruta local (repositorio o carpeta del usuario).");
            }
        }
    }

    private void Publicar(string repositorio)
    {
        var proyecto = Path.Combine(repositorio, "src", "Optica.Api", "Optica.Api.csproj");
        var inicio = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repositorio,
        };
        foreach (var argumento in new[] { "publish", proyecto, "-c", "Release", "-o", _salida, "-nologo", "-v", "q" })
        {
            inicio.ArgumentList.Add(argumento);
        }

        // Variables que el proceso de pruebas hereda de MSBuild y que harían que la publicación use otra instancia.
        foreach (var variable in new[] { "MSBuildExtensionsPath", "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildLoadMicrosoftTargetsReadOnly" })
        {
            inicio.Environment.Remove(variable);
        }

        using var proceso = Process.Start(inicio)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        Assert.True(proceso.WaitForExit(TimeSpan.FromMinutes(5)), "dotnet publish no terminó en 5 minutos.");
        Assert.True(proceso.ExitCode == 0, $"dotnet publish falló ({proceso.ExitCode}):\n{salida.Result}\n{errores.Result}");
    }

    /// <summary>
    /// La raíz del repositorio y la carpeta del usuario, con las dos barras posibles y con la barra invertida escapada
    /// como en JSON.
    /// </summary>
    private static List<string> RutasProhibidas(string repositorio)
    {
        var rutas = new List<string> { Path.TrimEndingDirectorySeparator(repositorio) };
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(perfil))
        {
            rutas.Add(Path.TrimEndingDirectorySeparator(perfil));
        }

        return rutas
            .SelectMany(r => new[] { r.Replace('\\', '/'), r.Replace('/', '\\'), r.Replace("/", "\\").Replace("\\", "\\\\") })
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Las direcciones de GitHub (Source Link) y el nombre de usuario de la PC, con la descripción que va en el mensaje.
    /// Un nombre de menos de 4 caracteres se ignora: aparecería por casualidad en cualquier ensamblado.
    /// </summary>
    private static List<(string Texto, string Descripcion)> TextosProhibidos()
    {
        var textos = new List<(string, string)>
        {
            ("raw.githubusercontent.com", "una URL de Source Link (raw.githubusercontent.com)"),
            ("github.com", "una dirección de github.com"),
        };
        var usuario = Environment.UserName;
        if (usuario.Length >= 4)
        {
            textos.Add((usuario, "el nombre de usuario de la PC"));
        }

        return textos;
    }

    /// <summary>Busca el texto en UTF-8 y en UTF-16, sin distinguir mayúsculas en la parte ASCII.</summary>
    private static bool ContieneSinMayusculas(byte[] datos, string texto)
    {
        var minusculas = AsciiEnMinusculas(datos);
        var patron = texto.ToLowerInvariant();
        return Buscar(minusculas, AsciiEnMinusculas(Encoding.UTF8.GetBytes(patron))) >= 0
            || Buscar(minusculas, AsciiEnMinusculas(Encoding.Unicode.GetBytes(patron))) >= 0;
    }

    private static byte[] AsciiEnMinusculas(byte[] datos)
    {
        var copia = (byte[])datos.Clone();
        for (var i = 0; i < copia.Length; i++)
        {
            if (copia[i] is >= (byte)'A' and <= (byte)'Z')
            {
                copia[i] = (byte)(copia[i] + ('a' - 'A'));
            }
        }

        return copia;
    }

    private static void AfirmarSinRutas(IEnumerable<string> prohibidas, string texto, string donde)
    {
        foreach (var prohibida in prohibidas)
        {
            Assert.False(
                texto.Contains(prohibida, StringComparison.OrdinalIgnoreCase),
                $"{donde} contiene una ruta local (repositorio o carpeta del usuario).");
        }
    }

    private static int Buscar(byte[] datos, byte[] patron) => datos.AsSpan().IndexOf(patron);
}
