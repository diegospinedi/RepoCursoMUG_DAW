using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>
/// Spec FEAT-007, Block 5: <see cref="OpcionesArca"/> lee y valida las claves <c>Arca:*</c>; sin <c>Arca:Entorno</c> el
/// entorno es el simulado (R-14, ADR-022); un valor inválido frena el arranque con un crítico que nombra la clave y no
/// el valor; homologación o producción sin credenciales arrancan y cada emisión termina "no enviada".
/// </summary>
public sealed class OpcionesArcaTests : IDisposable
{
    /// <summary>CUIT ficticio con dígito verificador válido (DNI 0: no es de una persona ni de una empresa real).</summary>
    private const string CuitValido = "20000000001";

    private readonly string _directorio =
        Path.Combine(Path.GetTempPath(), "optica-pruebas-arca", Guid.NewGuid().ToString("N"));

    private readonly RegistroCapturadoArca _registro = new();

    public OpcionesArcaTests()
    {
        Directory.CreateDirectory(_directorio);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directorio))
        {
            Directory.Delete(_directorio, recursive: true);
        }
    }

    [Fact]
    public void Opciones_SinEntorno_Simulado()
    {
        // Sin ninguna clave Arca: simulado, 30 s y autorizar (R-14). Vacío cuenta como ausente (appsettings con "").
        foreach (var configuracion in new[] { Configuracion(), Configuracion((OpcionesArca.ClaveEntorno, "")) })
        {
            var opciones = OpcionesArca.Leer(configuracion);

            Assert.Equal(EntornoArca.Simulado, opciones.Entorno);
            Assert.Equal(TimeSpan.FromSeconds(30), opciones.TiempoMaximo);
            Assert.Equal(RespuestaSimulador.Autorizar, opciones.RespuestaSimulado);
        }

        // Y la raíz de composición registra el simulador.
        using var servicios = Servicios(Configuracion());
        var autorizador = servicios.GetRequiredService<IAutorizadorComprobantes>();
        Assert.IsType<SimuladorArca>(autorizador);
        Assert.Equal(EntornoArca.Simulado, autorizador.Entorno);
        Assert.Equal(TimeSpan.FromSeconds(30), autorizador.TiempoMaximo);
    }

    [Theory]
    [InlineData("Simulado", EntornoArca.Simulado)]
    [InlineData("simulado", EntornoArca.Simulado)]
    [InlineData("HOMOLOGACION", EntornoArca.Homologacion)]
    [InlineData("homologacion", EntornoArca.Homologacion)]
    [InlineData("Produccion", EntornoArca.Produccion)]
    public void Opciones_Entorno_SinDistinguirMayusculas(string valor, EntornoArca esperado)
    {
        var opciones = OpcionesArca.Leer(Configuracion((OpcionesArca.ClaveEntorno, valor)));

        Assert.Equal(esperado, opciones.Entorno);
    }

    [Theory]
    [InlineData("Producción")]
    [InlineData("Produccion2")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("Simulado,Produccion")]
    public void Opciones_EntornoInvalido_Rechazado(string valor)
    {
        var error = Assert.Throws<OpcionesArcaInvalidasException>(
            () => OpcionesArca.Leer(Configuracion((OpcionesArca.ClaveEntorno, valor))));

        Assert.Equal(OpcionesArca.ClaveEntorno, error.Clave);
        Assert.Contains(OpcionesArca.ClaveEntorno, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(valor, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("121")]
    [InlineData("-1")]
    [InlineData("30.5")]
    [InlineData("treinta")]
    public void Opciones_TiempoMaximoInvalido_Rechazado(string valor)
    {
        var error = Assert.Throws<OpcionesArcaInvalidasException>(
            () => OpcionesArca.Leer(Configuracion((OpcionesArca.ClaveTiempoMaximo, valor))));

        Assert.Equal(OpcionesArca.ClaveTiempoMaximo, error.Clave);
        Assert.Contains(OpcionesArca.ClaveTiempoMaximo, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("120", 120)]
    [InlineData("5", 5)]
    public void Opciones_TiempoMaximoEnRango_Aceptado(string valor, int segundos)
    {
        var opciones = OpcionesArca.Leer(Configuracion((OpcionesArca.ClaveTiempoMaximo, valor)));

        Assert.Equal(TimeSpan.FromSeconds(segundos), opciones.TiempoMaximo);
    }

    [Theory]
    [InlineData("aceptar")]
    [InlineData("no responder")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void Opciones_RespuestaSimuladorInvalida_Rechazado(string valor)
    {
        var error = Assert.Throws<OpcionesArcaInvalidasException>(
            () => OpcionesArca.Leer(Configuracion((OpcionesArca.ClaveRespuestaSimulado, valor))));

        Assert.Equal(OpcionesArca.ClaveRespuestaSimulado, error.Clave);
    }

    [Theory]
    [InlineData("autorizar", RespuestaSimulador.Autorizar)]
    [InlineData("rechazar", RespuestaSimulador.Rechazar)]
    [InlineData("no_responder", RespuestaSimulador.NoResponder)]
    [InlineData("RECHAZAR", RespuestaSimulador.Rechazar)]
    [InlineData("No_Responder", RespuestaSimulador.NoResponder)]
    [InlineData("Autorizar", RespuestaSimulador.Autorizar)]
    public void Opciones_RespuestaSimulador_Aceptada(string valor, RespuestaSimulador esperada)
    {
        var opciones = OpcionesArca.Leer(Configuracion((OpcionesArca.ClaveRespuestaSimulado, valor)));

        Assert.Equal(esperada, opciones.RespuestaSimulado);
    }

    [Theory]
    [InlineData("20000000002")] // dígito verificador incorrecto
    [InlineData("2000000000")] // 10 dígitos
    [InlineData("200000000011")] // 12 dígitos
    [InlineData("20-00000000-1")] // con guiones
    [InlineData("2O000000001")] // una letra O
    public void Opciones_CuitInvalido_Rechazado(string cuit)
    {
        var error = Assert.Throws<OpcionesArcaInvalidasException>(() => OpcionesArca.Leer(Configuracion(
            (OpcionesArca.ClaveEntorno, "Homologacion"),
            (OpcionesArca.ClaveCuit, cuit))));

        Assert.Equal(OpcionesArca.ClaveCuit, error.Clave);
        Assert.DoesNotContain(cuit, error.Message, StringComparison.Ordinal);

        // En simulado el CUIT no se usa y no se valida.
        var simulado = OpcionesArca.Leer(Configuracion((OpcionesArca.ClaveCuit, cuit)));
        Assert.Equal(EntornoArca.Simulado, simulado.Entorno);
    }

    [Fact]
    public void Opciones_CuitValido_CredencialesCompletas()
    {
        var opciones = OpcionesArca.Leer(Configuracion(
            (OpcionesArca.ClaveEntorno, "Homologacion"),
            (OpcionesArca.ClaveCuit, CuitValido),
            (OpcionesArca.ClaveCertificadoRuta, Path.Combine(_directorio, "homologacion.pfx")),
            (OpcionesArca.ClaveCertificadoClave, "clave-de-prueba")));

        Assert.Equal(EntornoArca.Homologacion, opciones.Entorno);
        Assert.Equal(CuitValido, opciones.Cuit);
        Assert.True(opciones.CredencialesCompletas);
    }

    [Fact]
    public async Task Opciones_HomologacionSinCertificado_NoEnviada()
    {
        var configuracion = Configuracion(
            (OpcionesArca.ClaveEntorno, "Homologacion"),
            (OpcionesArca.ClaveCuit, CuitValido));
        Assert.False(OpcionesArca.Leer(configuracion).CredencialesCompletas);

        using var servicios = Servicios(configuracion);
        servicios.ValidarArca(); // arranca: no lanza
        var autorizador = servicios.GetRequiredService<IAutorizadorComprobantes>();

        var resultado = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), CancellationToken.None);

        Assert.Equal(EntornoArca.Homologacion, autorizador.Entorno);
        var noEnviada = Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado);
        Assert.Equal("La facturación con ARCA no está configurada.", noEnviada.Motivo);
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Warning
            && e.Texto.Contains(OpcionesArca.ClaveCertificadoRuta, StringComparison.Ordinal));
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains(CuitValido, StringComparison.Ordinal));
    }

    [Fact]
    public void Arranque_Simulado_AdvierteEnElLog()
    {
        using var servicios = Servicios(Configuracion());

        servicios.ValidarArca();

        // R-10: una instalación que factura en simulado tiene que poder notarlo.
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Warning
            && e.Texto.Contains("simulado", StringComparison.OrdinalIgnoreCase)
            && e.Texto.Contains(OpcionesArca.ClaveEntorno, StringComparison.Ordinal));
    }

    [Fact]
    public void Arranque_ValorInvalido_CriticoConLaClaveSinElValorYNoArranca()
    {
        const string valor = "valor-que-no-debe-aparecer";
        using var servicios = Servicios(Configuracion((OpcionesArca.ClaveEntorno, valor)));

        var error = Assert.Throws<OpcionesArcaInvalidasException>(servicios.ValidarArca);

        Assert.Equal(OpcionesArca.ClaveEntorno, error.Clave);
        var critico = Assert.Single(_registro.Entradas, e => e.Nivel == LogLevel.Critical);
        Assert.Contains(OpcionesArca.ClaveEntorno, critico.Texto, StringComparison.Ordinal);
        Assert.Null(critico.Excepcion);
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains(valor, StringComparison.Ordinal));
    }

    [Fact]
    public void Arranque_ConCredenciales_NingunValorEnElLog()
    {
        const string clave = "clave-del-pfx-que-no-debe-aparecer";
        var ruta = Path.Combine(_directorio, "produccion.pfx");
        using var servicios = Servicios(Configuracion(
            (OpcionesArca.ClaveEntorno, "Homologacion"),
            (OpcionesArca.ClaveCuit, CuitValido),
            (OpcionesArca.ClaveCertificadoRuta, ruta),
            (OpcionesArca.ClaveCertificadoClave, clave)));

        servicios.ValidarArca();

        // Algo se registró al arrancar (el entorno elegido), y ningún valor de las credenciales.
        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Warning
            && e.Texto.Contains(OpcionesArca.ClaveEntorno, StringComparison.Ordinal));
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains(clave, StringComparison.Ordinal)
            || e.Texto.Contains(ruta, StringComparison.Ordinal)
            || e.Texto.Contains(CuitValido, StringComparison.Ordinal));
    }

    /// <summary>
    /// Block 6 (cambiada a propósito, D-54): con credenciales completas, homologación y producción usan el cliente de
    /// WSAA y WSFEv1, con el protector de datos que registra la Api. Ya no existe el motivo "todavía no está disponible".
    /// </summary>
    /// <remarks>
    /// Sin red, sin depender del orden de los controles del autorizador (ronda 2): el <see cref="HttpClient"/> propio de
    /// ARCA se reemplaza en el contenedor por uno con <see cref="ManejadorSinRed"/>, que cuenta y rechaza todo intento.
    /// La emisión corre con un plazo abierto y el PFX no existe: termina "no enviada" por el certificado, y aunque el
    /// orden cambiara, ningún pedido podría salir del proceso.
    /// </remarks>
    [Theory]
    [InlineData("Homologacion", EntornoArca.Homologacion)]
    [InlineData("Produccion", EntornoArca.Produccion)]
    public async Task Arranque_ConCredenciales_AutorizadorWsfe(string texto, EntornoArca entorno)
    {
        var manejador = new ManejadorSinRed();
        using var clienteSinRed = new HttpClient(manejador);
        using var servicios = new ServiceCollection()
            .AddLogging(registro => registro.AddProvider(_registro).SetMinimumLevel(LogLevel.Trace))
            .AddSingleton<Optica.Application.Comun.IProtectorDatos, ProtectorDePrueba>()
            .AgregarInfraestructura(Configuracion(
                (OpcionesArca.ClaveEntorno, texto),
                (OpcionesArca.ClaveCuit, CuitValido),
                (OpcionesArca.ClaveCertificadoRuta, Path.Combine(_directorio, "homologacion.pfx")),
                (OpcionesArca.ClaveCertificadoClave, "clave-de-prueba")))
            .AddKeyedSingleton(ConfiguracionInfraestructura.ClaveClienteHttpArca, clienteSinRed)
            .BuildServiceProvider();
        servicios.ValidarArca();

        var autorizador = servicios.GetRequiredService<IAutorizadorComprobantes>();
        using var plazo = new CancellationTokenSource();
        var resultado = await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), plazo.Token);

        Assert.Same(clienteSinRed, servicios.GetRequiredKeyedService<HttpClient>(ConfiguracionInfraestructura.ClaveClienteHttpArca));
        Assert.IsType<AutorizadorWsfe>(autorizador);
        Assert.Same(autorizador, servicios.GetRequiredService<IAutorizadorComprobantes>());
        Assert.Equal(entorno, autorizador.Entorno);
        Assert.Equal(TimeSpan.FromSeconds(30), autorizador.TiempoMaximo);
        Assert.Equal(
            AutorizadorWsfe.MotivoCertificado, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Equal(0, manejador.Intentos);
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains("todavía no está disponible", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_directorio, $"arca-ta-{texto.ToLowerInvariant()}.json")));
    }

    /// <summary>Ronda 2: facturar en producción también queda advertido en el log al arrancar, sin valores.</summary>
    [Fact]
    public void Arranque_Produccion_AdvierteEnElLog()
    {
        using var servicios = Servicios(Configuracion(
            (OpcionesArca.ClaveEntorno, "Produccion"),
            (OpcionesArca.ClaveCuit, CuitValido),
            (OpcionesArca.ClaveCertificadoRuta, Path.Combine(_directorio, "produccion.pfx")),
            (OpcionesArca.ClaveCertificadoClave, "clave-de-prueba")));

        servicios.ValidarArca();

        Assert.Contains(_registro.Entradas, e => e.Nivel == LogLevel.Warning
            && e.Texto.Contains("producción", StringComparison.Ordinal)
            && e.Texto.Contains(OpcionesArca.ClaveEntorno, StringComparison.Ordinal));
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains(CuitValido, StringComparison.Ordinal)
            || e.Texto.Contains("clave-de-prueba", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OpcionesArca.ClaveCuit, CuitValido)]
    [InlineData(OpcionesArca.ClaveCuit, "20000000002")] // en simulado no se valida, pero está presente
    [InlineData(OpcionesArca.ClaveCertificadoRuta, "/ruta/que/no/debe/aparecer.pfx")]
    [InlineData(OpcionesArca.ClaveCertificadoClave, "clave-que-no-debe-aparecer")]
    public void Arranque_SimuladoConCredenciales_AdvierteSinValoresYSigueEnSimulado(string clave, string valor)
    {
        // Sin Arca:Entorno y con Arca:Entorno = Simulado: nunca se cambia de entorno solo (R-14).
        foreach (var configuracion in new[]
                 {
                     Configuracion((clave, valor)),
                     Configuracion((OpcionesArca.ClaveEntorno, "Simulado"), (clave, valor)),
                 })
        {
            var registro = new RegistroCapturadoArca();
            using var servicios = new ServiceCollection()
                .AddLogging(log => log.AddProvider(registro).SetMinimumLevel(LogLevel.Trace))
                .AgregarInfraestructura(configuracion)
                .BuildServiceProvider();

            var opciones = OpcionesArca.Leer(configuracion);
            servicios.ValidarArca();

            Assert.Equal(EntornoArca.Simulado, opciones.Entorno);
            Assert.True(opciones.CredencialesPresentes);
            Assert.IsType<SimuladorArca>(servicios.GetRequiredService<IAutorizadorComprobantes>());
            Assert.Contains(registro.Entradas, e => e.Nivel == LogLevel.Warning && e.Texto.StartsWith(
                "Hay credenciales de ARCA configuradas pero Arca:Entorno es Simulado: la facturación no tiene validez fiscal.",
                StringComparison.Ordinal));
            Assert.DoesNotContain(registro.Entradas, e => e.Texto.Contains(valor, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Opciones_SimuladoSinCredenciales_NoLasMarcaPresentes()
    {
        var opciones = OpcionesArca.Leer(Configuracion((OpcionesArca.ClaveCertificadoClave, "  ")));

        Assert.False(opciones.CredencialesPresentes);
        using var servicios = Servicios(Configuracion());
        servicios.ValidarArca();
        Assert.DoesNotContain(_registro.Entradas, e => e.Texto.Contains("Hay credenciales", StringComparison.Ordinal));
    }

    private IConfiguration Configuracion(params (string Clave, string Valor)[] pares)
    {
        var valores = new Dictionary<string, string?> { [FabricaConexion.ClaveDirectorio] = _directorio };
        foreach (var (clave, valor) in pares)
        {
            valores[clave] = valor;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private ServiceProvider Servicios(IConfiguration configuracion) =>
        new ServiceCollection()
            .AddLogging(registro => registro.AddProvider(_registro).SetMinimumLevel(LogLevel.Trace))
            .AgregarInfraestructura(configuracion)
            .BuildServiceProvider();
}
