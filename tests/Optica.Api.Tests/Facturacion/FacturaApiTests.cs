using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Api.Acceso;
using Optica.Application.Comun;
using Optica.Application.Facturacion;
using Optica.Domain.Catalogo;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;
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
        // FR-01 (ADR-025): la pendiente guardada tiene el número pedido al simulador, el primero del punto de venta.
        Assert.Equal(1, (await PendienteAsync(fabrica, numero))?.Numero);

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
    /// R-09 y FEAT-008 (sad path, ADR-025): dos pedidos simultáneos del mismo presupuesto. El primero toma el turno,
    /// registra la pendiente con el número 125 y queda esperando a ARCA; el segundo, que llega mientras tanto, responde
    /// 409 <c>pendiente</c> sin pedir el número ni llamar a ARCA. Al responder ARCA, el primero responde 201.
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

        var primero = escenario.EmitirAsync(numero);
        await autorizador.Llamado.Task.WaitAsync(TimeSpan.FromSeconds(30));
        // El número quedó registrado antes de enviar la solicitud (FR-01).
        Assert.Equal(125, (await PendienteAsync(fabrica, numero))?.Numero);
        var conflicto = await escenario.EmitirAsync(numero).WaitAsync(TimeSpan.FromSeconds(30));
        autorizador.Liberar.TrySetResult();
        var emitida = await primero.WaitAsync(TimeSpan.FromSeconds(30));

        await AfirmarConflictoAsync(conflicto, "pendiente", "La emisión anterior está pendiente de verificación.");
        Assert.Equal(HttpStatusCode.Created, emitida.StatusCode);
        Assert.Equal(1, autorizador.Llamadas);
        Assert.Equal(1, autorizador.PedidosDeNumero);
        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(
            "Factura B 0003-00000125", leido.GetProperty("factura").GetProperty("comprobante").GetString());
        Assert.False(leido.GetProperty("emisionPendiente").GetBoolean());
    }

    /// <summary>
    /// Sad path, con el orden de ADR-025: el próximo número (125) se registra en la pendiente y recién entonces el
    /// autorizador lanza una excepción imprevista (el puerto dice que no lanza). Responde el 500 genérico, sin su texto;
    /// como no se sabe si la solicitud llegó a ARCA, la emisión queda pendiente con el número 125 y volver a facturar es
    /// 409 <c>pendiente</c>.
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
        var pendiente = await PendienteAsync(fabrica, numero);
        Assert.NotNull(pendiente);
        Assert.Equal(125, pendiente.Numero);
        Assert.Equal(1000m, pendiente.Total);
        await AfirmarConflictoAsync(
            await escenario.EmitirAsync(numero), "pendiente", "La emisión anterior está pendiente de verificación.");
    }

    // Spec FEAT-009, Block 5: el detalle y el PDF de la factura (FR-04, FR-05, FR-24, FR-25). Datos ficticios: "Pérez,
    // Ana" del PRD con un DNI que empieza con 00 (D-08), CUIT del emisor 20-00000000-1 (D-33) y razón social inventada.

    private const string TituloFacturaNoEncontrada = "No se encontró la factura.";

    private static readonly string[] CamposDetalle =
    [
        "comprobante", "fecha", "presupuesto", "apellido", "nombre", "dni", "receptor", "lineas", "total",
        "ivaContenido", "cae", "vencimientoCae", "simulada",
    ];

    /// <summary>
    /// AC-24 (FR-24, FR-25, D-03, D-15): la factura emitida sin identificar al receptor desde el presupuesto 155 de
    /// "Pérez, Ana" responde 200 con sus datos y el presupuesto 155: el comprobante como texto, Apellido, Nombre y DNI del
    /// cliente del presupuesto y el receptor "Consumidor Final". No hay acciones de edición ni de eliminación: PUT, PATCH
    /// y DELETE del detalle y del PDF no existen (405), y la factura queda igual.
    /// </summary>
    [Fact]
    public async Task GetFactura_Detalle_Presupuesto155SinAcciones()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        await escenario.GrabarBorradorDirectoAsync(154);
        var numero = await escenario.FinalDeAsync(
            EscenarioFacturacion.ApellidoPrd, EscenarioFacturacion.NombrePrd, EscenarioFacturacion.DniPrd, 1815m);
        Assert.Equal(155, numero);
        await escenario.SimularUltimoNumeroAsync(124);
        Assert.Equal(HttpStatusCode.Created, (await escenario.EmitirAsync(numero)).StatusCode);

        var respuesta = await Http.GetAsync(escenario.Cliente, $"{EscenarioFacturacion.Ruta}/155/factura", escenario.Cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(CamposDetalle, json.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        Assert.Equal("Factura B 0003-00000125", json.GetProperty("comprobante").GetString());
        var hoy = DateOnly.FromDateTime(fabrica.Reloj.GetLocalNow().DateTime);
        Assert.Equal(Texto(hoy), json.GetProperty("fecha").GetString());
        Assert.Equal(155, json.GetProperty("presupuesto").GetInt64());
        Assert.Equal("Pérez", json.GetProperty("apellido").GetString());
        Assert.Equal("Ana", json.GetProperty("nombre").GetString());
        Assert.Equal("00123456", json.GetProperty("dni").GetString());
        Assert.Equal("Consumidor Final", json.GetProperty("receptor").GetString());
        var linea = Assert.Single(json.GetProperty("lineas").EnumerateArray().ToList());
        Assert.Equal(
            ["descripcion", "cantidad", "importe"], linea.EnumerateObject().Select(propiedad => propiedad.Name).ToArray());
        // El artículo 1 es el del presupuesto 154; el 155 lleva el artículo 2.
        Assert.Equal("Artículo 2", linea.GetProperty("descripcion").GetString());
        Assert.Equal(1, linea.GetProperty("cantidad").GetInt32());
        Assert.Equal(1815m, linea.GetProperty("importe").GetDecimal());
        Assert.Equal(1815m, json.GetProperty("total").GetDecimal());
        Assert.Equal(315m, json.GetProperty("ivaContenido").GetDecimal());
        Assert.Matches("^[0-9]{14}$", json.GetProperty("cae").GetString());
        Assert.Equal(
            Texto(hoy.AddDays(SimuladorArca.DiasVencimientoCae)), json.GetProperty("vencimientoCae").GetString());
        Assert.True(json.GetProperty("simulada").GetBoolean());

        // Sin acciones: solo lectura (FR-24, RF-32).
        var antes = json.GetRawText();
        foreach (var ruta in new[] { "/api/presupuestos/155/factura", "/api/presupuestos/155/factura/pdf" })
        {
            foreach (var metodo in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            {
                var contenido = metodo == HttpMethod.Delete
                    ? null
                    : new StringContent("{}", Encoding.UTF8, "application/json");
                var rechazo = await Http.EnviarAsync(escenario.Cliente, metodo, ruta, contenido, escenario.Cookie);
                Assert.True(
                    rechazo.StatusCode == HttpStatusCode.MethodNotAllowed,
                    $"{metodo} {ruta} respondió {(int)rechazo.StatusCode}; se esperaba 405.");
            }
        }

        var despues = await Http.GetAsync(escenario.Cliente, $"{EscenarioFacturacion.Ruta}/155/factura", escenario.Cookie);
        Assert.Equal(antes, (await Http.JsonAsync(despues)).GetRawText());
    }

    /// <summary>
    /// Sad path: un presupuesto en Final sin factura y un número sin presupuesto responden 404 con el título fijo, sin
    /// detalles, en el detalle y en el PDF; un número que no es un <c>long</c> no coincide con la ruta.
    /// </summary>
    [Fact]
    public async Task GetFactura_SinFactura_404()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        await escenario.CargarEmisorAsync();
        var sinFactura = await escenario.FinalAsync(1000m);

        foreach (var ruta in new[]
                 {
                     $"{EscenarioFacturacion.Ruta}/{sinFactura}/factura",
                     $"{EscenarioFacturacion.Ruta}/{sinFactura}/factura/pdf",
                     $"{EscenarioFacturacion.Ruta}/999/factura",
                     $"{EscenarioFacturacion.Ruta}/999/factura/pdf",
                 })
        {
            var respuesta = await Http.GetAsync(escenario.Cliente, ruta, escenario.Cookie);

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
            Assert.Null(respuesta.Content.Headers.ContentDisposition);
            var json = await Http.JsonAsync(respuesta);
            Assert.Equal(TituloFacturaNoEncontrada, json.GetProperty("title").GetString());
            Assert.False(json.TryGetProperty("detail", out _));
            Assert.DoesNotContain(EscenarioFacturacion.Apellido, json.GetRawText(), StringComparison.Ordinal);
        }

        var noEsNumero = await Http.GetAsync(escenario.Cliente, $"{EscenarioFacturacion.Ruta}/abc/factura", escenario.Cookie);
        Assert.Equal(HttpStatusCode.NotFound, noEsNumero.StatusCode);
    }

    /// <summary>
    /// AC-06 (FR-05): con los datos del emisor cargados, el PDF de la factura se descarga como adjunto
    /// <c>factura-B-0003-00000125.pdf</c>, sin datos del cliente en el nombre; es un PDF legible con el comprobante, el
    /// emisor y la leyenda de comprobante simulado.
    /// </summary>
    [Fact]
    public async Task GetFacturaPdf_Descarga_NombreSinDatos()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        await escenario.CargarEmisorAsync();
        var numero = await escenario.FinalDeAsync(
            EscenarioFacturacion.ApellidoPrd, EscenarioFacturacion.NombrePrd, EscenarioFacturacion.DniPrd, 1815m);
        await escenario.SimularUltimoNumeroAsync(124);
        Assert.Equal(HttpStatusCode.Created, (await escenario.EmitirAsync(numero)).StatusCode);

        using var respuesta = await Http.GetAsync(
            escenario.Cliente, $"{EscenarioFacturacion.Ruta}/{numero}/factura/pdf", escenario.Cookie);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/pdf", respuesta.Content.Headers.ContentType?.MediaType);
        var disposicion = Assert.IsType<System.Net.Http.Headers.ContentDispositionHeaderValue>(
            respuesta.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", disposicion.DispositionType);
        Assert.Equal("factura-B-0003-00000125.pdf", disposicion.FileName?.Trim('"'));
        foreach (var dato in new[] { "Pérez", "Perez", "Ana", EscenarioFacturacion.DniPrd })
        {
            Assert.DoesNotContain(dato, disposicion.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
        using var documento = UglyToad.PdfPig.PdfDocument.Open(bytes);
        var texto = string.Join(' ', documento.GetPage(1).GetWords().Select(palabra => palabra.Text));
        Assert.Contains("Factura B", texto, StringComparison.Ordinal);
        Assert.Contains("00000125", texto, StringComparison.Ordinal);
        Assert.Contains("Zq7Óptica", texto, StringComparison.Ordinal);
        Assert.Contains("COMPROBANTE SIMULADO", texto, StringComparison.Ordinal);

        // R-02 y FR-08 de punta a punta: emitida a Consumidor Final, el PDF no lleva el Apellido, el Nombre ni el DNI del
        // cliente del presupuesto.
        Assert.Contains("Consumidor Final", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(EscenarioFacturacion.ApellidoPrd, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(EscenarioFacturacion.DniPrd, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Nombre:", texto, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC-05 (FR-04, sad path): sin los datos del emisor, el PDF responde 409 <c>sin_datos_emisor</c> con el título y el
    /// detalle fijos, que piden completarlos en la configuración; no hay adjunto ni datos del cliente.
    /// </summary>
    [Fact]
    public async Task GetFacturaPdf_SinDatosEmisor_409()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        Assert.Equal(HttpStatusCode.Created, (await escenario.EmitirAsync(numero)).StatusCode);

        var respuesta = await Http.GetAsync(
            escenario.Cliente, $"{EscenarioFacturacion.Ruta}/{numero}/factura/pdf", escenario.Cookie);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Null(respuesta.Content.Headers.ContentDisposition);
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(409, json.GetProperty("status").GetInt32());
        Assert.Equal("No se pudo generar el PDF de la factura.", json.GetProperty("title").GetString());
        Assert.Equal(
            "Completá los datos del emisor en la configuración para generar el PDF de la factura.",
            json.GetProperty("detail").GetString());
        Assert.Equal("sin_datos_emisor", json.GetProperty("motivo").GetString());
        Assert.False(json.TryGetProperty("errors", out _));
        Assert.DoesNotContain(EscenarioFacturacion.Apellido, json.GetRawText(), StringComparison.Ordinal);

        // El detalle sí responde: los datos del emisor solo hacen falta para el PDF.
        var detalle = await Http.GetAsync(escenario.Cliente, $"{EscenarioFacturacion.Ruta}/{numero}/factura", escenario.Cookie);
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
    }

    /// <summary>
    /// Sad path: si el generador del PDF lanza, la Api responde el 500 genérico, sin el texto de la excepción ni
    /// adjunto.
    /// </summary>
    [Fact]
    public async Task GetFacturaPdf_GeneradorLanza_500SinTexto()
    {
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IGeneradorPdfFactura>();
            servicios.AddSingleton<IGeneradorPdfFactura>(new GeneradorPdfQueLanza());
        });
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        await escenario.CargarEmisorAsync();
        var numero = await escenario.FinalAsync(1000m);
        Assert.Equal(HttpStatusCode.Created, (await escenario.EmitirAsync(numero)).StatusCode);

        var respuesta = await Http.GetAsync(
            escenario.Cliente, $"{EscenarioFacturacion.Ruta}/{numero}/factura/pdf", escenario.Cookie);

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.Null(respuesta.Content.Headers.ContentDisposition);
        Assert.NotEqual("application/pdf", respuesta.Content.Headers.ContentType?.MediaType);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(GeneradorPdfQueLanza.Texto, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(EscenarioFacturacion.Apellido, texto, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sad path: sin sesión, el detalle y el PDF de la factura responden 401 sin cuerpo. Las dos rutas existen como GET y
    /// no son anónimas.
    /// </summary>
    [Fact]
    public async Task RutasFactura_SinSesion_401()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        await escenario.CargarEmisorAsync();
        var numero = await escenario.FinalAsync(1000m);
        Assert.Equal(HttpStatusCode.Created, (await escenario.EmitirAsync(numero)).StatusCode);
        var registradas = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get) ?? false)
            .Select(endpoint => "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .ToList();

        foreach (var (plantilla, ruta) in new[]
                 {
                     ("/api/presupuestos/{numero:long}/factura", $"{EscenarioFacturacion.Ruta}/{numero}/factura"),
                     ("/api/presupuestos/{numero:long}/factura/pdf", $"{EscenarioFacturacion.Ruta}/{numero}/factura/pdf"),
                 })
        {
            Assert.Contains(plantilla, registradas);
            Assert.False(RutasAnonimas.Contiene(HttpMethods.Get, ruta), $"GET {ruta} no puede ser anónima.");

            var respuesta = await Http.GetAsync(escenario.Cliente, ruta);

            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
            Assert.Null(respuesta.Content.Headers.ContentDisposition);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }
    }

    /// <summary>Un generador del PDF de la factura que lanza con un texto marcado.</summary>
    private sealed class GeneradorPdfQueLanza : IGeneradorPdfFactura
    {
        public const string Texto = "Zq7-falla-pdf-factura";

        public byte[] Generar(DocumentoFactura documento) => throw new InvalidOperationException(Texto);
    }

    /// <summary>La emisión pendiente guardada del presupuesto, leída en un alcance propio.</summary>
    private static async Task<EmisionPendiente?> PendienteAsync(FabricaApi fabrica, long numero)
    {
        using var alcance = fabrica.Services.CreateScope();
        return await alcance.ServiceProvider.GetRequiredService<IEmisionPendienteRepository>()
            .ObtenerPendienteAsync(numero);
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

        public Task<ResultadoProximoNumero> ProximoNumeroAsync(
            TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(125));

        public Task<ResultadoAutorizacion> AutorizarAsync(
            SolicitudComprobante solicitud, long numero, CancellationToken cancelacion) =>
            throw new InvalidOperationException(Texto);

        public Task<ResultadoConsulta> ConsultarAsync(
            TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion) =>
            throw new InvalidOperationException(Texto);
    }

    /// <summary>Autoriza con el número 125 recién cuando la prueba lo libera; anota cuántas veces lo llamaron.</summary>
    private sealed class AutorizadorQueEspera : IAutorizadorComprobantes
    {
        private int _llamadas;
        private int _pedidosDeNumero;

        public TaskCompletionSource Llamado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Llamadas => Volatile.Read(ref _llamadas);

        public int PedidosDeNumero => Volatile.Read(ref _pedidosDeNumero);

        public EntornoArca Entorno => EntornoArca.Simulado;

        public TimeSpan TiempoMaximo => TimeSpan.FromSeconds(30);

        public Task<ResultadoProximoNumero> ProximoNumeroAsync(
            TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion)
        {
            Interlocked.Increment(ref _pedidosDeNumero);
            return Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(125));
        }

        public async Task<ResultadoAutorizacion> AutorizarAsync(
            SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
        {
            Interlocked.Increment(ref _llamadas);
            Llamado.TrySetResult();
            await Liberar.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None);
            return new ResultadoAutorizacion.Autorizada(numero, "71234567890123", solicitud.Fecha.AddDays(10));
        }

        public Task<ResultadoConsulta> ConsultarAsync(
            TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoConsulta>(new ResultadoConsulta.NoAutorizado());
    }
}

/// <summary>
/// Envuelve el autorizador que registró la raíz de composición (el simulador) y avisa en <see cref="Llamado"/> cuando
/// la autorización ya está en curso. Para las pruebas de "sin respuesta": el plazo de la emisión ya corre en ese momento
/// (se crea antes de pedir el candado, D-32), así que la prueba adelanta el reloj de la fábrica una sola vez, sin esperas
/// reales. D-25: el aviso va después de que el autorizador real devolvió su tarea; el simulador con
/// <c>no_responder</c> registra la espera de la cancelación antes de devolverla, así que adelantar el reloj ya no puede
/// caer antes de que empiece a esperar (lo que lo hacía responder "no enviada").
/// </summary>
internal sealed class AutorizadorConSenal(IAutorizadorComprobantes real, TaskCompletionSource llamado)
    : IAutorizadorComprobantes
{
    public EntornoArca Entorno => real.Entorno;

    public TimeSpan TiempoMaximo => real.TiempoMaximo;

    public Task<ResultadoProximoNumero> ProximoNumeroAsync(
        TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion) =>
        real.ProximoNumeroAsync(tipo, puntoVenta, cancelacion);

    public Task<ResultadoAutorizacion> AutorizarAsync(
        SolicitudComprobante solicitud, long numero, CancellationToken cancelacion)
    {
        var autorizacion = real.AutorizarAsync(solicitud, numero, cancelacion);
        llamado.TrySetResult();
        return autorizacion;
    }

    public Task<ResultadoConsulta> ConsultarAsync(
        TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion) =>
        real.ConsultarAsync(tipo, puntoVenta, numero, cancelacion);

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

    /// <summary>El cliente "Pérez, Ana" del PRD de FEAT-009, con un DNI ficticio que empieza con 00 (D-08).</summary>
    public const string ApellidoPrd = "Pérez";
    public const string NombrePrd = "Ana";
    public const string DniPrd = "00123456";

    /// <summary>Los cinco datos del emisor, inventados, con el CUIT ficticio 20-00000000-1 (D-33).</summary>
    public const string CuerpoEmisor =
        "{\"razonSocial\":\"Zq7Óptica Inventada SA\",\"domicilioComercial\":\"Calle Inventada 123\","
        + "\"cuit\":\"20-00000000-1\",\"ingresosBrutos\":\"IB-0001\",\"inicioActividades\":\"2020-03-01\"}";

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
    public Task<long> BorradorAsync(params decimal[] precios) => BorradorDeAsync(Apellido, Nombre, Dni, precios);

    /// <summary>
    /// Un presupuesto en Borrador del cliente recibido, con una línea por precio (cantidad 1, sin descuento); devuelve su
    /// número.
    /// </summary>
    public async Task<long> BorradorDeAsync(string apellido, string nombre, string dni, params decimal[] precios)
    {
        var lineas = new List<object>();
        foreach (var precio in precios)
        {
            lineas.Add(new { codigoArticulo = await ArticuloAsync(precio), cantidad = 1, porcentajeDescuento = 0 });
        }

        var cuerpo = JsonSerializer.Serialize(new
        {
            cliente = new { apellido, nombre, dni, email = Email, telefono = Telefono },
            lineas,
        });
        var respuesta = await Http.PostJsonAsync(Cliente, Ruta, cuerpo, Cookie);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("numero").GetInt64();
    }

    /// <summary>Un presupuesto grabado en Borrador y pasado a Final, como en la app (AGENTS.md).</summary>
    public Task<long> FinalAsync(params decimal[] precios) => FinalDeAsync(Apellido, Nombre, Dni, precios);

    /// <summary>Un presupuesto del cliente recibido, grabado en Borrador y pasado a Final, como en la app (AGENTS.md).</summary>
    public async Task<long> FinalDeAsync(string apellido, string nombre, string dni, params decimal[] precios)
    {
        var numero = await BorradorDeAsync(apellido, nombre, dni, precios);
        var respuesta = await Http.EnviarAsync(
            Cliente,
            HttpMethod.Put,
            $"{Ruta}/{numero}/estado",
            new StringContent("{\"estado\":\"final\",\"version\":0}", Encoding.UTF8, "application/json"),
            Cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return numero;
    }

    /// <summary>
    /// Graba en Borrador por el repositorio el presupuesto <paramref name="numero"/>, como lo dejaría la aplicación, para
    /// que el alta siguiente tome el número que sigue (FEAT-009: el presupuesto 155 del PRD).
    /// </summary>
    public async Task GrabarBorradorDirectoAsync(long numero)
    {
        var codigo = await ArticuloAsync(1000m);
        using var alcance = _fabrica.Services.CreateScope();
        var servicios = alcance.ServiceProvider;
        var articulo = await servicios.GetRequiredService<IArticuloRepository>().ObtenerAsync(codigo)
            ?? throw new InvalidOperationException("No se encontró el artículo de la prueba.");
        var linea = LineaPresupuesto.DesdeArticulo(articulo, cantidad: 1, porcentajeDescuento: 0m).Linea
            ?? throw new InvalidOperationException("El artículo de la prueba no da una línea válida.");
        servicios.GetRequiredService<IPresupuestoRepository>().Agregar(Presupuesto.Nuevo(
            numero, new DateOnly(2026, 10, 30), new ClientePresupuesto(Apellido, Nombre, Dni, null, null, null), [linea]));
        await servicios.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
    }

    /// <summary>
    /// Deja el contador del simulador de ARCA para la Factura B del punto de venta 3 en <paramref name="ultimo"/>: la
    /// próxima factura lleva el número siguiente.
    /// </summary>
    public Task SimularUltimoNumeroAsync(long ultimo) =>
        File.WriteAllTextAsync(
            Path.Combine(_fabrica.DirectorioDatos, SimuladorArca.NombreArchivo),
            "{\"contadores\":{\"B-3\":" + ultimo.ToString(CultureInfo.InvariantCulture) + "}}");

    /// <summary>Graba los cinco datos del emisor (<see cref="CuerpoEmisor"/>).</summary>
    public async Task CargarEmisorAsync()
    {
        var respuesta = await Http.PostJsonAsync(Cliente, "/api/configuracion/emisor", CuerpoEmisor, Cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
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
