using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>
/// Spec FEAT-007, Block 5 (D-17) y spec FEAT-008, Block 3 (D-13, D-19): el simulador numera por tipo y punto de venta en
/// <c>arca-simulado.json</c> del directorio de datos, guarda los últimos 50 comprobantes autorizados de cada uno (sin
/// datos del receptor), autoriza solo el número contador + 1 y responde la consulta por número. No aplica un plazo
/// propio: <c>no_responder</c>, <c>autorizar_sin_responder</c> y la consulta <c>no_responder</c> esperan la cancelación
/// que reciben (D-32), así que las pruebas cancelan a mano y no duermen.
/// </summary>
public sealed class SimuladorArcaTests : IDisposable
{
    private const string CaeValido = "12345678901234";

    private readonly string _directorio =
        Path.Combine(Path.GetTempPath(), "optica-pruebas-arca", Guid.NewGuid().ToString("N"));

    private readonly RegistroCapturadoArca _registro = new();

    // Un token que se puede cancelar y nunca se cancela: como el plazo de EmitirFactura sin vencer (ronda 3: sin plazo,
    // el simulador no autoriza, igual que el cliente real).
    private readonly CancellationTokenSource _abierto = new();

    public SimuladorArcaTests()
    {
        Directory.CreateDirectory(_directorio);
    }

    private string Archivo => Path.Combine(_directorio, SimuladorArca.NombreArchivo);

    public void Dispose()
    {
        _abierto.Dispose();
        if (Directory.Exists(_directorio))
        {
            Directory.Delete(_directorio, recursive: true);
        }
    }

