using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests.Facturacion;

/// <summary>
/// Spec FEAT-007, Block 7: <c>POST /api/presupuestos/{numero}/factura</c> y el GET del presupuesto con la factura, de
/// punta a punta con la base SQLite y ARCA simulada (o un autorizador de prueba). Datos del cliente ficticios (D-50).
/// </summary>
public sealed class FacturaApiTests
{
    private const string TituloConflicto = "No se pudo emitir la factura.";

    private static readonly string[] CamposFactura =
        ["comprobante", "tipo", "numero", "cae", "vencimientoCae", "fecha", "total", "neto", "iva"];

    /// <summary>
    /// AC-01 por la Api: un Final de 3 líneas y total $3.000 responde 201 con la factura (Factura B, neto e IVA de la
    /// alícuota 21, la fecha local del día) y <c>Location</c> al presupuesto; la factura guardada tiene las 3 líneas.
    /// </summary>
    [Fact]
    public async Task Emitir_Final3Lineas_201ConLaFactura()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(500m, 1000m, 1500m);

        var respuesta = await escenario.EmitirAsync(numero);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var ubicacion = Assert.IsType<Uri>(respuesta.Headers.Location);
        Assert.Equal(
            $"{EscenarioFacturacion.Ruta}/{numero}",
            ubicacion.IsAbsoluteUri ? ubicacion.AbsolutePath : ubicacion.OriginalString);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposFactura, json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal("Factura B 0003-00000001", json.GetProperty("comprobante").GetString());
        Assert.Equal("b", json.GetProperty("tipo").GetString());
        Assert.Equal(1, json.GetProperty("numero").GetInt64());
        Assert.Matches("^[0-9]{14}$", json.GetProperty("cae").GetString());
        var hoy = DateOnly.FromDateTime(fabrica.Reloj.GetLocalNow().DateTime);
        Assert.Equal(Texto(hoy), json.GetProperty("fecha").GetString());
        Assert.Equal(Texto(hoy.AddDays(SimuladorArca.DiasVencimientoCae)), json.GetProperty("vencimientoCae").GetString());
        Assert.Equal(3000m, json.GetProperty("total").GetDecimal());
        Assert.Equal(2479.34m, json.GetProperty("neto").GetDecimal());
        Assert.Equal(520.66m, json.GetProperty("iva").GetDecimal());

