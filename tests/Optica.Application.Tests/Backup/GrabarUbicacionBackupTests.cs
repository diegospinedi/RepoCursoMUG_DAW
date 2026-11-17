using Microsoft.Extensions.Logging;
using Optica.Application.Backup;
using Optica.Application.Comun;
using Optica.Application.Tests.Acceso;
using Optica.Application.Tests.Catalogo;
using Optica.Domain.Backup;

namespace Optica.Application.Tests.Backup;

/// <summary>
/// Spec FEAT-010, Block 2 (FR-01 a FR-03; AC-01, AC-02, AC-03, AC-17; D-12, D-17, D-19): la grabación de la ubicación
/// del backup con un inspector de ubicaciones falso. Rutas ficticias armadas sobre la carpeta temporal (absolutas en
/// Windows y en Linux), sin nombres de personas; el inspector falso no toca el disco.
/// </summary>
public class GrabarUbicacionBackupTests
{
    private static readonly string Raiz = Path.Combine(Path.GetTempPath(), "optica-pruebas-ubicacion");
    private static readonly string Datos = Path.Combine(Raiz, "DatosOptica");
    private static readonly string Pendrive = Path.Combine(Raiz, "Pendrive", "Backups");
    private static readonly string OneDrive = Path.Combine(Raiz, "Perfil", "OneDrive");

    private readonly UbicacionEnMemoria _almacen = new();
    private readonly InspectorFalso _inspector = new();
    private readonly FabricaRegistrosFalsa _registros = new();

    private GrabarUbicacionBackup CrearCasoDeUso() =>
        new(_almacen, _almacen, _inspector, new DatosLocalesFalsos(Datos), new RelojFijo(), _registros);

    [Fact]
    public async Task Grabar_OtraUnidad_Graba()
    {
        _inspector.OtraUnidad.Add(Pendrive);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Pendrive + "  ");

        // AC-01: grabada (recortada) dentro de la transacción, y devuelta para mostrarla.
        Assert.True(resultado.EsGrabado);
        Assert.Empty(resultado.Errores);
        Assert.Equal(Pendrive, resultado.Ubicacion.Ruta);
        Assert.Equal(Pendrive, _almacen.Guardada?.Ruta);
        Assert.Equal(1, _almacen.Transacciones);
        Assert.Equal(1, _almacen.Confirmaciones);
        Assert.Equal(0, _almacen.AccesosFueraDeTransaccion);
        // La consulta la devuelve igual.
        Assert.Equal(Pendrive, (await new ObtenerUbicacionBackup(_almacen).EjecutarAsync()).Ruta);

