using System.Globalization;
using Microsoft.Extensions.Logging;
using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Application.Tests.Acceso;
using Optica.Domain.Catalogo;

namespace Optica.Application.Tests.Catalogo;

/// <summary>
/// La carga de la planilla de costos (Block 3 de FEAT-011): el lector es un doble que devuelve una lectura fija, y el
/// catálogo y la configuración viven en una sola unidad de trabajo en memoria. Códigos y precios ficticios.
/// </summary>
public class ActualizarCostosPorPlanillaTests
{
    private readonly CatalogoYConfiguracionEnMemoria _almacen = new();
    private readonly FabricaRegistrosFalsa _registros = new();
    private readonly RelojFijo _reloj = new();

    /// <summary>El candado compartido por las cargas de cada prueba, como el singleton de la Api (D-25).</summary>
    private readonly CandadoCargaPlanilla _candado = new();

    private ArticulosEnMemoria Articulos => _almacen.Articulos;

    private static readonly ReadOnlyMemory<byte> Archivo = new byte[] { 0x50, 0x4B, 0x03, 0x04 };

    private ActualizarCostosPorPlanilla CrearCasoDeUso(LectorFijo lector) =>
        new(lector, _almacen, _almacen.Articulos, _almacen, _reloj, _registros, _candado);

    // ---- Aplicada (AC-02, D-13, FR-14, FR-15) ----

    [Fact]
    public async Task Ejecutar_FilasValidasEInexistente_AplicaYListaRazon()
    {
        var abc = Articulos.Sembrar("ABC-1", costo: 1210m, version: 4);
        var def = Articulos.Sembrar("DEF-2", costo: 100m);
        var lector = LectorDe(Fila(2, "ABC-1", 2420m), Fila(3, "NO-EXISTE", 100m), Fila(4, " def-2 ", 200m));

        var resultado = await CrearCasoDeUso(lector).EjecutarAsync(Archivo);

        var aplicada = Assert.IsType<ResultadoPlanillaCostos.Aplicada>(resultado);
        Assert.Equal(3, aplicada.CantidadFilasLeidas);
        Assert.Equal(2, aplicada.CantidadAplicadas);
        var noAplicada = Assert.Single(aplicada.NoAplicadas);
        Assert.Equal((3, "NO-EXISTE", RazonNoAplicada.CodigoInexistente), (noAplicada.Numero, noAplicada.Codigo, noAplicada.Razon));
        Assert.Empty(aplicada.ParaRevisar);

        // AC-01: 2.420 con margen 50 da 3.630,00; la versión sube desde la leída.
        var abcGuardado = Articulos.Guardado(abc.Codigo!.Value);
        Assert.Equal((2420m, 3630m, 5L, true), (abcGuardado.PrecioCosto, abcGuardado.PrecioVenta, abcGuardado.Version, abcGuardado.Activo));
        var defGuardado = Articulos.Guardado(def.Codigo!.Value);
        Assert.Equal((200m, 300m, 1L), (defGuardado.PrecioCosto, defGuardado.PrecioVenta, defGuardado.Version));

        // Leído fuera de la transacción; todo lo demás dentro, con una sola confirmación (D-15, R-06).
        Assert.Equal(1, lector.Lecturas);
        Assert.Equal((LectorFijo.Acceso, false), Articulos.Accesos[0]);
        Assert.All(Articulos.Accesos.Skip(1), acceso => Assert.True(acceso.DentroDeOperacion));
        Assert.Equal(1, Articulos.Operaciones);
        Assert.Equal(1, Articulos.OperacionesConfirmadas);
        Assert.Equal(1, Articulos.Confirmaciones);
        Assert.Equal(1, Articulos.ListadosParaRecalculo);
        Assert.Equal(2, Articulos.CostosActualizadosRegistrados.Count);
    }