        using var alcance = fabrica.Services.CreateScope();
        var factura = await alcance.ServiceProvider.GetRequiredService<IFacturaRepository>()
            .ObtenerPorPresupuestoAsync(numero);
        Assert.NotNull(factura);
        Assert.Equal([500m, 1000m, 1500m], factura.Lineas.Select(linea => linea.PrecioFinal));
        Assert.Equal(EntornoArca.Simulado, factura.Entorno);
        Assert.False(await alcance.ServiceProvider.GetRequiredService<IEmisionPendienteRepository>().ExisteAsync(numero));
    }

    /// <summary>
    /// AC-19 y AC-30: con el punto de venta 3 y el número 125 que asigna ARCA, el 201 y el GET del presupuesto muestran
    /// "Factura B 0003-00000125", sin emisión pendiente.
    /// </summary>
    [Fact]
    public async Task ObtenerPresupuesto_ConFactura_MuestraComprobante()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1815m);
        await File.WriteAllTextAsync(
            Path.Combine(fabrica.DirectorioDatos, SimuladorArca.NombreArchivo), "{\"contadores\":{\"B-3\":124}}");

        var emitida = await escenario.EmitirAsync(numero);
        Assert.Equal(HttpStatusCode.Created, emitida.StatusCode);
        Assert.Equal("Factura B 0003-00000125", (await Http.JsonAsync(emitida)).GetProperty("comprobante").GetString());

        var leido = await escenario.LeerAsync(numero);
        var factura = leido.GetProperty("factura");
        Assert.Equal(["comprobante"], factura.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal("Factura B 0003-00000125", factura.GetProperty("comprobante").GetString());
        Assert.False(leido.GetProperty("emisionPendiente").GetBoolean());
        // El GET no lleva el receptor ni nada de la factura además del comprobante.
        Assert.DoesNotContain("cae", leido.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>AC-02 por la Api (sad path): un Borrador responde 409 <c>borrador</c> sin llamar a ARCA.</summary>
    [Fact]
    public async Task Emitir_Borrador_Conflicto409SinLlamarArca()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.BorradorAsync(1000m);

        var respuesta = await escenario.EmitirAsync(numero);

        await AfirmarConflictoAsync(
            respuesta, "borrador", "El presupuesto está en Borrador: pasalo a Final para facturarlo.");
        Assert.False(File.Exists(Path.Combine(fabrica.DirectorioDatos, SimuladorArca.NombreArchivo)));
        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("factura").ValueKind);
        Assert.False(leido.GetProperty("emisionPendiente").GetBoolean());
    }

    /// <summary>AC-03 por la Api (sad path): un presupuesto ya facturado responde 409 <c>facturado</c> sin llamar a ARCA.</summary>
    [Fact]
    public async Task Emitir_YaFacturado_Conflicto409SinLlamarArca()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        Assert.Equal(HttpStatusCode.Created, (await escenario.EmitirAsync(numero)).StatusCode);
        var contador = await File.ReadAllTextAsync(Path.Combine(fabrica.DirectorioDatos, SimuladorArca.NombreArchivo));

        var respuesta = await escenario.EmitirAsync(numero);

        await AfirmarConflictoAsync(respuesta, "facturado", "El presupuesto ya tiene una factura.");
        Assert.Equal(
            contador, await File.ReadAllTextAsync(Path.Combine(fabrica.DirectorioDatos, SimuladorArca.NombreArchivo)));
        Assert.Equal(
            "Factura B 0003-00000001",
            (await escenario.LeerAsync(numero)).GetProperty("factura").GetProperty("comprobante").GetString());
    }

    /// <summary>
    /// AC-13, AC-32 y AC-29 por la Api (sad path): sin tope, sin punto de venta o con la alícuota 19 guardada antes de
    /// FR-26, 409 con su motivo y sin llamar a ARCA.
    /// </summary>
    [Theory]
    [InlineData("sin_tope", "Cargá el tope de identificación en la configuración para facturar.")]
    [InlineData("sin_punto_venta", "Cargá el punto de venta en la configuración para facturar.")]
    [InlineData("alicuota_no_admitida", "Elegí una alícuota de IVA válida en la configuración para facturar.")]
    public async Task Emitir_ConfiguracionIncompleta_Conflicto409SinLlamarArca(string motivo, string detalle)
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica, configurar: false);
        var numero = await escenario.FinalAsync(1000m);
        switch (motivo)
        {
            case "sin_punto_venta":
                await escenario.ConfigurarAsync(tope: 100_000m, puntoVenta: null);
                break;
            case "alicuota_no_admitida":
                await escenario.ConfigurarAsync(tope: 100_000m, puntoVenta: 3);
                await escenario.GuardarAlicuotaFueraDeLaListaAsync(1900);
                break;
        }

        var respuesta = await escenario.EmitirAsync(numero);

        await AfirmarConflictoAsync(respuesta, motivo, detalle);
        Assert.False(File.Exists(Path.Combine(fabrica.DirectorioDatos, SimuladorArca.NombreArchivo)));
        Assert.False((await escenario.LeerAsync(numero)).GetProperty("emisionPendiente").GetBoolean());
    }

    /// <summary>
    /// AC-20 por la Api (rechazo, sad path): 409 <c>rechazada</c> con el código y la descripción de ARCA; sin factura ni
    /// pendiente, así Facturar sigue habilitado.
    /// </summary>
    [Fact]
    public async Task Emitir_Rechazada_409ConCodigoYDescripcion()
    {
        using var fabrica = new FabricaApi(respuestaArca: FabricaApi.RespuestaArcaRechazar);
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);

        var respuesta = await escenario.EmitirAsync(numero);

        var json = await AfirmarConflictoAsync(respuesta, "rechazada", "ARCA rechazó la factura.", conArca: true);
        Assert.Equal(SimuladorArca.CodigoRechazo, json.GetProperty("codigo").GetInt32());
        Assert.Equal(SimuladorArca.DescripcionRechazo, json.GetProperty("descripcion").GetString());
        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("factura").ValueKind);
        Assert.False(leido.GetProperty("emisionPendiente").GetBoolean());
    }

    /// <summary>
    /// AC-21 y AC-22 por la Api (timeout, sad path): ARCA no responde; a los 30 s (reloj de la fábrica) 409
    /// <c>sin_respuesta</c>, el GET muestra la emisión pendiente y volver a facturar responde 409 <c>pendiente</c>.
    /// </summary>
    [Fact]
    public async Task Emitir_SinRespuesta_409YLaPendienteBloquea()
    {
        TaskCompletionSource? llamado = null;
        using var fabrica = new FabricaApi(
            servicios => llamado = AutorizadorConSenal.Envolver(servicios),
            respuestaArca: FabricaApi.RespuestaArcaNoResponder);
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);

        var pedido = escenario.EmitirAsync(numero);
        // El plazo lo mide el reloj de la fábrica y ya corre cuando el simulador recibe la solicitud: se adelanta una vez.
        await llamado!.Task.WaitAsync(TimeSpan.FromSeconds(30));
        fabrica.Reloj.Advance(TimeSpan.FromSeconds(FabricaApi.TiempoMaximoArcaSegundos));

        var respuesta = await pedido.WaitAsync(TimeSpan.FromSeconds(30));

        await AfirmarConflictoAsync(
            respuesta, "sin_respuesta", "ARCA no respondió. La emisión quedó pendiente de verificación.");
        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("factura").ValueKind);
        Assert.True(leido.GetProperty("emisionPendiente").GetBoolean());

        await AfirmarConflictoAsync(
            await escenario.EmitirAsync(numero), "pendiente", "La emisión anterior está pendiente de verificación.");
    }

    /// <summary>
    /// Sad path: concepto faltante, vacío o desconocido, 400 en <c>concepto</c> con la acción del caso de uso; un concepto
    /// que no es texto o un cuerpo roto, 400 en <c>concepto</c> con la acción fija del cuerpo ilegible. Ninguno graba.
    /// </summary>
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"concepto\":null}", false)]
    [InlineData("{\"concepto\":\"\"}", false)]
    [InlineData("{\"concepto\":\"servicios\"}", false)]
    [InlineData("{\"concepto\":\"Productos\"}", false)]
    [InlineData("{\"concepto\":1}", true)]
    [InlineData("{\"concepto\":[\"productos\"]}", true)]
    [InlineData("esto no es json", true)]
    [InlineData("[]", true)]
    public async Task Emitir_ConceptoInvalido_400(string cuerpo, bool ilegible)
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);

        var respuesta = await escenario.EmitirAsync(numero, cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        var errores = json.GetProperty("errors");
        Assert.Equal(["concepto"], errores.EnumerateObject().Select(campo => campo.Name).ToArray());
        var accion = Assert.Single(errores.GetProperty("concepto").EnumerateArray()).GetString();
        Assert.Equal(
            ilegible
                ? "No se pudo leer el concepto enviado. Elegilo de nuevo."
                : "Elegí el concepto de la factura: Productos o Productos y servicios.",
            accion);
        Assert.DoesNotContain("servicios\"", json.GetRawText(), StringComparison.Ordinal);
        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("factura").ValueKind);
        Assert.False(leido.GetProperty("emisionPendiente").GetBoolean());
    }

    /// <summary>Sad path: un número sin presupuesto responde 404 con el título fijo, sin llamar a ARCA.</summary>
    [Fact]
    public async Task Emitir_PresupuestoInexistente_404Error()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);

        var respuesta = await escenario.EmitirAsync(999);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal("No se encontró el presupuesto.", (await Http.JsonAsync(respuesta)).GetProperty("title").GetString());
        Assert.False(File.Exists(Path.Combine(fabrica.DirectorioDatos, SimuladorArca.NombreArchivo)));
    }

    /// <summary>
    /// AC-23 por la Api (sad path, FR-25): no hay PUT ni DELETE de la factura. La ruta de la factura solo acepta POST
    /// (405) y no hay ruta propia de facturas (404); la factura queda sin cambios.
    /// </summary>
    [Fact]
    public async Task PutYDeleteFactura_NoExisten_Rechazado405o404()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        Assert.Equal(HttpStatusCode.Created, (await escenario.EmitirAsync(numero)).StatusCode);
        var antes = (await escenario.LeerAsync(numero)).GetRawText();

        var ruta = $"{EscenarioFacturacion.Ruta}/{numero}/factura";
        var put = await Http.EnviarAsync(
            escenario.Cliente, HttpMethod.Put, ruta,
            new StringContent("{\"concepto\":\"productos\"}", Encoding.UTF8, "application/json"), escenario.Cookie);
        var delete = await Http.EnviarAsync(escenario.Cliente, HttpMethod.Delete, ruta, null, escenario.Cookie);
        var patch = await Http.EnviarAsync(
            escenario.Cliente, HttpMethod.Patch, ruta,
            new StringContent("{}", Encoding.UTF8, "application/json"), escenario.Cookie);
        var ajena = await Http.EnviarAsync(
            escenario.Cliente, HttpMethod.Delete, $"/api/facturas/{numero}", null, escenario.Cookie);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, patch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.Equal(antes, (await escenario.LeerAsync(numero)).GetRawText());
    }

    /// <summary>
    /// R-09 (sad path): dos pedidos simultáneos del mismo presupuesto. Uno graba la pendiente y llega a ARCA; el otro
    /// responde 409 <c>pendiente</c> sin llamar a ARCA. Al responder ARCA, el primero responde 201.
    /// </summary>
    [Fact]
    public async Task Emitir_DosPedidosSimultaneos_UnoConflictoPendiente()
    {
        var autorizador = new AutorizadorQueEspera();
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IAutorizadorComprobantes>();
            servicios.AddSingleton<IAutorizadorComprobantes>(autorizador);
        });
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);

        var pedidos = new[] { escenario.EmitirAsync(numero), escenario.EmitirAsync(numero) };
        await autorizador.Llamado.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var primero = await Task.WhenAny(pedidos).WaitAsync(TimeSpan.FromSeconds(30));
        var conflicto = await primero;
        autorizador.Liberar.TrySetResult();
        var otro = await (pedidos[0] == primero ? pedidos[1] : pedidos[0]).WaitAsync(TimeSpan.FromSeconds(30));

        await AfirmarConflictoAsync(conflicto, "pendiente", "La emisión anterior está pendiente de verificación.");
        Assert.Equal(HttpStatusCode.Created, otro.StatusCode);
        Assert.Equal(1, autorizador.Llamadas);
        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(
            "Factura B 0003-00000125", leido.GetProperty("factura").GetProperty("comprobante").GetString());
        Assert.False(leido.GetProperty("emisionPendiente").GetBoolean());
    }

    /// <summary>
    /// Sad path: una excepción imprevista del autorizador (el puerto dice que no lanza) responde el 500 genérico, sin su
    /// texto; como no se sabe si la solicitud llegó a ARCA, la emisión queda pendiente y volver a facturar es 409
    /// <c>pendiente</c>.
    /// </summary>
    [Fact]
    public async Task Emitir_AutorizadorLanza_500ConLaPendienteGuardada()
    {
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IAutorizadorComprobantes>();
            servicios.AddSingleton<IAutorizadorComprobantes>(new AutorizadorQueLanza());
        });
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);

        var respuesta = await escenario.EmitirAsync(numero);

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.DoesNotContain(AutorizadorQueLanza.Texto, await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("factura").ValueKind);
        Assert.True(leido.GetProperty("emisionPendiente").GetBoolean());
        await AfirmarConflictoAsync(
            await escenario.EmitirAsync(numero), "pendiente", "La emisión anterior está pendiente de verificación.");
    }

    /// <summary>
    /// Un 409 de la emisión: ProblemDetails con el título fijo, el detalle de su motivo y la extensión <c>motivo</c>;
    /// <c>codigo</c> y <c>descripcion</c> solo en un rechazo de ARCA, y nunca <c>errors</c>.
    /// </summary>
    private static async Task<JsonElement> AfirmarConflictoAsync(
        HttpResponseMessage respuesta, string motivo, string detalle, bool conArca = false)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(TituloConflicto, json.GetProperty("title").GetString());
        Assert.Equal(detalle, json.GetProperty("detail").GetString());
        Assert.Equal(motivo, json.GetProperty("motivo").GetString());
        Assert.Equal(conArca, json.TryGetProperty("codigo", out _));
        Assert.Equal(conArca, json.TryGetProperty("descripcion", out _));
        Assert.False(json.TryGetProperty("errors", out _));
        return json;
    }

    private static string Texto(DateOnly fecha) => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Lanza una excepción imprevista con un texto marcado.</summary>
    private sealed class AutorizadorQueLanza : IAutorizadorComprobantes
    {
        public const string Texto = "Zq7-falla-imprevista";

        public EntornoArca Entorno => EntornoArca.Simulado;

        public TimeSpan TiempoMaximo => TimeSpan.FromSeconds(30);

        public Task<ResultadoAutorizacion> AutorizarAsync(SolicitudComprobante solicitud, CancellationToken cancelacion) =>
            throw new InvalidOperationException(Texto);
    }

    /// <summary>Autoriza con el número 125 recién cuando la prueba lo libera; anota cuántas veces lo llamaron.</summary>
    private sealed class AutorizadorQueEspera : IAutorizadorComprobantes
    {
        private int _llamadas;

        public TaskCompletionSource Llamado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Llamadas => Volatile.Read(ref _llamadas);

        public EntornoArca Entorno => EntornoArca.Simulado;

        public TimeSpan TiempoMaximo => TimeSpan.FromSeconds(30);

        public async Task<ResultadoAutorizacion> AutorizarAsync(
            SolicitudComprobante solicitud, CancellationToken cancelacion)
        {
            Interlocked.Increment(ref _llamadas);
            Llamado.TrySetResult();
            await Liberar.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None);
            return new ResultadoAutorizacion.Autorizada(125, "71234567890123", solicitud.Fecha.AddDays(10));
        }
    }
}

