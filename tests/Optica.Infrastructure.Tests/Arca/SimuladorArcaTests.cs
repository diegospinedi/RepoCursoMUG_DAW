using System.Text.Json;
using Microsoft.Extensions.Logging;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>
/// Spec FEAT-007, Block 5 (D-17): el simulador numera por tipo y punto de venta en <c>arca-simulado.json</c> del
/// directorio de datos, devuelve un CAE de 14 dígitos con vencimiento a 10 días y responde según
/// <c>Arca:Simulado:Respuesta</c>. No aplica un plazo propio: <c>no_responder</c> espera la cancelación que recibe (D-32),
/// así que las pruebas cancelan a mano y no duermen.
/// </summary>
public sealed class SimuladorArcaTests : IDisposable
{
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
            await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        var b2 = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        var c1 = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.AutorizarAsync(SolicitudesArca.FacturaC(3), _abierto.Token));
        var b1OtroPunto = Assert.IsType<ResultadoAutorizacion.Autorizada>(
            await simulador.AutorizarAsync(SolicitudesArca.FacturaB(4), _abierto.Token));

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
            await otro.AutorizarAsync(SolicitudesArca.FacturaB(3), _abierto.Token));
        Assert.Equal(3, b3.Numero);

        // Escritura atómica: no quedan temporales en el directorio de datos.
        Assert.Equal([SimuladorArca.NombreArchivo], Directory.GetFiles(_directorio).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Simulador_Rechazar_DevuelveCodigoYDescripcion()
    {
        var simulador = Crear(RespuestaSimulador.Rechazar);

        var resultado = await simulador.AutorizarAsync(SolicitudesArca.FacturaB(), _abierto.Token);

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

        var tarea = simulador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);
        await Task.Yield();

        // Sin plazo propio: espera hasta que el que llama cancela (el plazo de EmitirFactura, D-32).
        Assert.False(tarea.IsCompleted);

        await plazo.CancelAsync();
        var resultado = await tarea.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsType<ResultadoAutorizacion.SinRespuesta>(resultado);
        Assert.False(File.Exists(Archivo));
    }

    [Fact]
    public async Task Simulador_CancelacionAntesDeEmpezar_NoEnviada()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);
        using var plazo = new CancellationTokenSource();
        await plazo.CancelAsync();

        var resultado = await simulador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);

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

        var resultado = await simulador.AutorizarAsync(SolicitudesArca.FacturaB(), _abierto.Token);

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

        var resultado = await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

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
    public async Task Simulador_TokenNoCancelable_NoEnviadaSinColgar(RespuestaSimulador respuesta)
    {
        var simulador = Crear(respuesta);

        var tarea = simulador.AutorizarAsync(SolicitudesArca.FacturaB(), CancellationToken.None);
        await Task.Yield();

        Assert.True(tarea.IsCompleted);
        Assert.Equal(
            "La emisión no tiene un tiempo máximo de espera de ARCA.",
            Assert.IsType<ResultadoAutorizacion.NoEnviada>(await tarea).Motivo);
        Assert.Equal(AutorizadorWsfe.MotivoSinPlazo, SimuladorArca.MotivoSinPlazo);
        Assert.False(File.Exists(Archivo));
    }

    [Fact]
    public async Task Simulador_VeinteConcurrentes_NumerosDel1Al20()
    {
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(
            () => simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), _abierto.Token))));

        var numeros = resultados.Select(r => Assert.IsType<ResultadoAutorizacion.Autorizada>(r).Numero).Order();
        Assert.Equal(Enumerable.Range(1, 20).Select(n => (long)n), numeros);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Archivo));
        Assert.Equal(20, json.RootElement.GetProperty("contadores").GetProperty("B-3").GetInt64());
    }

    [Fact]
    public async Task Simulador_ArchivoDeMasDe64KB_NoEnviadaError()
    {
        // JSON válido, con la forma del contador, pero más grande de lo que puede ser el del simulador.
        var contenido = "{ \"contadores\": { \"B-3\": 1 }, \"relleno\": \"" + new string('x', 64 * 1024) + "\" }";
        await File.WriteAllTextAsync(Archivo, contenido);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error
            && e.Texto.Contains(SimuladorArca.NombreArchivo, StringComparison.Ordinal));
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo));
    }

    [Fact]
    public async Task Simulador_FallaAlGrabarElContador_NoEnviadaErrorSinTemporales()
    {
        // Una carpeta con el nombre del archivo: se lee como "sin contador" y el reemplazo final falla.
        Directory.CreateDirectory(Archivo);
        var simulador = Crear(RespuestaSimulador.Autorizar);

        var resultado = await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

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

        var resultado = await simulador.AutorizarAsync(SolicitudesArca.FacturaB(3), _abierto.Token);

        Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Error
            && e.Texto.Contains("máximo", StringComparison.Ordinal));
        Assert.Equal(contenido, await File.ReadAllTextAsync(Archivo));

        // Otro tipo en el mismo punto de venta sigue numerando.
        var c = await simulador.AutorizarAsync(SolicitudesArca.FacturaC(3), _abierto.Token);
        Assert.Equal(1, Assert.IsType<ResultadoAutorizacion.Autorizada>(c).Numero);
    }

    private SimuladorArca Crear(RespuestaSimulador respuesta) =>
        new(_directorio, respuesta, TimeSpan.FromSeconds(30), _registro.Para<SimuladorArca>());
}