    /// <summary>
    /// D-13: una fila con costo y venta iguales a los guardados cuenta como aplicada y no se registra. "Para revisar" son
    /// las aplicadas con costo cero o negativo (FR-14) o sobre un inactivo (FR-15), en el orden de la hoja.
    /// </summary>
    [Fact]
    public async Task Ejecutar_SinCambioNegativoEInactivo_ParaRevisarYSoloRegistraLasQueCambian()
    {
        var igual = Articulos.Sembrar("IGU-1", costo: 1210m, version: 2);
        Articulos.Sembrar("NEG-2", costo: 100m);
        var inactivo = Articulos.Sembrar("INA-3", costo: 100m, activo: false, version: 7);
        var lector = LectorDe(Fila(2, "IGU-1", 1210m), Fila(3, "NEG-2", -100m), Fila(4, "INA-3", 400m));

        var aplicada = Assert.IsType<ResultadoPlanillaCostos.Aplicada>(await CrearCasoDeUso(lector).EjecutarAsync(Archivo));

        Assert.Equal((3, 3), (aplicada.CantidadFilasLeidas, aplicada.CantidadAplicadas));
        Assert.Empty(aplicada.NoAplicadas);
        Assert.Equal([3, 4], aplicada.ParaRevisar.Select(f => f.Numero));
        Assert.True(aplicada.ParaRevisar[0].PrecioNoPositivo);
        Assert.True(aplicada.ParaRevisar[1].Inactivo);
        Assert.Equal(["NEG-2", "INA-3"], Articulos.CostosActualizadosRegistrados.Select(a => a.CodigoProveedor));
        Assert.Equal(2L, Articulos.Guardado(igual.Codigo!.Value).Version);
        var inactivoGuardado = Articulos.Guardado(inactivo.Codigo!.Value);
        Assert.Equal((400m, 600m, false, 8L), (inactivoGuardado.PrecioCosto, inactivoGuardado.PrecioVenta, inactivoGuardado.Activo, inactivoGuardado.Version));
    }

    // ---- Rechazos sin tocar la base ----

    [Fact]
    public async Task Ejecutar_NoEsXlsx_RechazadoSinTocarLaBase()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        var antes = Articulos.Guardados.ToList();

        var resultado = await CrearCasoDeUso(new LectorFijo(LecturaPlanilla.NoEsXlsx.Instancia, Articulos))
            .EjecutarAsync(Archivo);