/// <summary>
/// Envuelve el autorizador que registró la raíz de composición (el simulador) y avisa en <see cref="Llamado"/> cuando
/// recibe la solicitud. Para las pruebas de "sin respuesta": el plazo de la emisión ya corre en ese momento (se crea
/// antes de pedir el candado, D-32), así que la prueba adelanta el reloj de la fábrica una sola vez, sin esperas reales.
/// </summary>
internal sealed class AutorizadorConSenal(IAutorizadorComprobantes real, TaskCompletionSource llamado)
    : IAutorizadorComprobantes
{
    public EntornoArca Entorno => real.Entorno;

    public TimeSpan TiempoMaximo => real.TiempoMaximo;

    public Task<ResultadoAutorizacion> AutorizarAsync(SolicitudComprobante solicitud, CancellationToken cancelacion)
    {
        llamado.TrySetResult();
        return real.AutorizarAsync(solicitud, cancelacion);
    }

    /// <summary>
    /// Reemplaza el registro del autorizador por su versión con señal y devuelve la señal: se completa cuando el
    /// autorizador recibe la primera solicitud.
    /// </summary>
    public static TaskCompletionSource Envolver(IServiceCollection servicios)
    {
        var original = servicios.Last(descriptor => descriptor.ServiceType == typeof(IAutorizadorComprobantes));
        var fabrica = original.ImplementationFactory
            ?? throw new InvalidOperationException("El autorizador no se registró con una fábrica.");
        servicios.Remove(original);
        var llamado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        servicios.AddSingleton<IAutorizadorComprobantes>(proveedor =>
            new AutorizadorConSenal((IAutorizadorComprobantes)fabrica(proveedor), llamado));
        return llamado;
    }
}

