using Optica.Application.Backup;
using Optica.Infrastructure.Backup;

namespace Optica.Infrastructure.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 2 (FR-02, AC-02, AC-17, D-07, D-17, D-18): las reglas puras del inspector de ubicaciones con
/// listas de unidades falsas de Windows y de Linux, y la nube conocida con una fuente de carpetas armada sobre una carpeta
/// temporal propia que se borra al final. Rutas ficticias, sin nombres de personas.
/// </summary>
public sealed class InspectorUbicacionesTests : IDisposable
{
    /// <summary>Unidades de una PC con Windows: el disco del sistema, un pendrive y un disco externo.</summary>
    private static readonly string[] UnidadesWindows = [@"C:\", @"E:\", @"F:\"];

    /// <summary>Puntos de montaje de un Linux: la raíz, un disco montado y otro montado dentro de él.</summary>
    private static readonly string[] MontajesLinux = ["/", "/dev/shm", "/media/pendrive", "/media/pendrive/interno"];

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "optica-pruebas-inspector", Guid.NewGuid().ToString("N"));

    public InspectorUbicacionesTests() => Directory.CreateDirectory(_raiz);

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    [Fact]
    public void Inspector_MismaUnidadEntre_LetrasYMontajes()
    {
        // Windows (D-07): la unidad es la letra, sin distinguir mayúsculas; otra letra es otra unidad aunque sea otra
        // partición del mismo disco físico.
        Assert.True(InspectorUbicaciones.MismaUnidadEntre(@"C:\OpticaDatos", @"C:\Backups", UnidadesWindows));
        Assert.True(InspectorUbicaciones.MismaUnidadEntre(@"C:\OpticaDatos", @"c:\otra\carpeta", UnidadesWindows));
        Assert.False(InspectorUbicaciones.MismaUnidadEntre(@"C:\OpticaDatos", @"E:\Backups", UnidadesWindows));
        Assert.False(InspectorUbicaciones.MismaUnidadEntre(@"C:\OpticaDatos", @"f:\Copias", UnidadesWindows));
        // Una letra que no figura en la lista sigue siendo otra unidad: la letra manda.
        Assert.False(InspectorUbicaciones.MismaUnidadEntre(@"C:\OpticaDatos", @"G:\Backups", UnidadesWindows));

        // Linux, sin letras (pruebas y e2e): el punto de montaje de prefijo más largo, comparado por segmentos.
        Assert.True(InspectorUbicaciones.MismaUnidadEntre("/var/optica", "/home/backups", MontajesLinux));
        Assert.False(InspectorUbicaciones.MismaUnidadEntre("/var/optica", "/dev/shm/backups", MontajesLinux));
        Assert.False(InspectorUbicaciones.MismaUnidadEntre("/var/optica", "/media/pendrive/Backups", MontajesLinux));
        Assert.True(InspectorUbicaciones.MismaUnidadEntre("/media/pendrive/a", "/media/pendrive/b", MontajesLinux));
        // El montaje anidado gana al que lo contiene.
        Assert.False(InspectorUbicaciones.MismaUnidadEntre("/media/pendrive/a", "/media/pendrive/interno/b", MontajesLinux));
        // Por segmentos, no por texto: /dev/shmx no está en /dev/shm, y /media/pendrive2 no está en /media/pendrive.
        Assert.True(InspectorUbicaciones.MismaUnidadEntre("/var/optica", "/dev/shmx/backups", MontajesLinux));
        Assert.True(InspectorUbicaciones.MismaUnidadEntre("/var/optica", "/media/pendrive2/backups", MontajesLinux));
        // En Linux las mayúsculas distinguen carpetas.
        Assert.True(InspectorUbicaciones.MismaUnidadEntre("/var/optica", "/DEV/SHM/backups", MontajesLinux));

        // Sin poder ubicar alguna de las dos, se toma como la misma unidad: el lado seguro de FR-02.
        Assert.True(InspectorUbicaciones.MismaUnidadEntre("/var/optica", "/dev/shm/backups", []));

        // D-17: rutas UNC y de dispositivo, que se rechazan antes de mirar la unidad (su raíz parecería otra unidad).
        foreach (var ruta in new[]
        {
            @"\\servidor\recurso\Backups", @"\\?\C:\Backups", @"\\.\C:\Backups", "//servidor/recurso/Backups", @"\\?\UNC\servidor\x",
        })
        {
            Assert.True(InspectorUbicaciones.EsUncODispositivo(ruta), ruta);
        }

        foreach (var ruta in new[] { @"C:\Backups", @"E:\", "/dev/shm/backups", "/" })
        {
            Assert.False(InspectorUbicaciones.EsUncODispositivo(ruta), ruta);
        }

        // La instancia real nunca acepta una UNC ni una de dispositivo como local.
        var inspector = new InspectorUbicaciones(new CarpetasFijas());
        Assert.False(inspector.EsLocal(@"\\servidor\recurso\Backups"));
        Assert.False(inspector.EsLocal(@"\\?\C:\Backups"));
        Assert.False(inspector.EsLocal(@"\\.\C:\Backups"));
        Assert.True(inspector.EsLocal(_raiz));
    }

    [Fact]
    public void Inspector_DentroDeNubeConocida()
    {
        // La cuenta que corre la Api (D-18): OneDrive personal y de empresa por sus variables, Dropbox por su info.json en
        // LOCALAPPDATA y Google Drive para escritorio en la carpeta del usuario. Todo armado bajo la carpeta temporal.
        var perfil = Crear("perfil");
        var oneDrive = Crear("perfil", "OneDrive");
        var oneDriveEmpresa = Crear("perfil", "OneDrive - Empresa Ficticia");
        var dropbox = Crear("perfil", "Dropbox");
        var local = Crear("local");
        var roaming = Crear("roaming");
        Directory.CreateDirectory(Path.Combine(local, "Dropbox"));
        File.WriteAllText(
            Path.Combine(local, "Dropbox", "info.json"),
            "{\"personal\":{\"path\":" + System.Text.Json.JsonSerializer.Serialize(dropbox) + ",\"host\":1}}");
        // Un info.json roto en APPDATA no impide leer el resto ni lanza.
        Directory.CreateDirectory(Path.Combine(roaming, "Dropbox"));
        File.WriteAllText(Path.Combine(roaming, "Dropbox", "info.json"), "{\"business\":{\"path\": esto no es json");

        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["OneDrive"] = oneDrive,
            ["OneDriveConsumer"] = oneDrive,
            ["OneDriveCommercial"] = oneDriveEmpresa,
            ["LOCALAPPDATA"] = local,
            ["APPDATA"] = roaming,
            ["USERPROFILE"] = perfil,
        };
        var fuente = new FuenteCarpetasNube(nombre => variables.GetValueOrDefault(nombre));

        // Google Drive para escritorio solo cuenta con su indicio de instalación (%LOCALAPPDATA%\Google\DriveFS): sin él,
        // una carpeta "Mi unidad" creada a mano no es nube.
        var sinGoogle = fuente.Carpetas();
        foreach (var carpeta in new[] { "Google Drive", "My Drive", "Mi unidad" })
        {
            Assert.DoesNotContain(Path.Combine(perfil, carpeta), sinGoogle);
        }

        Assert.False(new InspectorUbicaciones(fuente).EnNubeConocida(Path.Combine(perfil, "Mi unidad", "Backups")));
        Directory.CreateDirectory(Path.Combine(local, "Google", "DriveFS"));

        var carpetas = fuente.Carpetas();
        Assert.Contains(oneDrive, carpetas);
        Assert.Contains(oneDriveEmpresa, carpetas);
        Assert.Contains(dropbox, carpetas);
        Assert.Contains(Path.Combine(perfil, "Google Drive"), carpetas);
        Assert.Contains(Path.Combine(perfil, "My Drive"), carpetas);
        Assert.Contains(Path.Combine(perfil, "Mi unidad"), carpetas);

        var inspector = new InspectorUbicaciones(fuente);

        // AC-17: una carpeta "Backups" dentro de la carpeta local de OneDrive, en la misma unidad, es nube conocida.
        Assert.True(inspector.EnNubeConocida(Path.Combine(oneDrive, "Backups")));
        Assert.True(inspector.EnNubeConocida(Path.Combine(oneDriveEmpresa, "Copias", "Optica")));
        Assert.True(inspector.EnNubeConocida(Path.Combine(dropbox, "Backups")));
        Assert.True(inspector.EnNubeConocida(Path.Combine(perfil, "Mi unidad", "Backups")));
        Assert.True(inspector.EnNubeConocida(Path.Combine(perfil, "Google Drive", "Backups")));
        // Normalizada: un ".." que sale de la nube no cuenta, y uno que vuelve a entrar sí.
        Assert.False(inspector.EnNubeConocida(Path.Combine(oneDrive, "..", "Backups")));
        Assert.True(inspector.EnNubeConocida(Path.Combine(perfil, "x", "..", "OneDrive", "Backups")));

        // AC-02: fuera de la nube, no; tampoco una carpeta hermana con un nombre que empieza igual.
        Assert.False(inspector.EnNubeConocida(Path.Combine(perfil, "Documentos", "Backups")));
        Assert.False(inspector.EnNubeConocida(oneDrive + "Falso"));
        Assert.False(inspector.EnNubeConocida(perfil));

        // Una carpeta de nube que es la raíz de una unidad (OneDrive=C:\ o el path de Dropbox en la raíz) se descarta: si
        // no, toda la unidad pasaría por nube.
        var raizDeUnidad = Path.GetPathRoot(_raiz)!;
        Assert.True(InspectorUbicaciones.EsRaizDeUnidad(raizDeUnidad));
        Assert.True(InspectorUbicaciones.EsRaizDeUnidad(@"C:\"));
        Assert.True(InspectorUbicaciones.EsRaizDeUnidad("c:"));
        Assert.True(InspectorUbicaciones.EsRaizDeUnidad("/"));
        Assert.False(InspectorUbicaciones.EsRaizDeUnidad(@"C:\OneDrive"));
        Assert.False(InspectorUbicaciones.EsRaizDeUnidad("/dev/shm"));
        var dropboxEnLaRaiz = Crear("local-raiz");
        Directory.CreateDirectory(Path.Combine(dropboxEnLaRaiz, "Dropbox"));
        File.WriteAllText(
            Path.Combine(dropboxEnLaRaiz, "Dropbox", "info.json"),
            "{\"personal\":{\"path\":" + System.Text.Json.JsonSerializer.Serialize(raizDeUnidad) + "}}");
        var enLaRaiz = new FuenteCarpetasNube(nombre => nombre switch
        {
            "OneDriveCommercial" => raizDeUnidad,
            "LOCALAPPDATA" => dropboxEnLaRaiz,
            _ => null,
        });
        Assert.False(new InspectorUbicaciones(enLaRaiz).EnNubeConocida(Path.Combine(perfil, "Documentos", "Backups")));
        Assert.False(new InspectorUbicaciones(enLaRaiz).EnNubeConocida(raizDeUnidad));

        // Un info.json de más de TamanoMaximoInfoDropbox se omite aunque sea JSON válido.
        var grande = Crear("local-grande");
        Directory.CreateDirectory(Path.Combine(grande, "Dropbox"));
        File.WriteAllText(
            Path.Combine(grande, "Dropbox", "info.json"),
            "{\"personal\":{\"path\":" + System.Text.Json.JsonSerializer.Serialize(dropbox) + ",\"relleno\":\""
            + new string('a', FuenteCarpetasNube.TamanoMaximoInfoDropbox) + "\"}}");
        Assert.Empty(new FuenteCarpetasNube(nombre => nombre == "LOCALAPPDATA" ? grande : null).Carpetas());

        // Sin variables ni archivos no hay nube conocida, y una variable relativa o vacía se ignora.
        var vacia = new FuenteCarpetasNube(nombre => nombre == "OneDrive" ? "relativa/OneDrive" : "");
        Assert.Empty(vacia.Carpetas());
        Assert.False(new InspectorUbicaciones(vacia).EnNubeConocida(Path.Combine(oneDrive, "Backups")));
    }

    [Fact]
    public void Inspector_DentroDeAlguna_PorSegmentos()
    {
        // Windows: segmentos sin distinguir mayúsculas, con cualquiera de las dos barras.
        Assert.True(InspectorUbicaciones.DentroDeAlguna(@"C:\Nube\OneDrive\Backups", [@"C:\Nube\OneDrive"]));
        Assert.True(InspectorUbicaciones.DentroDeAlguna(@"c:\nube\onedrive\Backups", [@"C:\Nube\OneDrive\"]));
        Assert.True(InspectorUbicaciones.DentroDeAlguna(@"C:\Nube\OneDrive", [@"C:\Nube\OneDrive"]));
        Assert.True(InspectorUbicaciones.DentroDeAlguna("C:/Nube/OneDrive/Backups", [@"C:\Nube\OneDrive"]));
        Assert.False(InspectorUbicaciones.DentroDeAlguna(@"C:\Nube\OneDriveFalso\Backups", [@"C:\Nube\OneDrive"]));
        Assert.False(InspectorUbicaciones.DentroDeAlguna(@"C:\Nube", [@"C:\Nube\OneDrive"]));
        Assert.False(InspectorUbicaciones.DentroDeAlguna(@"D:\Nube\OneDrive\Backups", [@"C:\Nube\OneDrive"]));

        // Linux: segmentos que distinguen mayúsculas.
        Assert.True(InspectorUbicaciones.DentroDeAlguna("/srv/nube/Dropbox/Backups", ["/otra", "/srv/nube/Dropbox"]));
        Assert.True(InspectorUbicaciones.DentroDeAlguna("/srv/nube/Dropbox/", ["/srv/nube/Dropbox"]));
        Assert.False(InspectorUbicaciones.DentroDeAlguna("/srv/nube/DropboxFalso", ["/srv/nube/Dropbox"]));
        Assert.False(InspectorUbicaciones.DentroDeAlguna("/srv/nube/dropbox/Backups", ["/srv/nube/Dropbox"]));
        Assert.True(InspectorUbicaciones.DentroDeAlguna("/cualquier/carpeta", ["/"]));

        // Una ruta de Windows no está dentro de una de Linux ni al revés; vacías y en blanco se ignoran.
        Assert.False(InspectorUbicaciones.DentroDeAlguna(@"C:\srv\nube", ["/srv/nube"]));
        Assert.False(InspectorUbicaciones.DentroDeAlguna("/srv/nube", [@"C:\srv\nube"]));
        Assert.False(InspectorUbicaciones.DentroDeAlguna("/srv/nube", ["", "   "]));
        Assert.False(InspectorUbicaciones.DentroDeAlguna("/srv/nube", []));

        // La instancia real: existe y admite escritura sobre una carpeta temporal propia, y la sonda no deja archivos.
        var inspector = new InspectorUbicaciones(new CarpetasFijas());
        var carpeta = Crear("Backups");
        Assert.True(inspector.Existe(carpeta));
        Assert.True(inspector.PuedeEscribir(carpeta));
        Assert.Empty(Directory.EnumerateFileSystemEntries(carpeta));
        var inexistente = Path.Combine(_raiz, "NoExiste");
        Assert.False(inspector.Existe(inexistente));
        Assert.False(inspector.PuedeEscribir(inexistente));
        Assert.False(Directory.Exists(inexistente));
        Assert.True(inspector.MismaUnidad(carpeta, Path.Combine(_raiz, "Otra")));
    }

    [Fact]
    public void Inspector_Superpuestas_PorSegmentosEnLosDosSentidos()
    {
        // Regla pura (D-19): la misma carpeta, una dentro de la otra en cualquier sentido; con letra, sin distinguir
        // mayúsculas y con separador final o no.
        Assert.True(InspectorUbicaciones.SuperpuestasEntre(@"C:\OpticaDatos", @"C:\OpticaDatos"));
        Assert.True(InspectorUbicaciones.SuperpuestasEntre(@"C:\OpticaDatos", @"c:\opticadatos\"));
        Assert.True(InspectorUbicaciones.SuperpuestasEntre(@"C:\OpticaDatos\Backups", @"C:\OpticaDatos"));
        Assert.True(InspectorUbicaciones.SuperpuestasEntre(@"C:\", @"C:\OpticaDatos"));
        Assert.False(InspectorUbicaciones.SuperpuestasEntre(@"C:\OpticaDatosCopias", @"C:\OpticaDatos"));
        Assert.False(InspectorUbicaciones.SuperpuestasEntre(@"E:\OpticaDatos", @"C:\OpticaDatos"));
        // Sin letra, distinguiendo mayúsculas.
        Assert.True(InspectorUbicaciones.SuperpuestasEntre("/var/optica/", "/var/optica"));
        Assert.True(InspectorUbicaciones.SuperpuestasEntre("/var", "/var/optica"));
        Assert.False(InspectorUbicaciones.SuperpuestasEntre("/var/Optica", "/var/optica"));
        Assert.False(InspectorUbicaciones.SuperpuestasEntre("/var/opticaX", "/var/optica"));

        // La instancia normaliza antes de comparar: "..", separador final y, en Windows, las mayúsculas.
        var inspector = new InspectorUbicaciones(new CarpetasFijas());
        var datos = Crear("Datos");
        Assert.True(inspector.Superpuestas(Path.Combine(_raiz, "x", "..", "Datos") + Path.DirectorySeparatorChar, datos));
        Assert.True(inspector.Superpuestas(_raiz, datos));
        Assert.True(inspector.Superpuestas(Path.Combine(datos, "Backups"), datos));
        Assert.False(inspector.Superpuestas(datos + "Copias", datos));
        Assert.Equal(OperatingSystem.IsWindows(), inspector.Superpuestas(datos.ToUpperInvariant(), datos.ToLowerInvariant()));

        // La forma normalizada: sin ".." ni separador final.
        Assert.Equal(datos, inspector.Normalizar(Path.Combine(_raiz, "x", "..", "Datos") + Path.DirectorySeparatorChar));
        Assert.Equal(Path.GetPathRoot(_raiz), inspector.Normalizar(Path.GetPathRoot(_raiz)!));
    }

    [Fact]
    public void Inspector_EsLocal_RedODesconocida_CerradoPorDefecto()
    {
        // D-17 y el contrato del puerto: una unidad de red, una de tipo desconocido o sin raíz, o una que no aparece en la
        // lista, no es local. Unidades inyectadas, según el sistema en que corre la prueba.
        var windows = OperatingSystem.IsWindows();
        var unidades = windows
            ? new[]
            {
                new UnidadDisco(@"C:\", DriveType.Fixed), new UnidadDisco(@"E:\", DriveType.Removable),
                new UnidadDisco(@"N:\", DriveType.Network), new UnidadDisco(@"U:\", DriveType.Unknown),
                new UnidadDisco(@"R:\", DriveType.NoRootDirectory),
            }
            : new[]
            {
                new UnidadDisco("/", DriveType.Fixed), new UnidadDisco("/media/pendrive", DriveType.Removable),
                new UnidadDisco("/mnt/red", DriveType.Network), new UnidadDisco("/mnt/rara", DriveType.Unknown),
                new UnidadDisco("/mnt/sinraiz", DriveType.NoRootDirectory),
            };
        string Ruta(string letra, string montaje) => windows ? letra + @":\Backups" : montaje + "/Backups";
        var inspector = new InspectorUbicaciones(new CarpetasFijas(), () => unidades);

        Assert.True(inspector.EsLocal(Ruta("C", "/home")));
        Assert.True(inspector.EsLocal(Ruta("e", "/media/pendrive")));
        Assert.False(inspector.EsLocal(Ruta("N", "/mnt/red")));
        Assert.False(inspector.EsLocal(Ruta("U", "/mnt/rara")));
        Assert.False(inspector.EsLocal(Ruta("R", "/mnt/sinraiz")));

        // Sin la unidad en la lista (otra letra, o ninguna unidad conocida), no se puede evaluar: no es local.
        if (windows)
        {
            Assert.False(inspector.EsLocal(@"G:\Backups"));
        }

        Assert.False(new InspectorUbicaciones(new CarpetasFijas(), () => []).EsLocal(Ruta("C", "/home")));
    }

    private string Crear(params string[] partes)
    {
        var ruta = Path.Combine([_raiz, .. partes]);
        Directory.CreateDirectory(ruta);
        return ruta;
    }

    private sealed class CarpetasFijas(params string[] carpetas) : IFuenteCarpetasNube
    {
        public IReadOnlyList<string> Carpetas() => carpetas;
    }
}