        Assert.IsType<ResultadoPlanillaCostos.NoEsXlsx>(resultado);
        AfirmarSinTocarLaBase(antes);
    }

    [Fact]
    public async Task Ejecutar_ExcedeLimites_RechazadoSinTocarLaBase()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        var antes = Articulos.Guardados.ToList();

        var resultado = await CrearCasoDeUso(new LectorFijo(LecturaPlanilla.ExcedeLimites.Instancia, Articulos))
            .EjecutarAsync(Archivo);

        Assert.IsType<ResultadoPlanillaCostos.ExcedeLimites>(resultado);
        AfirmarSinTocarLaBase(antes);
    }

    /// <summary>
    /// Un lector que no cumple su contrato (aquí, devuelve null: no hay otro subtipo posible de
    /// <see cref="LecturaPlanilla"/>) es un error de programación: lanza con un mensaje fijo, sin datos, y no toca la base.
    /// </summary>
    [Fact]
    public async Task Ejecutar_LecturaDesconocida_LanzaSinTocarLaBase()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        var antes = Articulos.Guardados.ToList();

        var error = await Assert.ThrowsAsync<System.Diagnostics.UnreachableException>(
            () => CrearCasoDeUso(new LectorFijo(null!, Articulos)).EjecutarAsync(Archivo));

        Assert.Equal("El lector de la planilla devolvió una lectura desconocida.", error.Message);
        Assert.Null(error.InnerException);
        AfirmarSinTocarLaBase(antes);
    }

    /// <summary>AC-04 (tercera columna) y AC-05 (encabezados "Codigo" y "Precio"): nada se evalúa ni se graba (FR-04).</summary>
    [Theory]
    [InlineData(PlanillaCostos.EncabezadoCodigo, PlanillaCostos.EncabezadoPrecio, true)]
    [InlineData("Codigo", "Precio", false)]
    public async Task Ejecutar_FormatoInesperado_RechazadoSinTocarLaBase(string encabezadoA, string encabezadoB, bool columnasDeMas)
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        var antes = Articulos.Guardados.ToList();
        var contenido = new ContenidoPlanilla(
            new CeldaPlanilla.Texto(encabezadoA), new CeldaPlanilla.Texto(encabezadoB), columnasDeMas,
            [Fila(2, "ABC-1", 2420m)]);

        var resultado = await CrearCasoDeUso(new LectorFijo(new LecturaPlanilla.Leida(contenido), Articulos))
            .EjecutarAsync(Archivo);

        Assert.IsType<ResultadoPlanillaCostos.FormatoInesperado>(resultado);
        AfirmarSinTocarLaBase(antes);
    }

    // ---- Fallas al confirmar (D-15, R-06) ----

    [Fact]
    public async Task Ejecutar_ConflictoAlConfirmar_NadaAplicado()
    {
        var abc = Articulos.Sembrar("ABC-1", costo: 1210m);
        Articulos.Sembrar("DEF-2", costo: 100m);
        var antes = Articulos.Guardados.ToList();
        // Otra pestaña confirma un cambio del artículo entre la lectura y la confirmación: su versión ya no es la leída.
        Articulos.AntesDeConfirmar = () => Articulos.EscribirDesdeOtraPestana(abc.Codigo!.Value);
        var lector = LectorDe(Fila(2, "DEF-2", 200m), Fila(3, "ABC-1", 2420m));

        var conflicto = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => CrearCasoDeUso(lector).EjecutarAsync(Archivo));

        Assert.Equal(MotivoConflicto.Concurrencia, conflicto.Motivo);
        Assert.Equal(0, Articulos.OperacionesConfirmadas);
        AfirmarSinCambios(antes);
        Assert.Empty(_registros.Entradas);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ejecutar_ErrorAlConfirmar_NoRegistraLaCarga(bool enElCommitExterno)
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        Articulos.Sembrar("DEF-2", costo: 100m);
        var antes = Articulos.Guardados.ToList();
        var falla = new InvalidOperationException("Falla de la base.");
        if (enElCommitExterno)
        {
            Articulos.ErrorAlConfirmarOperacion = falla;
        }
        else
        {
            Articulos.ErrorAlConfirmar = falla;
        }

        var lanzada = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrearCasoDeUso(LectorDe(Fila(2, "ABC-1", 2420m), Fila(3, "DEF-2", 200m))).EjecutarAsync(Archivo));

        Assert.Same(falla, lanzada);
        Assert.Equal(1, Articulos.Operaciones);
        Assert.Equal(0, Articulos.OperacionesConfirmadas);
        AfirmarSinCambios(antes);
        Assert.Empty(_registros.Entradas);
    }

    // ---- Registro en el log (D-16, R-05, R-07) ----

    [Fact]
    public async Task Ejecutar_Log_SoloCantidadesSinCodigosNiPrecios()
    {
        Articulos.Sembrar("QZ-777", "Lente distintivo", costo: 100m, activo: false);
        Articulos.Sembrar("ZX-3141", "Armazón distintivo", costo: 200m);
        Articulos.Sembrar("ZX-5005", "Estuche distintivo", costo: 300m);
        var lector = LectorDe(
            Fila(2, "QZ-777", -98765.43m),
            Fila(3, "ZX-3141", 4321.09m),
            Fila(4, "NO-EXISTE-QZ", 1111.11m),
            Fila(5, "REP-QZ", 2222.22m),
            Fila(6, " rep-qz ", 3333.33m),
            new FilaPlanilla(7, new CeldaPlanilla.Texto("ZX-5005"), new CeldaPlanilla.Texto("precio-raro-QZ")));
        var confirmadasAlRegistrar = -1;
        _registros.AlRegistrar = () => confirmadasAlRegistrar = Articulos.OperacionesConfirmadas;

        var aplicada = Assert.IsType<ResultadoPlanillaCostos.Aplicada>(await CrearCasoDeUso(lector).EjecutarAsync(Archivo));

        Assert.Equal((6, 2), (aplicada.CantidadFilasLeidas, aplicada.CantidadAplicadas));
        Assert.Equal("Optica.Application.Catalogo.Planilla", ActualizarCostosPorPlanilla.CategoriaRegistro);
        Assert.Contains(ActualizarCostosPorPlanilla.CategoriaRegistro, _registros.Categorias);
        var entrada = Assert.Single(_registros.Entradas);
        Assert.Equal(LogLevel.Information, entrada.Nivel);
        Assert.Null(entrada.Error);
        // Después del commit de la operación (R-07).
        Assert.Equal(1, confirmadasAlRegistrar);

        var valores = entrada.Valores.Where(par => par.Key != "{OriginalFormat}")
            .ToDictionary(par => par.Key, par => par.Value);
        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["FilasLeidas"] = 6,
                ["Aplicadas"] = 2,
                ["CodigoInexistente"] = 1,
                ["PrecioInvalido"] = 1,
                ["CodigoRepetido"] = 2,
                ["PrecioNoPositivo"] = 1,
                ["Inactivos"] = 1,
                ["FechaHoraUtc"] = RelojFijo.Instante.UtcDateTime,
            },
            valores);

        // R-05: ni códigos, ni precios, ni texto de celdas, ni descripciones, ni en el mensaje ni en sus valores.
        var textos = entrada.Valores.Select(par => Convert.ToString(par.Value, CultureInfo.InvariantCulture) ?? string.Empty)
            .Append(entrada.Mensaje)
            .ToList();
        string[] prohibidos =
        [
            "QZ", "qz", "ZX", "3141", "5005", "777", "98765", "4321", "1111", "2222", "3333", "precio-raro",
            "distintivo", "NO-EXISTE",
        ];
        foreach (var prohibido in prohibidos)
        {
            Assert.All(textos, texto => Assert.DoesNotContain(prohibido, texto, StringComparison.Ordinal));
        }
    }

    // ---- Cargas simultáneas (D-23, R-02, R-04) ----

    /// <summary>
    /// D-23: mientras una carga está dentro de su transacción, otra espera en fila sin leer su planilla; al terminar la
    /// primera, la segunda lee, aplica y confirma. Las dos usan instancias distintas del caso de uso (como dos pedidos de
    /// la Api, cada uno con su alcance) y el mismo <see cref="CandadoCargaPlanilla"/>, como el singleton de la Api (D-25).
    /// La espera se observa con la señal del candado, sin demoras por tiempo.
    /// </summary>
    [Fact]
    public async Task Ejecutar_DosCargasSimultaneas_NoSeSolapan()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        Articulos.Sembrar("DEF-2", costo: 100m);
        using var dentroDeLaTransaccion = new ManualResetEventSlim();
        using var liberar = new ManualResetEventSlim();
        using var esperando = new ManualResetEventSlim();
        _candado.EsperaIniciada += (_, _) => esperando.Set();
        var confirmaciones = 0;
        Articulos.AntesDeConfirmar = () =>
        {
            // Solo la primera confirmación se detiene, dentro de la transacción de la primera carga.
            if (Interlocked.Increment(ref confirmaciones) == 1)
            {
                dentroDeLaTransaccion.Set();
                liberar.Wait(TimeSpan.FromSeconds(30));
            }
        };
        var primerLector = LectorDe(Fila(2, "ABC-1", 2420m));
        var segundoLector = LectorDe(Fila(2, "DEF-2", 200m));

        var primera = Task.Run(() => CrearCasoDeUso(primerLector).EjecutarAsync(Archivo));
        Task<ResultadoPlanillaCostos>? segunda = null;
        try
        {
            Assert.True(dentroDeLaTransaccion.Wait(TimeSpan.FromSeconds(30)), "La primera carga no llegó a confirmar.");
            segunda = Task.Run(() => CrearCasoDeUso(segundoLector).EjecutarAsync(Archivo));

            // La segunda espera el candado: ni leyó su planilla ni terminó mientras la primera sigue en su transacción.
            Assert.True(esperando.Wait(TimeSpan.FromSeconds(30)), "La segunda carga no quedó esperando el candado.");
            Assert.Equal(0, segundoLector.Lecturas);
            Assert.False(segunda.IsCompleted);
        }
        finally
        {
            liberar.Set();
        }

        var resultados = await Task.WhenAll(primera, segunda);

        Assert.All(resultados, resultado => Assert.Equal(1, Assert.IsType<ResultadoPlanillaCostos.Aplicada>(resultado).CantidadAplicadas));
        Assert.Equal((1, 1), (primerLector.Lecturas, segundoLector.Lecturas));
        Assert.Equal(2, Articulos.OperacionesConfirmadas);
        Assert.Equal(
            [2420m, 200m],
            Articulos.Guardados.OrderBy(a => a.CodigoProveedor).Select(a => a.PrecioCosto));
    }

    /// <summary>
    /// D-23: una carga que espera su turno y se cancela (el navegador cerró el pedido) termina con
    /// <see cref="OperationCanceledException"/> sin leer la planilla ni tocar la base; la que tenía el turno sigue y lo
    /// libera.
    /// </summary>
    [Fact]
    public async Task Ejecutar_CanceladaMientrasEspera_NoLeeNiTocaLaBase()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        using var dentroDeLaTransaccion = new ManualResetEventSlim();
        using var liberar = new ManualResetEventSlim();
        using var esperando = new ManualResetEventSlim();
        _candado.EsperaIniciada += (_, _) => esperando.Set();
        var confirmaciones = 0;
        Articulos.AntesDeConfirmar = () =>
        {
            if (Interlocked.Increment(ref confirmaciones) == 1)
            {
                dentroDeLaTransaccion.Set();
                liberar.Wait(TimeSpan.FromSeconds(30));
            }
        };
        var primerLector = LectorDe(Fila(2, "ABC-1", 2420m));
        var segundoLector = LectorDe(Fila(2, "ABC-1", 3000m));
        using var cancelacion = new CancellationTokenSource();

        var primera = Task.Run(() => CrearCasoDeUso(primerLector).EjecutarAsync(Archivo));
        try
        {
            Assert.True(dentroDeLaTransaccion.Wait(TimeSpan.FromSeconds(30)), "La primera carga no llegó a confirmar.");
            var segunda = Task.Run(() => CrearCasoDeUso(segundoLector).EjecutarAsync(Archivo, cancelacion.Token));
            Assert.True(esperando.Wait(TimeSpan.FromSeconds(30)), "La segunda carga no quedó esperando el candado.");
            await cancelacion.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => segunda);
            Assert.Equal(0, segundoLector.Lecturas);
        }
        finally
        {
            liberar.Set();
        }

        Assert.IsType<ResultadoPlanillaCostos.Aplicada>(await primera);
        Assert.Equal(1, Articulos.Operaciones);
        Assert.Equal(2420m, Assert.Single(Articulos.Guardados).PrecioCosto);

        // El turno quedó libre: otra carga entra sin esperar.
        var tercera = await CrearCasoDeUso(LectorDe(Fila(2, "ABC-1", 2500m))).EjecutarAsync(Archivo)
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.IsType<ResultadoPlanillaCostos.Aplicada>(tercera);
    }

    // ---- Auxiliares ----

    private void AfirmarSinTocarLaBase(IReadOnlyList<Articulo> antes)
    {
        AfirmarSinCambios(antes);
        Assert.Equal(0, _almacen.Lecturas);
        Assert.Equal(0, Articulos.Operaciones);
        Assert.Equal(0, Articulos.ListadosParaRecalculo);
        Assert.Equal(0, Articulos.Confirmaciones);
        Assert.Empty(Articulos.CostosActualizadosRegistrados);
        Assert.Empty(_registros.Entradas);
    }

    private void AfirmarSinCambios(IReadOnlyList<Articulo> antes) =>
        Assert.Equal(
            antes.Select(a => (a.Codigo, a.PrecioCosto, a.PrecioVenta, a.Version, a.Activo)).OrderBy(a => a.Codigo),
            Articulos.Guardados.Select(a => (a.Codigo, a.PrecioCosto, a.PrecioVenta, a.Version, a.Activo))
                .OrderBy(a => a.Codigo));

    private LectorFijo LectorDe(params FilaPlanilla[] filas) => new(
        new LecturaPlanilla.Leida(new ContenidoPlanilla(
            new CeldaPlanilla.Texto(PlanillaCostos.EncabezadoCodigo),
            new CeldaPlanilla.Texto(PlanillaCostos.EncabezadoPrecio),
            TieneColumnasDeMas: false,
            filas)),
        Articulos);

    private static FilaPlanilla Fila(int numero, string codigo, decimal precio) =>
        new(numero, new CeldaPlanilla.Texto(codigo), new CeldaPlanilla.Numero(precio));

    /// <summary>Un lector que devuelve siempre la misma lectura y anota cada lectura en los accesos de la unidad.</summary>
    private sealed class LectorFijo(LecturaPlanilla lectura, ArticulosEnMemoria unidad) : ILectorPlanillaCostos
    {
        public const string Acceso = "LeerPlanilla";

        public int Lecturas { get; private set; }

        public LecturaPlanilla Leer(ReadOnlyMemory<byte> archivo)
        {
            Lecturas++;
            unidad.Anotar(Acceso);
            return lectura;
        }
    }
}