/// <summary>
/// Lo que necesitan las pruebas de facturación por la Api: la sesión, la configuración (Responsable Inscripto, alícuota
/// 21, tope y punto de venta 3), artículos con precio de venta exacto (costo y margen 0) y presupuestos en Borrador o en
/// Final, con datos del cliente ficticios (D-50).
/// </summary>
internal sealed class EscenarioFacturacion
{
    public const string Ruta = "/api/presupuestos";
    public const string Dni = "00718294";
    public const string Apellido = "Zq7Ficticiez";
    public const string Nombre = "Zq7Prueboncia";
    public const string Email = "zq7@example.com";
    public const string Telefono = "011-5555-0199";

    private readonly FabricaApi _fabrica;
    private int _articulos;

    private EscenarioFacturacion(FabricaApi fabrica, HttpClient cliente, string cookie)
    {
        _fabrica = fabrica;
        Cliente = cliente;
        Cookie = cookie;
    }

    public HttpClient Cliente { get; }

    public string Cookie { get; }

    /// <summary>Define la sesión y, con <paramref name="configurar"/>, graba la configuración completa.</summary>
    public static async Task<EscenarioFacturacion> CrearAsync(
        FabricaApi fabrica, bool configurar = true, decimal tope = 100_000m)
    {
        var cliente = fabrica.CrearClienteSinCookies();
        var cookie = await Http.DefinirAsync(cliente);
        var escenario = new EscenarioFacturacion(fabrica, cliente, cookie);
        if (configurar)
        {
            await escenario.ConfigurarAsync(tope, 3);
        }

        return escenario;
    }