    [Fact]
    public async Task Simulador_Autorizar_NumeraConsecutivoPorTipoYPuntoVenta()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var b1 = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        var b2 = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        var c1 = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaC(3), _abierto.Token));
        var b1OtroPunto = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaB(4), _abierto.Token));

        Assert.Equal(1, b1.Numero);
        Assert.Equal(2, b2.Numero);
        Assert.Equal(1, c1.Numero);
        Assert.Equal(1, b1OtroPunto.Numero);
        foreach (var autorizada in new[] { b1, b2, c1, b1OtroPunto })
        {
            Assert.Equal(14, autorizada.Cae.Length);
            Assert.True(autorizada.Cae.All(char.IsAsciiDigit));
            Assert.Equal(SolicitudesArca.Fecha.AddDays(10), autorizada.Vencimiento);
        }

        Assert.Equal(EntornoArca.Simulado, simulador.Entorno);

        // El contador sobrevive a un reinicio: otra instancia sobre el mismo directorio sigue con el 3.
        var otro = Crear(RespuestaSimulador.Autorizar);
        var b3 = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await otro.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        Assert.Equal(3, b3.Numero);

        // Escritura atómica: no quedan temporales en el directorio de datos.
        Assert.Equal([SimuladorArca.NombreArchivo], Directory.GetFiles(_directorio).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Simulador_Rechazar_DevuelveCodigoYDescripcion()
    {
        var simulador = Crear(RespuestaSimulador.Rechazar);

        var resultado = await simulador.EmitirAsync(SolicitudesArca.FacturaB(), _abierto.Token);

        var rechazada = Assert.IsType<ResultadoAutorizacion.Rechazada>(resultado);
        Assert.Equal(10016, rechazada.Codigo);
        Assert.Equal(SimuladorArca.DescripcionRechazo, rechazada.Descripcion);
        Assert.False(string.IsNullOrWhiteSpace(rechazada.Descripcion));
        Assert.False(File.Exists(Archivo)); // un rechazo no consume número
    }

    [Fact]
    public async Task Simulador_NoResponder_TimeoutPorCancelacion()
    {
        var simulador = Crear(RespuestaSimulador.NoResponder);
        using var plazo = new CancellationTokenSource();

        var tarea = simulador.EmitirAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await Task.Yield();

        // Sin plazo propio: espera hasta que el que llama cancela (el plazo de EmitirFactura, D-32).
        Assert.False(tarea.IsCompleted);

        await plazo.CancelAsync();
        var resultado = await tarea.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(resultado);
        Assert.False(File.Exists(Archivo)); // no registra nada
        Assert.IsType<ResultadoConsulta.NoAutorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, _abierto.Token));
    }

    [Fact]
    public async Task Simulador_CancelacionAntesDeEmpezar_NoEnviada()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);
        using var plazo = new CancellationTokenSource();
        await plazo.CancelAsync();

        var resultado = await simulador.EmitirAsync(SolicitudesArca.FacturaB(), plazo.Token);

        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.False(File.Exists(Archivo));
    }

    [Theory]
    [InlineData("{ esto no es JSON CONTENIDO-PRIVADO")]
    [InlineData("""{ "contadores": { "B-3": -1 }, "nota": "CONTENIDO-PRIVADO" }""")]
    [InlineData("""{ "contadores": { "B-3": "CONTENIDO-PRIVADO" } }""")]
    [InlineData("""[ "CONTENIDO-PRIVADO" ]""")]
    public async Task Simulador_ArchivoIlegible_NoEnviadaError(string contenido)
    {
        await File.WriteAllTextAsync(Archivo, contenido);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.EmitirAsync(SolicitudesArca.FacturaB(), _abierto.Token);

        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error
            && e.Texto.Contains(SimuladorArca.NombreArchivo, StringComparison.Ordinal));
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains("CONTENIDO-PRIVADO", StringComparison.Ordinal)
            || (e.Excepcion?.ToString().Contains("CONTENIDO-PRIVADO", StringComparison.Ordinal) ?? false));
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo)); // no se pisa
    }

    [Fact]
    public async Task Simulador_ArchivoValido_ContinuaDesdeElContador()
    {
        await File.WriteAllTextAsync(Archivo, """{ "contadores": { "B-3": 124 } }""");
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

        Assert.Equal(125, Assert.IsType<ResultadoAutorizacion.Autorizada>(resultado).Numero);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Archivo));
        Assert.Equal(125, json.RootElement.GetProperty("contadores").GetProperty("B-3").GetInt64());
    }

    /// <summary>
    /// Ronda 3 (alineado con <see cref="AutorizadorWsfe"/>): con un token que no se puede cancelar no hay plazo, así que
    /// no se "envía" nada: no enviada con el mismo motivo fijo, en cualquier respuesta configurada, sin colgarse y sin
    /// numerar. "Sin respuesta" dejaría una emisión pendiente por un error de programación.
    /// </summary>
    [Theory]
    [InlineData(RespuestaSimulador.Autorizar)]
    [InlineData(RespuestaSimulador.Rechazar)]
    [InlineData(RespuestaSimulador.NoResponder)]
    [InlineData(RespuestaSimulador.AutorizarSinResponder)]
    [InlineData(RespuestaSimulador.NoResponderUnaVez)]
    public async Task Simulador_TokenNoCancelable_NoEnviadaSinColgar(RespuestaSimulador respuesta)
    {
        var simulador = Crear(respuesta);

        var tarea = simulador.EmitirAsync(SolicitudesArca.FacturaB(), CancellationToken.None);
        await Task.Yield();

        Assert.True(tarea.IsCompleted);
        Assert.Equal(
            "La emisión no tiene un tiempo máximo de espera de ARCA.",
            Assert.IsType<ResultadoAutorizacion.NoEnviada>(await tarea).Motivo);
        Assert.Equal(AutorizadorWsfe.MotivoSinPlazo, SimuladorArca.MotivoSinPlazo);
        Assert.False(File.Exists(Archivo));
    }

    /// <summary>
    /// Con el número del puerto (ADR-025) el simulador no elige el número: veinte autorizaciones simultáneas del mismo
    /// número 1 (sin el candado de la emisión) autorizan una sola vez y rechazan las demás con 10016.
    /// </summary>
    [Fact]
    public async Task Simulador_VeinteConcurrentesConElMismoNumero_UnaSolaAutorizada()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(
            () => simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), 1, _abierto.Token))));

        Assert.Single(resultados.OfType<ResultadoAutorizacion.Autorizada>());
        Assert.Equal(19, resultados.OfType<ResultadoAutorizacion.Rechazada>().Count(r => r.Codigo == 10016));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Archivo));
        Assert.Equal(1, json.RootElement.GetProperty("contadores").GetProperty("B-3").GetInt64());
        Assert.Single(json.RootElement.GetProperty("comprobantes").GetProperty("B-3").EnumerateArray());
    }

    [Fact]
    public async Task Simulador_ArchivoDeMasDe256KB_NoEnviadaError()
    {
        // Con la forma exacta del archivo (sin propiedades desconocidas), inflado con espacios: solo lo rechaza el
        // control de tamaño, no la validación de la forma (D-13).
        var contenido = "{ \"contadores\": { \"B-3\": 1 }" + new string(' ', 256 * 1024) + "}";
        using (var valido = JsonDocument.Parse(contenido))
        {
            Assert.Equal(1, valido.RootElement.GetProperty("contadores").GetProperty("B-3").GetInt64());
        }

        await File.WriteAllTextAsync(Archivo, contenido);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error
            && e.Texto.Contains(SimuladorArca.NombreArchivo, StringComparison.Ordinal));
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo));
    }

    /// <summary>
    /// D-13: si grabar el comprobante dejaría el archivo por encima de 256 KB, no se graba (no se podría volver a leer)
    /// y la autorización es "no enviada", con un error propio en el log que nombra el archivo y no su ruta.
    /// </summary>
    [Fact]
    public async Task Simulador_GrabarSuperariaElLimite_NoEnviadaSinGrabar()
    {
        var contenido = ContenidoCercaDelLimite(margen: 60);
        Assert.Equal(SimuladorArca.TamanoMaximoArchivo - 60, Encoding.UTF8.GetByteCount(contenido));
        await File.WriteAllTextAsync(Archivo, contenido);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        // El archivo se lee (está dentro del límite), pero el comprobante nuevo de C-1 no entra.
        Assert.Equal(1, Assert.IsType<ResultadoProximoNumero.Numero>(
            await simulador.ProximoNumeroAsync(TipoComprobante.FacturaC, 1, _abierto.Token)).Valor);
        var resultado = await simulador.AutorizarAsync(SolicitudesArca.FacturaC(1), 1, _abierto.Token);

        Assert.Equal(SimuladorArca.MotivoContador, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo));
        Assert.Equal([SimuladorArca.NombreArchivo], Directory.GetFiles(_directorio).Select(Path.GetFileName));
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error
            && e.Texto.Contains(SimuladorArca.NombreArchivo, StringComparison.Ordinal)
            && e.Texto.Contains("tamaño máximo", StringComparison.Ordinal));
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains(_directorio, StringComparison.Ordinal));
        Assert.IsType<ResultadoConsulta.NoAutorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaC, 1, 1, _abierto.Token));
    }

    /// <summary>
    /// Un archivo válido, compacto como lo graba el simulador, de exactamente <c>256 KB - margen</c>: listas llenas de B-1
    /// en adelante, ajustado byte a byte con la cantidad de dígitos de los totales.
    /// </summary>
    private static string ContenidoCercaDelLimite(int margen)
    {
        var objetivo = (int)SimuladorArca.TamanoMaximoArchivo - margen;
        var contadores = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var comprobantes = new SortedDictionary<string, List<ComprobanteArchivo>>(StringComparer.Ordinal);
        string Serializar() => JsonSerializer.Serialize(new { contadores, comprobantes });

        for (var puntoVenta = 1; ; puntoVenta++)
        {
            var clave = $"B-{puntoVenta}";
            contadores[clave] = 50;
            comprobantes[clave] = Enumerable.Range(1, 50)
                .Select(n => new ComprobanteArchivo(n, 1, CaeValido, "2026-11-09", "2026-10-30")).ToList();
            if (Encoding.UTF8.GetByteCount(Serializar()) > objetivo)
            {
                // La última lista llena no entra: se saca y el resto se completa con dígitos.
                contadores.Remove(clave);
                comprobantes.Remove(clave);
                break;
            }
        }

        var faltan = objetivo - Encoding.UTF8.GetByteCount(Serializar());
        Assert.InRange(faltan, 0, 10_000);
        foreach (var lista in comprobantes.Values)
        {
            for (var i = 0; i < lista.Count && faltan > 0; i++)
            {
                var digitos = Math.Min(faltan, 17);
                lista[i] = lista[i] with { totalCentavos = long.Parse("1" + new string('0', digitos)) };
                faltan -= digitos;
            }
        }

        return Serializar();
    }

    private sealed record ComprobanteArchivo(
        long numero, long totalCentavos, string cae, string vencimiento, string fecha);

    /// <summary>D-13: el límite subió de 64 KB a 256 KB; un archivo válido de más de 64 KB se lee.</summary>
    [Fact]
    public async Task Simulador_ArchivoValidoDeMasDe64KB_SeLee()
    {
        var contadores = new Dictionary<string, long>();
        var comprobantes = new Dictionary<string, object[]>();
        for (var puntoVenta = 1; puntoVenta <= 20; puntoVenta++)
        {
            contadores[$"B-{puntoVenta}"] = 50;
            comprobantes[$"B-{puntoVenta}"] = Enumerable.Range(1, 50).Select(n => Comprobante(n)).ToArray();
        }

        var contenido = JsonSerializer.Serialize(new { contadores, comprobantes });
        Assert.InRange(Encoding.UTF8.GetByteCount(contenido), 64 * 1024 + 1, 256 * 1024);
        await File.WriteAllTextAsync(Archivo, contenido);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var proximo = await simulador.ProximoNumeroAsync(TipoComprobante.FacturaB, 7, _abierto.Token);

        Assert.Equal(51, Assert.IsType<ResultadoProximoNumero.Numero>(proximo).Valor);
    }

    [Fact]
    public async Task Simulador_FallaAlGrabarElContador_NoEnviadaErrorSinTemporales()
    {
        // Una carpeta con el nombre del archivo: se lee como "sin contador" y el reemplazo final falla.
        Directory.CreateDirectory(Archivo);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error
            && e.Texto.Contains(SimuladorArca.NombreArchivo, StringComparison.Ordinal)
            && e.Texto.Contains("grabar", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(_directorio));
    }

    [Fact]
    public async Task Simulador_ContadorEnElMaximo_NoEnviadaError()
    {
        var contenido = """{ "contadores": { "B-3": 99999999 } }""";
        await File.WriteAllTextAsync(Archivo, contenido);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error
            && e.Texto.Contains("máximo", StringComparison.Ordinal));
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo));

        // Otro tipo en el mismo punto de venta sigue numerando.
        var c = await simulador.EmitirAsync(SolicitudesArca.FacturaC(3), _abierto.Token);
        Assert.Equal(1, Assert.IsType<ResultadoAutorizacion.Autorizada>(c).Numero);
    }

    // ---- Spec FEAT-008, Block 3 ----

    /// <summary>
    /// AC-05 (y el INFO del auditor): la consulta encuentra el comprobante que el mismo simulador autorizó, con el total
    /// de la solicitud, el CAE y el vencimiento que devolvió la autorización y la fecha del comprobante; también desde
    /// otra instancia (el comprobante quedó en el archivo, no en memoria).
    /// </summary>
    [Fact]
    public async Task Simulador_AutorizaYConsulta_MismoTotal()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);
        var b = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        var c = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaC(3), _abierto.Token));

        foreach (var instancia in new[] { simulador, Crear(RespuestaSimulador.Autorizar) })
        {
            var consultaB = Assert.IsType<ResultadoConsulta.Autorizado>(
                await instancia.ConsultarAsync(TipoComprobante.FacturaB, 3, b.Numero, _abierto.Token));
            Assert.Equal(1210m, consultaB.Total);
            Assert.Equal(b.Cae, consultaB.Cae);
            Assert.Equal(b.Vencimiento, consultaB.Vencimiento);
            Assert.Equal(SolicitudesArca.Fecha, consultaB.Fecha);

            var consultaC = Assert.IsType<ResultadoConsulta.Autorizado>(
                await instancia.ConsultarAsync(TipoComprobante.FacturaC, 3, c.Numero, _abierto.Token));
            Assert.Equal(1000m, consultaC.Total);
            Assert.Equal(c.Cae, consultaC.Cae);
        }
    }

    [Fact]
    public async Task Simulador_ConsultaNumeroNoAutorizado_NoAutorizado()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);

        // Sin archivo: nada autorizado.
        Assert.IsType<ResultadoConsulta.NoAutorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, _abierto.Token));

        await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

        // El autorizado sí figura (la prueba distingue "no lo tiene" de "nunca encuentra nada").
        Assert.IsType<ResultadoConsulta.Autorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, _abierto.Token));

        // El siguiente número, otro tipo y otro punto de venta: no autorizados.
        Assert.IsType<ResultadoConsulta.NoAutorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 2, _abierto.Token));
        Assert.IsType<ResultadoConsulta.NoAutorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaC, 3, 1, _abierto.Token));
        Assert.IsType<ResultadoConsulta.NoAutorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 4, 1, _abierto.Token));
    }

    /// <summary>
    /// AC-05: <c>autorizar_sin_responder</c> registra el comprobante y espera la cancelación (sin respuesta); mientras
    /// espera no retiene el candado, y después la consulta lo encuentra autorizado.
    /// </summary>
    [Fact]
    public async Task Simulador_AutorizarSinResponder_RegistraYSinRespuesta()
    {
        var simulador = Crear(RespuestaSimulador.AutorizarSinResponder);
        using var plazo = new CancellationTokenSource();

        var tarea = simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), 1, plazo.Token);
        await Task.Yield();
        Assert.False(tarea.IsCompleted);

        // Ya registrado y sin el candado tomado: el próximo número es el 2 mientras la autorización sigue esperando.
        var proximo = await simulador.ProximoNumeroAsync(TipoComprobante.FacturaB, 3, _abierto.Token)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, Assert.IsType<ResultadoProximoNumero.Numero>(proximo).Valor);

        await plazo.CancelAsync();
        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(await tarea.WaitAsync(TimeSpan.FromSeconds(10)));

        var consulta = Assert.IsType<ResultadoConsulta.Autorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, _abierto.Token));
        Assert.Equal(1210m, consulta.Total);
        Assert.Equal(SolicitudesArca.Fecha.AddDays(SimuladorArca.DiasVencimientoCae), consulta.Vencimiento);
        Assert.Equal(SolicitudesArca.Fecha, consulta.Fecha);
    }

    /// <summary>
    /// AC-06 de punta a punta: <c>no_responder_una_vez</c> no responde la primera solicitud del proceso (no registra) y
    /// autoriza las siguientes; la consulta del número de la primera, antes de la segunda, es "no autorizado".
    /// </summary>
    [Fact]
    public async Task Simulador_NoResponderUnaVez_SegundaAutoriza()
    {
        var simulador = Crear(RespuestaSimulador.NoResponderUnaVez);
        using var plazo = new CancellationTokenSource();

        var primera = simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), 1, plazo.Token);
        await Task.Yield();
        Assert.False(primera.IsCompleted);
        await plazo.CancelAsync();
        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(await primera.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(File.Exists(Archivo));
        Assert.IsType<ResultadoConsulta.NoAutorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, _abierto.Token));

        var segunda = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        var tercera = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token));

        Assert.Equal(1, segunda.Numero);
        Assert.Equal(2, tercera.Numero);
        Assert.IsType<ResultadoConsulta.Autorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, _abierto.Token));
    }

    /// <summary>AC-11: con <c>Arca:Simulado:Consulta = no_responder</c> la consulta espera la cancelación.</summary>
    [Fact]
    public async Task Simulador_ConsultaNoResponder_SinRespuesta()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar, ConsultaSimulador.NoResponder);
        Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        using var plazo = new CancellationTokenSource();

        var tarea = simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, plazo.Token);
        await Task.Yield();
        Assert.False(tarea.IsCompleted);

        await plazo.CancelAsync();

        Assert.IsType<ResultadoConsulta.SinRespuesta>(await tarea.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(ConsultaSimulador.Responder)]
    [InlineData(ConsultaSimulador.NoResponder)]
    public async Task Simulador_ConsultaSinPlazoOCancelada_NoConsultadaSinColgar(ConsultaSimulador consulta)
    {
        var simulador = Crear(RespuestaSimulador.Autorizar, consulta);

        var sinPlazo = simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, CancellationToken.None);
        await Task.Yield();
        Assert.True(sinPlazo.IsCompleted);
        Assert.Equal(SimuladorArca.MotivoSinPlazo, Assert.IsType<ResultadoConsulta.NoConsultada>(await sinPlazo).Motivo);

        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();
        Assert.IsType<ResultadoConsulta.NoConsultada>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, cancelado.Token));
    }

    [Fact]
    public async Task Simulador_NumeroIncorrecto_Rechazado()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);

        // Saltea un número: 10016 y no registra nada.
        var salteado = Assert.IsType<ResultadoAutorizacion.Rechazada>(
            await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), 2, _abierto.Token));
        Assert.Equal(SimuladorArca.CodigoRechazo, salteado.Codigo);
        Assert.Equal(SimuladorArca.DescripcionRechazo, salteado.Descripcion);
        Assert.False(File.Exists(Archivo));

        Assert.Equal(1, Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), 1, _abierto.Token)).Numero);
        var contenido = await File.ReadAllTextAsync(Archivo);

        // Repite un número ya autorizado: 10016, y el archivo no cambia.
        Assert.Equal(10016, Assert.IsType<ResultadoAutorizacion.Rechazada>(
            await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), 1, _abierto.Token)).Codigo);
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo));
    }

    /// <summary>Validación estricta del archivo (spec, Block 3): cualquier forma inválida es "no consultada".</summary>
    [Theory]
    [InlineData("{ esto no es JSON CONTENIDO-PRIVADO")]
    [InlineData("""[ "CONTENIDO-PRIVADO" ]""")]
    [InlineData("""{ "contadores": { "B-3": -1 } }""")]
    [InlineData("""{ "contadores": { "B-3": 1.5 } }""")]
    [InlineData("""{ "contadores": { "B-3": null } }""")]
    [InlineData("""{ "contadores": { "X-3": 1 } }""")]
    [InlineData("""{ "contadores": { "B-0": 1 } }""")]
    [InlineData("""{ "contadores": { "B-100000": 1 } }""")]
    [InlineData("""{ "contadores": { "B-03": 1 } }""")]
    [InlineData("""{ "contadores": { "B-3": 1, "B-3": 2 } }""")]
    [InlineData("""{ "contadores": [] }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "nota": "CONTENIDO-PRIVADO" }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": [] }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": {} } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "X-3": [] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [], "B-3": [] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ 1 ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 0, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": "1", "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 2, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 2 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" }, { "numero": 1, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": -1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1.5, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": null, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "1234567890123", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "1234567890123A", "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": 12345678901234, "vencimiento": "2026-11-24", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-4", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "24/11/2026", "fecha": "2026-11-14" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": null } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14", "dni": "CONTENIDO-PRIVADO" } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": "2026-11-14", "numero": 1 } ] } }""")]
    [InlineData("""{ "contadores": { "B-3": 1 }, "comprobantes": { "B-3": [ { "numero": 1, "totalCentavos": 1, "cae": "12345678901234", "vencimiento": "2026-11-24", "fecha": { "x": [ 1 ] } } ] } }""")]
    public async Task Simulador_ArchivoInvalido_NoConsultada(string contenido)
    {
        await File.WriteAllTextAsync(Archivo, contenido);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 1, _abierto.Token);

        Assert.Equal(SimuladorArca.MotivoContador, Assert.IsType<ResultadoConsulta.NoConsultada>(resultado).Motivo);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error
            && e.Texto.Contains(SimuladorArca.NombreArchivo, StringComparison.Ordinal));

        // WARN-2: borrar el archivo haría que el simulador "olvide" comprobantes autorizados y una pendiente se vuelva a
        // emitir; el log no lo recomienda.
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error && e.Texto.Contains(
            "no lo borres si hay emisiones pendientes de verificación", StringComparison.Ordinal));
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains("borralo", StringComparison.Ordinal));
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains("CONTENIDO-PRIVADO", StringComparison.Ordinal)
            || (e.Excepcion?.ToString().Contains("CONTENIDO-PRIVADO", StringComparison.Ordinal) ?? false));
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo));

        // La misma forma inválida tampoco deja autorizar ni pedir el próximo número.
        Assert.IsType<ResultadoProximoNumero.NoEnviada>(
            await simulador.ProximoNumeroAsync(TipoComprobante.FacturaB, 3, _abierto.Token));
        Assert.IsType<ResultadoAutorizacion.NoEnviada>(
            await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), 2, _abierto.Token));
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo));
    }

    [Fact]
    public async Task Simulador_ListaDeMasDe50_NoConsultada()
    {
        var contenido = JsonSerializer.Serialize(new
        {
            contadores = new Dictionary<string, long> { ["B-3"] = 51 },
            comprobantes = new Dictionary<string, object[]>
            {
                ["B-3"] = Enumerable.Range(1, 51).Select(n => Comprobante(n)).ToArray(),
            },
        });
        await File.WriteAllTextAsync(Archivo, contenido);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 51, _abierto.Token);

        Assert.IsType<ResultadoConsulta.NoConsultada>(resultado);
    }

    /// <summary>R-05 (D-13): el archivo conserva solo los últimos 50 comprobantes de cada tipo y punto de venta.</summary>
    [Fact]
    public async Task Simulador_CrecimientoMasDe50_ConservaUltimos50YNoSuperaLimite()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);
        for (var i = 0; i < 60; i++)
        {
            Assert.IsType<ResultadoAutorizacion.Autorizada>(
                await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        }

        var tamano = new FileInfo(Archivo).Length;
        using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(Archivo)))
        {
            Assert.Equal(60, json.RootElement.GetProperty("contadores").GetProperty("B-3").GetInt64());
            var numeros = json.RootElement.GetProperty("comprobantes").GetProperty("B-3").EnumerateArray()
                .Select(c => c.GetProperty("numero").GetInt64()).Order();
            Assert.Equal(Enumerable.Range(11, 50).Select(n => (long)n), numeros);
        }

        // Diez más no hacen crecer el archivo de forma apreciable: la lista sigue en 50.
        for (var i = 0; i < 10; i++)
        {
            await simulador.EmitirAsync(SolicitudesArca.FacturaB(3), _abierto.Token);
        }

        Assert.InRange(new FileInfo(Archivo).Length, 1, tamano + 64);
        Assert.True(new FileInfo(Archivo).Length <= 256 * 1024);

        // El último se encuentra; el más viejo descartado ya no tiene datos (supuesto: "sin respuesta", no "no
        // autorizado", porque ARCA sí lo autorizó).
        Assert.IsType<ResultadoConsulta.Autorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 70, _abierto.Token));
        Assert.IsType<ResultadoConsulta.SinRespuesta>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 20, _abierto.Token));
        Assert.IsType<ResultadoConsulta.Autorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 21, _abierto.Token));
    }

    /// <summary>Compatibilidad: el archivo de FEAT-007 (solo contadores) se lee y se amplía al autorizar.</summary>
    [Fact]
    public async Task Simulador_ArchivoDeFeat007_SeLee()
    {
        await File.WriteAllTextAsync(Archivo, """{ "contadores": { "B-3": 124, "C-1": 7 } }""");
        var simulador = Crear(RespuestaSimulador.Autorizar);

        Assert.Equal(125, Assert.IsType<ResultadoProximoNumero.Numero>(
            await simulador.ProximoNumeroAsync(TipoComprobante.FacturaB, 3, _abierto.Token)).Valor);
        Assert.IsType<ResultadoConsulta.NoAutorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 125, _abierto.Token));

        var autorizada = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), 125, _abierto.Token));

        Assert.Equal(125, autorizada.Numero);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Archivo));
        Assert.Equal(125, json.RootElement.GetProperty("contadores").GetProperty("B-3").GetInt64());
        Assert.Equal(7, json.RootElement.GetProperty("contadores").GetProperty("C-1").GetInt64());
        Assert.IsType<ResultadoConsulta.Autorizado>(
            await simulador.ConsultarAsync(TipoComprobante.FacturaB, 3, 125, _abierto.Token));
    }

    /// <summary>Completion criterion: el archivo nunca guarda datos del receptor.</summary>
    [Fact]
    public async Task Simulador_Archivo_SinDatosDelReceptor()
    {
        var conDni = new SolicitudComprobante(
            TipoComprobante.FacturaB,
            3,
            SolicitudesArca.Fecha,
            ConceptoFactura.Productos,
            ReceptorFactura.ConDni(EscenarioArca.Dni),
            ImportesFactura.ParaFacturaB(1815m, 21m));
        var simulador = Crear(RespuestaSimulador.AutorizarSinResponder);
        using var plazo = new CancellationTokenSource();

        var tarea = simulador.AutorizarAsync(conDni, 1, plazo.Token);
        await plazo.CancelAsync();
        await tarea.WaitAsync(TimeSpan.FromSeconds(10));

        var contenido = await File.ReadAllTextAsync(Archivo);
        Assert.DoesNotContain(EscenarioArca.Dni, contenido, StringComparison.Ordinal);
        Assert.DoesNotContain(EscenarioArca.Dni.TrimStart('0'), contenido, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(contenido);
        Assert.Equal(["comprobantes", "contadores"], json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        var comprobante = Assert.Single(json.RootElement.GetProperty("comprobantes").GetProperty("B-3").EnumerateArray());
        Assert.Equal(
            ["cae", "fecha", "numero", "totalCentavos", "vencimiento"],
            comprobante.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(181500, comprobante.GetProperty("totalCentavos").GetInt64());
        Assert.Equal("2026-10-30", comprobante.GetProperty("fecha").GetString());
        Assert.Equal("2026-11-09", comprobante.GetProperty("vencimiento").GetString());
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains(EscenarioArca.Dni.TrimStart('0'), StringComparison.Ordinal));
    }

    private static object Comprobante(long numero) => new
    {
        numero,
        totalCentavos = 121000L,
        cae = CaeValido,
        vencimiento = "2026-11-09",
        fecha = "2026-10-30",
    };

    private SimuladorArca Crear(RespuestaSimulador respuesta, ConsultaSimulador consulta = ConsultaSimulador.Responder) =>
        new(_directorio, respuesta, consulta, TimeSpan.FromSeconds(30), _registro.Para<SimuladorArca>());
}