        // El registro: fecha y hora UTC, el tipo de destino, y nunca la ruta (D-12: puede llevar el nombre de una persona).
        Assert.Contains(GrabarUbicacionBackup.CategoriaRegistro, _registros.Categorias);
        var entrada = Assert.Single(_registros.Entradas);
        Assert.Equal(LogLevel.Information, entrada.Nivel);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, entrada.Valores["FechaHoraUtc"]);
        var texto = entrada.Mensaje + " " + string.Join(" ", entrada.Valores.Values);
        Assert.DoesNotContain(Pendrive, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Pendrive", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("optica-pruebas-ubicacion", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grabar_MismaUnidadFueraDeNube_Rechazado()
    {
        var mismaUnidad = Path.Combine(Raiz, "Documentos", "Backups");

        var resultado = await CrearCasoDeUso().EjecutarAsync(mismaUnidad);

        // AC-02: no se graba e indica junto al campo que debe estar en otra unidad o en una carpeta de nube.
        AfirmarRechazo(resultado, ValidacionUbicacionBackup.AccionMismaUnidad, mismaUnidad);
        Assert.Contains("otra unidad", ValidacionUbicacionBackup.AccionMismaUnidad, StringComparison.Ordinal);
        Assert.Contains("nube", ValidacionUbicacionBackup.AccionMismaUnidad, StringComparison.Ordinal);
        // La sonda de escritura no se intenta en una carpeta que igual se rechaza.
        Assert.Empty(_inspector.Sondeadas);
    }

    [Fact]
    public async Task Grabar_MismaUnidadEnOneDrive_Graba()
    {
        var enOneDrive = Path.Combine(OneDrive, "Backups");
        _inspector.EnNube.Add(enOneDrive);

        var resultado = await CrearCasoDeUso().EjecutarAsync(enOneDrive);

        // AC-17: misma unidad que la base, pero dentro de la carpeta local de OneDrive.
        Assert.True(resultado.EsGrabado);
        Assert.Equal(enOneDrive, _almacen.Guardada?.Ruta);
        var entrada = Assert.Single(_registros.Entradas);
        Assert.DoesNotContain("OneDrive", entrada.Mensaje + " " + string.Join(" ", entrada.Valores.Values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grabar_Inexistente_Rechazado()
    {
        _inspector.OtraUnidad.Add(Pendrive);
        _inspector.Inexistentes.Add(Pendrive);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Pendrive);

        // AC-03: indica junto al campo que no se puede escribir en ella.
        AfirmarRechazo(resultado, ValidacionUbicacionBackup.AccionInexistente, Pendrive);
        Assert.Contains("no se puede escribir", ValidacionUbicacionBackup.AccionInexistente, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grabar_SinEscritura_Rechazado()
    {
        _inspector.OtraUnidad.Add(Pendrive);
        _inspector.SinEscritura.Add(Pendrive);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Pendrive);

        // AC-03: comprobado al grabar con la sonda del inspector.
        AfirmarRechazo(resultado, ValidacionUbicacionBackup.AccionSinEscritura, Pendrive);
        Assert.Contains("No se puede escribir", ValidacionUbicacionBackup.AccionSinEscritura, StringComparison.Ordinal);
        Assert.Equal([Pendrive], _inspector.Sondeadas);
    }

    [Fact]
    public async Task Grabar_RutaUncODispositivo_Rechazada()
    {
        // D-17: una UNC, una unidad de red o un prefijo de dispositivo; el inspector la informa como no local (las reglas
        // reales de esas rutas se prueban en Infrastructure). Aunque esté "en otra unidad", se rechaza.
        var deRed = Path.Combine(Raiz, "UnidadDeRed", "Backups");
        _inspector.OtraUnidad.Add(deRed);
        _inspector.NoLocales.Add(deRed);

        var resultado = await CrearCasoDeUso().EjecutarAsync(deRed);

        AfirmarRechazo(resultado, ValidacionUbicacionBackup.AccionNoLocal, deRed);
        Assert.Empty(_inspector.Sondeadas);

        // Escritas como UNC o con prefijo de dispositivo y marcadas "no local" por el inspector falso: en Windows son
        // absolutas y el rechazo es el del inspector (AccionNoLocal); en Linux no son absolutas y las rechaza antes el
        // dominio. En los dos casos, en el campo ruta y sin repetirlas.
        foreach (var ruta in new[] { @"\\servidor\recurso\Backups", @"\\?\C:\Backups", @"\\.\C:\Backups" })
        {
            _inspector.NoLocales.Add(ruta);
            var rechazo = await CrearCasoDeUso().EjecutarAsync(ruta);
            Assert.False(rechazo.EsGrabado);
            var esperada = Path.IsPathFullyQualified(ruta)
                ? ValidacionUbicacionBackup.AccionNoLocal
                : ValidacionUbicacionBackup.AccionNoAbsoluta;
            Assert.Equal(new ErrorValidacion(ValidacionUbicacionBackup.CampoRuta, esperada), Assert.Single(rechazo.Errores));
        }
        Assert.Null(_almacen.Guardada);
    }

    [Fact]
    public async Task Grabar_DentroDeCarpetaDatos_Rechazado()
    {
        // D-19: la carpeta de datos, una dentro de ella y una que la contiene; aunque el inspector diga "otra unidad" o
        // "nube" (la carpeta de datos podría estar dentro de OneDrive).
        foreach (var ruta in new[] { Datos, Path.Combine(Datos, "Backups"), Raiz, Datos + Path.DirectorySeparatorChar })
        {
            _inspector.OtraUnidad.Add(ruta.TrimEnd(Path.DirectorySeparatorChar));
            _inspector.EnNube.Add(ruta.TrimEnd(Path.DirectorySeparatorChar));
            var resultado = await CrearCasoDeUso().EjecutarAsync(ruta);
            AfirmarRechazo(resultado, ValidacionUbicacionBackup.AccionCarpetaDatos, ruta);
        }

        // Una hermana con un nombre que empieza igual no está dentro.
        var hermana = Datos + "Copias";
        _inspector.OtraUnidad.Add(hermana);
        Assert.True((await CrearCasoDeUso().EjecutarAsync(hermana)).EsGrabado);

        // La regla vive en el puerto: Application solo la consulta, con la ruta normalizada y la carpeta de datos.
        Assert.Contains((hermana, Datos), _inspector.ConsultasSuperpuestas);
    }

    [Fact]
    public async Task Grabar_PuntosYSeparadorFinal_GrabaNormalizada()
    {
        // Se graba la forma normalizada que devuelve el inspector: sin "..", sin separador final.
        _inspector.OtraUnidad.Add(Pendrive);
        var escrita = Path.Combine(Raiz, "Otra", "..", "Pendrive", "Backups") + Path.DirectorySeparatorChar;

        var resultado = await CrearCasoDeUso().EjecutarAsync(escrita);

        Assert.True(resultado.EsGrabado);
        Assert.Equal(Pendrive, resultado.Ubicacion.Ruta);
        Assert.Equal(Pendrive, _almacen.Guardada?.Ruta);
        Assert.Equal([Pendrive], _inspector.Sondeadas);
    }

    [Fact]
    public async Task Grabar_NormalizadaDemasiadoLarga_Rechazada()
    {
        // El largo de 260 se vuelve a validar sobre la forma normalizada, la que se graba.
        var larga = Path.Combine(Raiz, new string('a', UbicacionBackup.LargoMaximoRuta));
        _inspector.OtraUnidad.Add(Pendrive);
        _inspector.OtraUnidad.Add(larga);
        _inspector.Normalizaciones[Pendrive] = larga;

        var resultado = await CrearCasoDeUso().EjecutarAsync(Pendrive);

        AfirmarRechazo(resultado, ValidacionUbicacionBackup.AccionLarga, Pendrive);
        Assert.Empty(_inspector.Sondeadas);
    }

    private void AfirmarRechazo(ResultadoGrabacionUbicacionBackup resultado, string accion, string ruta)
    {
        Assert.False(resultado.EsGrabado);
        Assert.Null(resultado.Ubicacion);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(new ErrorValidacion(ValidacionUbicacionBackup.CampoRuta, accion), error);
        Assert.DoesNotContain(ruta.TrimEnd(Path.DirectorySeparatorChar), error.Accion, StringComparison.Ordinal);
        Assert.Null(_almacen.Guardada);
        Assert.Equal(0, _almacen.Transacciones);
        Assert.Empty(_registros.Entradas);
    }

    /// <summary>
    /// Inspector sin disco: misma unidad que todo salvo lo de <see cref="OtraUnidad"/>; existe, es local y admite
    /// escritura salvo lo listado. Anota cada sonda de escritura.
    /// </summary>
    private sealed class InspectorFalso : IInspectorUbicaciones
    {
        public HashSet<string> OtraUnidad { get; } = new(StringComparer.Ordinal);

        public HashSet<string> EnNube { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Inexistentes { get; } = new(StringComparer.Ordinal);

        public HashSet<string> SinEscritura { get; } = new(StringComparer.Ordinal);

        public HashSet<string> NoLocales { get; } = new(StringComparer.Ordinal);

        public List<string> Sondeadas { get; } = [];

        /// <summary>Normalizaciones forzadas; las demás, con <see cref="Path.GetFullPath(string)"/> sin separador final.</summary>
        public Dictionary<string, string> Normalizaciones { get; } = new(StringComparer.Ordinal);

        public List<(string A, string B)> ConsultasSuperpuestas { get; } = [];

        public string? Normalizar(string ruta) =>
            Normalizaciones.TryGetValue(ruta, out var forzada)
                ? forzada
                : Path.TrimEndingDirectorySeparator(Path.GetFullPath(ruta));

        /// <summary>Iguales o una dentro de la otra, por prefijo de segmentos sobre las rutas normalizadas.</summary>
        public bool Superpuestas(string rutaA, string rutaB)
        {
            ConsultasSuperpuestas.Add((rutaA, rutaB));
            var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rutaA));
            var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rutaB));
            return a == b
                || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        public bool EsLocal(string ruta) => !NoLocales.Contains(ruta);

        public bool MismaUnidad(string rutaA, string rutaB) => !OtraUnidad.Contains(rutaA) && !OtraUnidad.Contains(rutaB);

        public bool EnNubeConocida(string ruta) => EnNube.Contains(ruta);

        public bool Existe(string ruta) => !Inexistentes.Contains(ruta);

        public bool PuedeEscribir(string ruta)
        {
            Sondeadas.Add(ruta);
            return !SinEscritura.Contains(ruta) && !Inexistentes.Contains(ruta);
        }
    }

    private sealed class DatosLocalesFalsos(string directorio) : IDatosLocales
    {
        public string Directorio => directorio;

        public string DirectorioClaves => Path.Combine(directorio, "claves");

        public string RutaVisible => directorio;

        public bool BaseExiste => true;

        public bool MigracionesPendientes() => false;

        public void RenovarClaves()
        {
        }

        public FallaDatos? DescribirFalla(Exception error) => null;
    }

    /// <summary>La ubicación en memoria y su unidad de trabajo: lo registrado queda recién al confirmar.</summary>
    private sealed class UbicacionEnMemoria : IUbicacionBackupRepository, IUnidadDeTrabajo
    {
        private UbicacionBackup? _pendiente;
        private bool _enTransaccion;

        public UbicacionBackup? Guardada { get; private set; }

        public int Transacciones { get; private set; }

        public int Confirmaciones { get; private set; }

        public int AccesosFueraDeTransaccion { get; private set; }

        public Task<UbicacionBackup?> ObtenerAsync(CancellationToken cancelacion = default) => Task.FromResult(Guardada);

        public void Reemplazar(UbicacionBackup ubicacion)
        {
            if (!_enTransaccion)
            {
                AccesosFueraDeTransaccion++;
            }

            _pendiente = ubicacion;
        }

        public async Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default)
        {
            Transacciones++;
            _enTransaccion = true;
            try
            {
                var resultado = await operacion(cancelacion);
                if (_pendiente is not null)
                {
                    throw new InvalidOperationException("La operación terminó con cambios sin confirmar.");
                }

                return resultado;
            }
            finally
            {
                _enTransaccion = false;
                _pendiente = null;
            }
        }

        public Task ConfirmarAsync(CancellationToken cancelacion = default)
        {
            Confirmaciones++;
            if (_pendiente is not null)
            {
                Guardada = _pendiente;
                _pendiente = null;
            }

            return Task.CompletedTask;
        }
    }
}