    /// <summary>Graba la configuración: Responsable Inscripto, alícuota 21, múltiplo 0,01, el tope y el punto de venta.</summary>
    public async Task ConfigurarAsync(decimal? tope, int? puntoVenta)
    {
        var cuerpo = JsonSerializer.Serialize(new
        {
            alicuotaIva = 21m,
            condicionFiscal = "responsable_inscripto",
            multiploRedondeo = 0.01m,
            topeIdentificacion = tope,
            puntoVenta,
        });
        var respuesta = await Http.PostJsonAsync(Cliente, "/api/configuracion", cuerpo, Cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    /// <summary>
    /// Deja guardada una alícuota que no es de ARCA (en centésimas), como una grabada antes de FR-26 (D-06): la Api ya no
    /// la acepta, así que se escribe en la base con un comando parametrizado.
    /// </summary>
    public async Task GuardarAlicuotaFueraDeLaListaAsync(long centesimas)
    {
        using var alcance = _fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
        await contexto.Database.ExecuteSqlAsync($"UPDATE ConfiguracionNegocio SET AlicuotaIvaCentesimas = {centesimas}");
    }

    /// <summary>Un presupuesto en Borrador con una línea por precio (cantidad 1, sin descuento); devuelve su número.</summary>
    public async Task<long> BorradorAsync(params decimal[] precios)
    {
        var lineas = new List<object>();
        foreach (var precio in precios)
        {
            lineas.Add(new { codigoArticulo = await ArticuloAsync(precio), cantidad = 1, porcentajeDescuento = 0 });
        }

        var cuerpo = JsonSerializer.Serialize(new
        {
            cliente = new { apellido = Apellido, nombre = Nombre, dni = Dni, email = Email, telefono = Telefono },
            lineas,
        });
        var respuesta = await Http.PostJsonAsync(Cliente, Ruta, cuerpo, Cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("numero").GetInt64();
    }

    /// <summary>Un presupuesto grabado en Borrador y pasado a Final, como en la app (AGENTS.md).</summary>
    public async Task<long> FinalAsync(params decimal[] precios)
    {
        var numero = await BorradorAsync(precios);
        var respuesta = await Http.EnviarAsync(
            Cliente,
            HttpMethod.Put,
            $"{Ruta}/{numero}/estado",
            new StringContent("{\"estado\":\"final\",\"version\":0}", Encoding.UTF8, "application/json"),
            Cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return numero;
    }

    /// <summary>Pide la factura del presupuesto con el concepto Productos, o con <paramref name="cuerpo"/>.</summary>
    public Task<HttpResponseMessage> EmitirAsync(long numero, string cuerpo = "{\"concepto\":\"productos\"}") =>
        Http.PostJsonAsync(Cliente, $"{Ruta}/{numero}/factura", cuerpo, Cookie);

    /// <summary>El GET del presupuesto (200).</summary>
    public async Task<JsonElement> LeerAsync(long numero)
    {
        var respuesta = await Http.GetAsync(Cliente, $"{Ruta}/{numero}", Cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).Clone();
    }

    /// <summary>Un artículo con precio de venta exacto: costo <paramref name="precio"/> y margen 0.</summary>
    private async Task<long> ArticuloAsync(decimal precio)
    {
        _articulos++;
        var cuerpo = JsonSerializer.Serialize(new
        {
            codigoProveedor = $"FAC-{_articulos}",
            descripcion = $"Artículo {_articulos}",
            precioCosto = precio,
            margenUtilidad = 0m,
        });
        var respuesta = await Http.PostJsonAsync(Cliente, "/api/articulos", cuerpo, Cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(precio, json.GetProperty("precioVenta").GetDecimal());
        return json.GetProperty("codigo").GetInt64();
    }
}
