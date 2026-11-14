using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure;
using Optica.Infrastructure.Arca;
using Optica.Infrastructure.Persistencia;

namespace Optica.Api.Tests.Facturacion;

/// <summary>
/// Spec FEAT-008, Block 6: <c>POST /api/presupuestos/{numero}/factura/verificacion</c>, <c>POST
/// /api/presupuestos/{numero}/factura/descarte</c>, <c>GET /api/facturacion/emisiones-pendientes</c> y la observación en
/// el GET del presupuesto, de punta a punta con la base SQLite y ARCA simulada. Las pendientes de FEAT-007 y las de otro
/// entorno se cargan por SQL respetando los triggers (D-06): solo INSERT, nunca UPDATE. Datos del cliente ficticios (DNI
/// que empieza con 00, email @example.com).
/// </summary>
public sealed class VerificacionApiTests
{
    private const string Ruta = EscenarioFacturacion.Ruta;
    private const string RutaPendientes = "/api/facturacion/emisiones-pendientes";
    private const string TituloVerificacion = "No se pudo verificar la emisión.";
    private const string TituloDescarte = "No se pudo descartar la emisión.";
    private const string RespuestaAutorizarSinResponder = "autorizar_sin_responder";
    private const string RespuestaNoResponderUnaVez = "no_responder_una_vez";

    private static readonly string[] CamposFactura =
        ["comprobante", "tipo", "numero", "cae", "vencimientoCae", "fecha", "total", "neto", "iva"];

    private static readonly string[] CamposObservacion =
        ["motivo", "comprobante", "importeRegistrado", "importeArca", "entorno"];

    /// <summary>AC-05: ARCA autorizó el número 1 y no respondió; la verificación lo recupera y responde 200.</summary>
    [Fact]
    public async Task Verificacion_Recuperada_200ConFactura()
    {
        TaskCompletionSource? llamado = null;
        using var fabrica = new FabricaApi(
            servicios => llamado = AutorizadorConSenal.Envolver(servicios), respuestaArca: RespuestaAutorizarSinResponder);
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await DejarPendienteSinRespuestaAsync(fabrica, escenario, numero, llamado!);
        fabrica.Reloj.Advance(TimeSpan.FromSeconds(60));

        var respuesta = await VerificarAsync(escenario, numero);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("application/json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.True(respuesta.Headers.CacheControl?.NoStore ?? false, "El 200 de la verificación no trae no-store.");
        var json = await Http.JsonAsync(respuesta);
        Assert.Equal(["resultado", "factura"], Nombres(json));
        Assert.Equal("recuperada", json.GetProperty("resultado").GetString());
        var factura = json.GetProperty("factura");
        Assert.Equal(CamposFactura, Nombres(factura));
        Assert.Equal("Factura B 0003-00000001", factura.GetProperty("comprobante").GetString());
        Assert.Equal("b", factura.GetProperty("tipo").GetString());
        Assert.Equal(1, factura.GetProperty("numero").GetInt64());
        Assert.Matches("^[0-9]{14}$", factura.GetProperty("cae").GetString());
        Assert.Equal(1000m, factura.GetProperty("total").GetDecimal());

        var leido = await escenario.LeerAsync(numero);
        Assert.Equal("Factura B 0003-00000001", leido.GetProperty("factura").GetProperty("comprobante").GetString());
        Assert.False(leido.GetProperty("emisionPendiente").GetBoolean());
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("observacionEmision").ValueKind);
        Assert.Equal([(1L, 100000L)], await ComprobantesSimuladosAsync(fabrica));
    }

    /// <summary>
    /// AC-07 (sad path): ARCA tiene el número 1 con $ 2.000 y la venta registró $ 1.000: 409 <c>importe_distinto</c> con el
    /// comprobante y los dos importes; nada se emite ni se recupera, y el GET muestra la observación.
    /// </summary>
    [Fact]
    public async Task Verificacion_ImporteDistinto_409ConImportes()
    {
        TaskCompletionSource? llamado = null;
        using var fabrica = new FabricaApi(
            servicios => llamado = AutorizadorConSenal.Envolver(servicios), respuestaArca: RespuestaAutorizarSinResponder);
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await DejarPendienteSinRespuestaAsync(fabrica, escenario, numero, llamado!);
        await CambiarTotalSimuladoAsync(fabrica, 100000, 200000);
        fabrica.Reloj.Advance(TimeSpan.FromSeconds(60));

        var respuesta = await VerificarAsync(escenario, numero);

        var json = await AfirmarConflictoAsync(
            respuesta, TituloVerificacion, "importe_distinto", "comprobante", "importeRegistrado", "importeArca");
        Assert.Equal("Factura B 0003-00000001", json.GetProperty("comprobante").GetString());
        Assert.Equal(1000m, json.GetProperty("importeRegistrado").GetDecimal());
        Assert.Equal(2000m, json.GetProperty("importeArca").GetDecimal());

        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("factura").ValueKind);
        Assert.True(leido.GetProperty("emisionPendiente").GetBoolean());
        var observacion = leido.GetProperty("observacionEmision");
        Assert.Equal(CamposObservacion, Nombres(observacion));
        Assert.Equal("importe_distinto", observacion.GetProperty("motivo").GetString());
        Assert.Equal("Factura B 0003-00000001", observacion.GetProperty("comprobante").GetString());
        Assert.Equal(1000m, observacion.GetProperty("importeRegistrado").GetDecimal());
        Assert.Equal(2000m, observacion.GetProperty("importeArca").GetDecimal());
        Assert.Equal("simulado", observacion.GetProperty("entorno").GetString());
        Assert.Equal([(1L, 200000L)], await ComprobantesSimuladosAsync(fabrica));
    }

    /// <summary>
    /// AC-15 (sad path): una pendiente grabada por FEAT-007 (columnas nuevas nulas, cargada por SQL, D-06) responde 409
    /// <c>sin_numero</c> sin consultar a ARCA; el GET muestra la observación sin comprobante y se puede descartar (D-03).
    /// </summary>
    [Fact]
    public async Task Verificacion_PendienteSinNumero_409SinNumero()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await InsertarPendienteFeat007Async(fabrica, numero, 100000);

        var respuesta = await VerificarAsync(escenario, numero);

        await AfirmarConflictoAsync(respuesta, TituloVerificacion, "sin_numero");
        Assert.False(File.Exists(ArchivoSimulador(fabrica)));
        var observacion = (await escenario.LeerAsync(numero)).GetProperty("observacionEmision");
        Assert.Equal(CamposObservacion, Nombres(observacion));
        Assert.Equal("sin_numero", observacion.GetProperty("motivo").GetString());
        Assert.Equal(JsonValueKind.Null, observacion.GetProperty("comprobante").ValueKind);
        Assert.Equal(1000m, observacion.GetProperty("importeRegistrado").GetDecimal());
        Assert.Equal(JsonValueKind.Null, observacion.GetProperty("importeArca").ValueKind);
        Assert.Equal("simulado", observacion.GetProperty("entorno").GetString());

        var descarte = await DescartarAsync(escenario, numero);
        Assert.Equal(HttpStatusCode.NoContent, descarte.StatusCode);
        Assert.False((await escenario.LeerAsync(numero)).GetProperty("emisionPendiente").GetBoolean());
    }

    /// <summary>
    /// AC-16 (sad path): una pendiente con número enviada a homologación (cargada por SQL, D-06) con el sistema en
    /// simulado responde 409 <c>otro_entorno</c> con el entorno de la pendiente, sin consultar a ARCA.
    /// </summary>
    [Fact]
    public async Task Verificacion_PendienteOtroEntorno_409OtroEntorno()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await InsertarPendienteConNumeroAsync(
            fabrica, numero, 125, FabricaApi.Inicio.AddDays(-1), entorno: "homologacion");

        var respuesta = await VerificarAsync(escenario, numero);

        var json = await AfirmarConflictoAsync(respuesta, TituloVerificacion, "otro_entorno", "entorno");
        Assert.Equal("homologacion", json.GetProperty("entorno").GetString());
        Assert.False(File.Exists(ArchivoSimulador(fabrica)));
        var observacion = (await escenario.LeerAsync(numero)).GetProperty("observacionEmision");
        Assert.Equal("otro_entorno", observacion.GetProperty("motivo").GetString());
        Assert.Equal("Factura B 0003-00000125", observacion.GetProperty("comprobante").GetString());
        Assert.Equal(JsonValueKind.Null, observacion.GetProperty("importeArca").ValueKind);
        Assert.Equal("homologacion", observacion.GetProperty("entorno").GetString());
    }

    /// <summary>AC-17 (sad path): a los 20 s de quedar pendiente, 409 <c>espera</c> con 40 segundos, sin llamar a ARCA.</summary>
    [Fact]
    public async Task Verificacion_Antes60s_409Espera()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await InsertarPendienteConNumeroAsync(fabrica, numero, 1, fabrica.Reloj.GetUtcNow().AddSeconds(-20));

        var respuesta = await VerificarAsync(escenario, numero);

        var json = await AfirmarConflictoAsync(respuesta, TituloVerificacion, "espera", "segundosRestantes");
        Assert.Equal(40, json.GetProperty("segundosRestantes").GetInt32());
        Assert.False(File.Exists(ArchivoSimulador(fabrica)));
        var leido = await escenario.LeerAsync(numero);
        Assert.True(leido.GetProperty("emisionPendiente").GetBoolean());
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("observacionEmision").ValueKind);
    }

    /// <summary>
    /// AC-10 (sad path): descartar una pendiente que no se verificó (sin observación) responde 409
    /// <c>sin_observacion</c> y la pendiente queda; sin pendiente, 409 <c>sin_pendiente</c>.
    /// </summary>
    [Fact]
    public async Task Descarte_DirectoSinVerificar_409SinObservacion()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        var sinPendiente = await escenario.FinalAsync(500m);
        await InsertarPendienteFeat007Async(fabrica, numero, 100000);

        var respuesta = await DescartarAsync(escenario, numero);

        await AfirmarConflictoAsync(respuesta, TituloDescarte, "sin_observacion");
        Assert.True((await escenario.LeerAsync(numero)).GetProperty("emisionPendiente").GetBoolean());
        Assert.Equal(0, await ContarDescartesAsync(fabrica, numero));

        await AfirmarConflictoAsync(await DescartarAsync(escenario, sinPendiente), TituloDescarte, "sin_pendiente");
    }

    /// <summary>
    /// AC-08: con la observación de importe distinto, el descarte responde 204 sin cuerpo, queda registrado y el GET ya
    /// no tiene pendiente ni observación (Facturar vuelve a estar disponible).
    /// </summary>
    [Fact]
    public async Task Descarte_TrasObservacion_204YGetSinPendiente()
    {
        TaskCompletionSource? llamado = null;
        using var fabrica = new FabricaApi(
            servicios => llamado = AutorizadorConSenal.Envolver(servicios), respuestaArca: RespuestaAutorizarSinResponder);
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await DejarPendienteSinRespuestaAsync(fabrica, escenario, numero, llamado!);
        await CambiarTotalSimuladoAsync(fabrica, 100000, 200000);
        fabrica.Reloj.Advance(TimeSpan.FromSeconds(60));
        await AfirmarConflictoAsync(
            await VerificarAsync(escenario, numero), TituloVerificacion, "importe_distinto", "comprobante",
            "importeRegistrado", "importeArca");

        var respuesta = await DescartarAsync(escenario, numero);

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.True(respuesta.Headers.CacheControl?.NoStore ?? false, "El 204 del descarte no trae no-store.");
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        var leido = await escenario.LeerAsync(numero);
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("factura").ValueKind);
        Assert.False(leido.GetProperty("emisionPendiente").GetBoolean());
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("observacionEmision").ValueKind);
        Assert.Equal(1, await ContarDescartesAsync(fabrica, numero));
        Assert.Equal("{\"presupuestos\":[]}", await LeerPendientesAsync(escenario));
    }

    /// <summary>
    /// Sad path: sin sesión, las tres rutas nuevas responden 401 sin cuerpo (también con un cuerpo de más de 1 KB, antes
    /// del 413) y no tocan nada.
    /// </summary>
    [Fact]
    public async Task RutasNuevas_SinSesion_401()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await InsertarPendienteFeat007Async(fabrica, numero, 100000);
        var cliente = fabrica.CrearClienteSinCookies();
        var grande = new string(' ', 2048);

        var pedidos = new (string Descripcion, Task<HttpResponseMessage> Pedido)[]
        {
            ("POST verificacion", Http.PostJsonAsync(cliente, $"{Ruta}/{numero}/factura/verificacion", string.Empty)),
            ("POST verificacion grande", Http.PostJsonAsync(cliente, $"{Ruta}/{numero}/factura/verificacion", grande)),
            ("POST descarte", Http.PostJsonAsync(cliente, $"{Ruta}/{numero}/factura/descarte", string.Empty)),
            ("POST descarte grande", Http.PostJsonAsync(cliente, $"{Ruta}/{numero}/factura/descarte", grande)),
            ("GET pendientes", Http.GetAsync(cliente, RutaPendientes)),
        };

        foreach (var (descripcion, pedido) in pedidos)
        {
            var respuesta = await pedido;
            Assert.True(
                respuesta.StatusCode == HttpStatusCode.Unauthorized,
                $"{descripcion} respondió {(int)respuesta.StatusCode} sin sesión; se esperaba 401.");
            Assert.Null(respuesta.Headers.Location);
            Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
        }

        var leido = await escenario.LeerAsync(numero);
        Assert.True(leido.GetProperty("emisionPendiente").GetBoolean());
        Assert.Equal(JsonValueKind.Null, leido.GetProperty("observacionEmision").ValueKind);
    }

    /// <summary>Sad path: verificar o descartar un presupuesto inexistente responde 404 con el título fijo.</summary>
    [Fact]
    public async Task Verificacion_PresupuestoInexistente_404()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);

        foreach (var respuesta in new[] { await VerificarAsync(escenario, 999), await DescartarAsync(escenario, 999) })
        {
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                "No se encontró el presupuesto.", (await Http.JsonAsync(respuesta)).GetProperty("title").GetString());
        }

        Assert.False(File.Exists(ArchivoSimulador(fabrica)));
    }

    /// <summary>
    /// Sad path: la verificación y el descarte aceptan hasta 1 KB de cuerpo (que no se lee): un byte más, 413; en el
    /// límite exacto el pedido llega a la acción, que responde el 404 del presupuesto inexistente.
    /// </summary>
    [Fact]
    public async Task Verificacion_CuerpoGrande_413()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);

        foreach (var accion in new[] { "verificacion", "descarte" })
        {
            var ruta = $"{Ruta}/999/factura/{accion}";
            var rechazo = await Http.PostJsonAsync(escenario.Cliente, ruta, CuerpoDeBytes(1025), escenario.Cookie);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rechazo.StatusCode);

            var aceptado = await Http.PostJsonAsync(escenario.Cliente, ruta, CuerpoDeBytes(1024), escenario.Cookie);
            Assert.Equal(HttpStatusCode.NotFound, aceptado.StatusCode);
        }
    }

    /// <summary>
    /// Error (sad path): el autorizador lanza una excepción en la consulta, aunque su contrato dice que no lanza; responde
    /// el 500 genérico sin el texto de la falla y la pendiente queda. El caso "resultado no previsto" del traductor (null o
    /// un valor fuera de los previstos) lo cubren las pruebas de <c>CierrePorDefectoPresupuestosTests</c>.
    /// </summary>
    [Fact]
    public async Task Verificacion_AutorizadorLanza_Error500()
    {
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IAutorizadorComprobantes>();
            servicios.AddSingleton<IAutorizadorComprobantes>(new AutorizadorConsultaQueLanza());
        });
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await InsertarPendienteConNumeroAsync(fabrica, numero, 1, FabricaApi.Inicio.AddDays(-1));

        var respuesta = await VerificarAsync(escenario, numero);

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(AutorizadorConsultaQueLanza.Texto, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(EscenarioFacturacion.Dni, texto, StringComparison.Ordinal);
        Assert.True((await escenario.LeerAsync(numero)).GetProperty("emisionPendiente").GetBoolean());
    }

    /// <summary>
    /// AC-03 y AC-04: sin pendientes, <c>{"presupuestos":[]}</c>; con pendientes en dos presupuestos, solo sus números de
    /// menor a mayor, sin ningún otro dato.
    /// </summary>
    [Fact]
    public async Task EmisionesPendientes_SoloNumeros()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var primero = await escenario.FinalAsync(1000m);
        _ = await escenario.FinalAsync(1000m);
        var tercero = await escenario.FinalAsync(1000m);

        var vacia = await Http.GetAsync(escenario.Cliente, RutaPendientes, escenario.Cookie);
        Assert.Equal(HttpStatusCode.OK, vacia.StatusCode);
        Assert.Equal("application/json", vacia.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"presupuestos\":[]}", await vacia.Content.ReadAsStringAsync());

        await InsertarPendienteConNumeroAsync(fabrica, tercero, 2, FabricaApi.Inicio.AddDays(-1));
        await InsertarPendienteFeat007Async(fabrica, primero, 100000);

        var texto = await LeerPendientesAsync(escenario);
        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"{{\"presupuestos\":[{primero},{tercero}]}}"), texto);
    }

    /// <summary>
    /// AC-02 y R-01: el GET de un presupuesto con pendiente y observación lleva <c>emisionPendiente</c> y la observación
    /// con sus cinco campos, sin datos del cliente; ni la respuesta de la verificación ni el log llevan el DNI.
    /// </summary>
    [Fact]
    public async Task Get_ConObservacion_SinDni()
    {
        using var fabrica = new FabricaApi();
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await InsertarPendienteFeat007Async(fabrica, numero, 100000);

        var verificacion = await VerificarAsync(escenario, numero);
        var cuerpoVerificacion = await verificacion.Content.ReadAsStringAsync();
        var leido = await escenario.LeerAsync(numero);

        Assert.Equal(HttpStatusCode.Conflict, verificacion.StatusCode);
        Assert.True(leido.GetProperty("emisionPendiente").GetBoolean());
        var observacion = leido.GetProperty("observacionEmision");
        Assert.Equal(CamposObservacion, Nombres(observacion));
        foreach (var dato in new[]
                 {
                     EscenarioFacturacion.Dni, EscenarioFacturacion.Apellido, EscenarioFacturacion.Nombre,
                     EscenarioFacturacion.Email, EscenarioFacturacion.Telefono,
                 })
        {
            Assert.DoesNotContain(dato, observacion.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(dato, cuerpoVerificacion, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            fabrica.Registro.Lineas,
            linea => linea.TextoCompleto.Contains(EscenarioFacturacion.Dni, StringComparison.Ordinal));
    }

    /// <summary>
    /// AC-13 y NFR-02 contra el simulador real (D-30): la recuperación (FR-05) y la reemisión (FR-06) dejan a lo sumo un
    /// comprobante autorizado por presupuesto en el archivo del simulador, y volver a verificar o a facturar no autoriza
    /// otro.
    /// </summary>
    [Fact]
    public async Task Verificacion_RecuperacionYReemision_UnComprobantePorPresupuesto()
    {
        // Recuperación: ARCA autorizó el 1 sin responder; la verificación lo recupera sin pedir otro.
        TaskCompletionSource? llamadoRecupera = null;
        using (var fabrica = new FabricaApi(
                   servicios => llamadoRecupera = AutorizadorConSenal.Envolver(servicios),
                   respuestaArca: RespuestaAutorizarSinResponder))
        {
            var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
            var numero = await escenario.FinalAsync(1000m);
            await DejarPendienteSinRespuestaAsync(fabrica, escenario, numero, llamadoRecupera!);
            fabrica.Reloj.Advance(TimeSpan.FromSeconds(60));

            Assert.Equal(HttpStatusCode.OK, (await VerificarAsync(escenario, numero)).StatusCode);
            await AfirmarConflictoAsync(await VerificarAsync(escenario, numero), TituloVerificacion, "sin_pendiente");
            Assert.Equal(HttpStatusCode.Conflict, (await escenario.EmitirAsync(numero)).StatusCode);

            Assert.Equal([(1L, 100000L)], await ComprobantesSimuladosAsync(fabrica));
            Assert.Equal([(numero, 1L)], await FacturasAsync(fabrica));
        }

        // Reemisión: ARCA no tiene el 1 (la primera solicitud no respondió y no se autorizó); la verificación lo consulta
        // y emite con el número que da ARCA. Otro presupuesto factura después con el siguiente.
        TaskCompletionSource? llamadoReemite = null;
        using (var fabrica = new FabricaApi(
                   servicios => llamadoReemite = AutorizadorConSenal.Envolver(servicios),
                   respuestaArca: RespuestaNoResponderUnaVez))
        {
            var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
            var numero = await escenario.FinalAsync(1000m);
            var otro = await escenario.FinalAsync(1500m);
            await DejarPendienteSinRespuestaAsync(fabrica, escenario, numero, llamadoReemite!);
            fabrica.Reloj.Advance(TimeSpan.FromSeconds(60));

            var reemitida = await VerificarAsync(escenario, numero);
            Assert.Equal(HttpStatusCode.OK, reemitida.StatusCode);
            Assert.Equal("emitida", (await Http.JsonAsync(reemitida)).GetProperty("resultado").GetString());
            Assert.Equal(HttpStatusCode.Created, (await escenario.EmitirAsync(otro)).StatusCode);
            await AfirmarConflictoAsync(await VerificarAsync(escenario, numero), TituloVerificacion, "sin_pendiente");

            Assert.Equal([(1L, 100000L), (2L, 150000L)], await ComprobantesSimuladosAsync(fabrica));
            Assert.Equal([(numero, 1L), (otro, 2L)], await FacturasAsync(fabrica));
        }
    }

    /// <summary>
    /// D-32 (rechazo, sad path): ARCA no tiene el número y rechaza la reemisión: 409 <c>rechazada</c> con
    /// <c>codigoArca</c> y <c>descripcionArca</c>; la Api no registra el cuerpo de la respuesta (la descripción no está en
    /// el log) y la pendiente se quitó (D-29).
    /// </summary>
    [Fact]
    public async Task Verificacion_Rechazada_409ConCodigoSinRegistrarElCuerpo()
    {
        using var fabrica = new FabricaApi(respuestaArca: FabricaApi.RespuestaArcaRechazar);
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var numero = await escenario.FinalAsync(1000m);
        await InsertarPendienteConNumeroAsync(fabrica, numero, 1, FabricaApi.Inicio.AddDays(-1));

        var respuesta = await VerificarAsync(escenario, numero);

        var json = await AfirmarConflictoAsync(
            respuesta, TituloVerificacion, "rechazada", "codigoArca", "descripcionArca");
        Assert.Equal(SimuladorArca.CodigoRechazo, json.GetProperty("codigoArca").GetInt32());
        Assert.Equal(SimuladorArca.DescripcionRechazo, json.GetProperty("descripcionArca").GetString());
        Assert.False((await escenario.LeerAsync(numero)).GetProperty("emisionPendiente").GetBoolean());
        Assert.DoesNotContain(
            fabrica.Registro.Lineas,
            linea => linea.TextoCompleto.Contains(SimuladorArca.DescripcionRechazo, StringComparison.Ordinal)
                || linea.TextoCompleto.Contains(TituloVerificacion, StringComparison.Ordinal));
    }

    /// <summary>
    /// D-34 (rechazo, sad path; datos personales): ARCA rechaza con una descripción que repite el DNI ficticio del
    /// receptor en dos formas. Ni el 409 de la emisión (FEAT-007) ni el de la verificación lo llevan; el código sí.
    /// </summary>
    [Fact]
    public async Task Rechazo_DescripcionConDni_409SinDni()
    {
        using var fabrica = new FabricaApi(servicios =>
        {
            servicios.RemoveAll<IAutorizadorComprobantes>();
            servicios.AddSingleton<IAutorizadorComprobantes>(new AutorizadorQueRechazaConDni());
        });
        var escenario = await EscenarioFacturacion.CrearAsync(fabrica);
        var emitido = await escenario.FinalAsync(150_000m);
        var verificado = await escenario.FinalAsync(1000m);
        await InsertarPendienteConNumeroAsync(fabrica, verificado, 1, FabricaApi.Inicio.AddDays(-1));

        var emision = await escenario.EmitirAsync(emitido);
        var verificacion = await VerificarAsync(escenario, verificado);

        Assert.Equal(HttpStatusCode.Conflict, emision.StatusCode);
        var jsonEmision = await Http.JsonAsync(emision);
        Assert.Equal("rechazada", jsonEmision.GetProperty("motivo").GetString());
        Assert.Equal(10015, jsonEmision.GetProperty("codigo").GetInt32());
        var jsonVerificacion = await AfirmarConflictoAsync(
            verificacion, TituloVerificacion, "rechazada", "codigoArca", "descripcionArca");
        Assert.Equal(10015, jsonVerificacion.GetProperty("codigoArca").GetInt32());
        foreach (var texto in new[] { jsonEmision.GetRawText(), jsonVerificacion.GetRawText() })
        {
            Assert.DoesNotContain("718294", texto.Replace(".", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Contains("DocNro", texto, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// D-31: la espera de verificación tiene que alcanzar el plazo de ARCA más el margen del envío. 60/30 (producción) y
    /// 3/2 (harness e2e) arrancan; 39/30 y 2/2 son un error de configuración con texto fijo, sin el valor recibido.
    /// </summary>
    [Theory]
    [InlineData(60, 30, true)]
    [InlineData(40, 30, true)]
    [InlineData(3, 2, true)]
    [InlineData(39, 30, false)]
    [InlineData(2, 2, false)]
    [InlineData(1, 1, false)]
    public void OpcionesVerificacion_EsperaMenorQuePlazoMasMargen_ErrorDeConfiguracion(
        int espera, int plazo, bool valida)
    {
        var opciones = OpcionesArca.Leer(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OpcionesArca.ClaveEsperaVerificacion] = espera.ToString(CultureInfo.InvariantCulture),
                [OpcionesArca.ClaveTiempoMaximo] = plazo.ToString(CultureInfo.InvariantCulture),
            })
            .Build());

        if (valida)
        {
            Assert.Equal(
                TimeSpan.FromSeconds(espera),
                ConfiguracionInfraestructura.CrearOpcionesVerificacion(opciones).EsperaVerificacion);
            return;
        }

        var error = Assert.Throws<OpcionesArcaInvalidasException>(
            () => ConfiguracionInfraestructura.CrearOpcionesVerificacion(opciones));
        Assert.Equal(OpcionesArca.ClaveEsperaVerificacion, error.Clave);
        Assert.Equal(ConfiguracionInfraestructura.TextoEsperaInsuficiente, error.Message);
        Assert.DoesNotContain(espera.ToString(CultureInfo.InvariantCulture) + " ", error.Message, StringComparison.Ordinal);
    }

    /// <summary>D-31: con una espera menor que el plazo más el margen, la Api no arranca y lo registra como crítico.</summary>
    [Fact]
    public void Arranque_EsperaInsuficiente_NoArranca()
    {
        using var fabrica = new FabricaApi();
        using var conEsperaCorta = fabrica.WithWebHostBuilder(constructor => constructor.ConfigureAppConfiguration(
            configuracion => configuracion.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OpcionesArca.ClaveEsperaVerificacion] = "30",
            })));

        var error = Assert.ThrowsAny<Exception>(() => conEsperaCorta.CreateClient());

        Assert.IsType<OpcionesArcaInvalidasException>(error.GetBaseException());
        Assert.Contains(
            fabrica.Registro.Lineas,
            linea => linea.Nivel == Microsoft.Extensions.Logging.LogLevel.Critical
                && linea.Mensaje.Contains(OpcionesArca.ClaveEsperaVerificacion, StringComparison.Ordinal));
    }

    /// <summary>
    /// Emite la factura del presupuesto con el simulador que no responde (o autoriza sin responder) y adelanta el reloj
    /// hasta el plazo: 409 <c>sin_respuesta</c> y la pendiente queda con el número 1, del momento de la emisión.
    /// </summary>
    private static async Task DejarPendienteSinRespuestaAsync(
        FabricaApi fabrica, EscenarioFacturacion escenario, long numero, TaskCompletionSource llamado)
    {
        var pedido = escenario.EmitirAsync(numero);
        await llamado.Task.WaitAsync(TimeSpan.FromSeconds(30));
        fabrica.Reloj.Advance(TimeSpan.FromSeconds(FabricaApi.TiempoMaximoArcaSegundos));
        var respuesta = await pedido.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("sin_respuesta", (await Http.JsonAsync(respuesta)).GetProperty("motivo").GetString());
        Assert.True((await escenario.LeerAsync(numero)).GetProperty("emisionPendiente").GetBoolean());
    }

    private static Task<HttpResponseMessage> VerificarAsync(EscenarioFacturacion escenario, long numero) =>
        Http.PostJsonAsync(escenario.Cliente, $"{Ruta}/{numero}/factura/verificacion", string.Empty, escenario.Cookie);

    private static Task<HttpResponseMessage> DescartarAsync(EscenarioFacturacion escenario, long numero) =>
        Http.PostJsonAsync(escenario.Cliente, $"{Ruta}/{numero}/factura/descarte", string.Empty, escenario.Cookie);

    private static async Task<string> LeerPendientesAsync(EscenarioFacturacion escenario)
    {
        var respuesta = await Http.GetAsync(escenario.Cliente, RutaPendientes, escenario.Cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await respuesta.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Un 409 de la verificación o del descarte: ProblemDetails con el título fijo, un <c>detail</c> no vacío, el
    /// <c>motivo</c> y exactamente las extensiones de datos de <paramref name="extensiones"/>; nunca <c>errors</c>.
    /// </summary>
    private static async Task<JsonElement> AfirmarConflictoAsync(
        HttpResponseMessage respuesta, string titulo, string motivo, params string[] extensiones)
    {
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        var json = (await Http.JsonAsync(respuesta)).Clone();
        Assert.Equal(titulo, json.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("detail").GetString()));
        Assert.Equal(motivo, json.GetProperty("motivo").GetString());
        Assert.False(json.TryGetProperty("errors", out _));
        string[] propias = ["type", "title", "status", "detail", "instance", "traceId", "motivo"];
        Assert.Equal(
            extensiones.Order(StringComparer.Ordinal),
            Nombres(json).Where(nombre => !propias.Contains(nombre)).Order(StringComparer.Ordinal));
        return json;
    }

    private static string[] Nombres(JsonElement objeto) =>
        [.. objeto.EnumerateObject().Select(propiedad => propiedad.Name)];

    private static string ArchivoSimulador(FabricaApi fabrica) =>
        Path.Combine(fabrica.DirectorioDatos, SimuladorArca.NombreArchivo);

    /// <summary>Los comprobantes autorizados de la Factura B punto de venta 3 en el archivo del simulador: número y total.</summary>
    private static async Task<List<(long Numero, long TotalCentavos)>> ComprobantesSimuladosAsync(FabricaApi fabrica)
    {
        using var documento = JsonDocument.Parse(await File.ReadAllTextAsync(ArchivoSimulador(fabrica)));
        if (!documento.RootElement.TryGetProperty("comprobantes", out var comprobantes)
            || !comprobantes.TryGetProperty("B-3", out var lista))
        {
            return [];
        }

        return [.. lista.EnumerateArray().Select(c =>
            (c.GetProperty("numero").GetInt64(), c.GetProperty("totalCentavos").GetInt64()))];
    }

    /// <summary>Cambia el total de un comprobante en el archivo del simulador, como si ARCA lo tuviera con otro importe.</summary>
    private static async Task CambiarTotalSimuladoAsync(FabricaApi fabrica, long antes, long despues)
    {
        var archivo = ArchivoSimulador(fabrica);
        var texto = await File.ReadAllTextAsync(archivo);
        var original = string.Create(CultureInfo.InvariantCulture, $"\"totalCentavos\":{antes}");
        Assert.Contains(original, texto, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            archivo,
            texto.Replace(
                original, string.Create(CultureInfo.InvariantCulture, $"\"totalCentavos\":{despues}"),
                StringComparison.Ordinal));
    }

    /// <summary>Las facturas guardadas: presupuesto y número de comprobante, por presupuesto.</summary>
    private static async Task<List<(long Presupuesto, long Numero)>> FacturasAsync(FabricaApi fabrica)
    {
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
        var conexion = contexto.Database.GetDbConnection();
        await conexion.OpenAsync();
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT \"PresupuestoNumero\", \"Numero\" FROM \"Factura\" ORDER BY \"PresupuestoNumero\"";
        var filas = new List<(long, long)>();
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            filas.Add((lector.GetInt64(0), lector.GetInt64(1)));
        }

        return filas;
    }

    /// <summary>Los descartes registrados del presupuesto (FR-10).</summary>
    private static async Task<int> ContarDescartesAsync(FabricaApi fabrica, long presupuesto)
    {
        using var alcance = fabrica.Services.CreateScope();
        var descartes = await alcance.ServiceProvider.GetRequiredService<IDescarteEmisionRepository>()
            .ListarDescartesAsync(presupuesto);
        return descartes.Count;
    }

    /// <summary>
    /// Una pendiente como la grababa FEAT-007 (AC-15, D-06): sin número, concepto, importes ni receptor (columnas nuevas
    /// nulas), del entorno simulado, de hace un día. Solo INSERT parametrizado: los triggers no se tocan.
    /// </summary>
    private static async Task InsertarPendienteFeat007Async(FabricaApi fabrica, long presupuesto, long totalCentavos)
    {
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
        var momento = Momento(FabricaApi.Inicio.AddDays(-1));
        await contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO \"EmisionPendiente\" (\"PresupuestoNumero\", \"Momento\", \"Tipo\", \"PuntoVenta\", \"Fecha\", \"TotalCentavos\", \"Entorno\") VALUES ({presupuesto}, {momento}, 'b', 3, '2026-10-30', {totalCentavos}, 'simulado')");
    }

    /// <summary>
    /// Una pendiente de FEAT-008 con número, concepto Productos, $ 1.000 (neto 826,45 e IVA 173,55 al 21 %) y consumidor
    /// final sin identificar, del <paramref name="entorno"/> y el <paramref name="momento"/> pedidos (AC-16, AC-17).
    /// </summary>
    private static async Task InsertarPendienteConNumeroAsync(
        FabricaApi fabrica, long presupuesto, long numero, DateTimeOffset momento, string entorno = "simulado")
    {
        using var alcance = fabrica.Services.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<OpticaDbContext>();
        var texto = Momento(momento);
        var fecha = DateOnly.FromDateTime(momento.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await contexto.Database.ExecuteSqlAsync(
            $"INSERT INTO \"EmisionPendiente\" (\"PresupuestoNumero\", \"Momento\", \"Tipo\", \"PuntoVenta\", \"Fecha\", \"TotalCentavos\", \"Entorno\", \"Numero\", \"Concepto\", \"NetoCentavos\", \"IvaCentavos\", \"Alicuota\", \"ReceptorIdentificado\") VALUES ({presupuesto}, {texto}, 'b', 3, {fecha}, 100000, {entorno}, {numero}, 'productos', 82645, 17355, '21', 0)");
    }

    private static string Momento(DateTimeOffset momento) =>
        momento.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Un cuerpo JSON de exactamente <paramref name="bytes"/> bytes (la acción no lo lee).</summary>
    private static string CuerpoDeBytes(int bytes)
    {
        const string Vacio = "{\"r\":\"\"}";
        var cuerpo = "{\"r\":\"" + new string('a', bytes - Encoding.UTF8.GetByteCount(Vacio)) + "\"}";
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(cuerpo));
        return cuerpo;
    }

    /// <summary>
    /// Rechaza toda autorización con una descripción que repite el DNI ficticio del escenario (con y sin puntos); la
    /// consulta responde "no autorizado", así la verificación reemite y recibe el mismo rechazo.
    /// </summary>
    private sealed class AutorizadorQueRechazaConDni : IAutorizadorComprobantes
    {
        public EntornoArca Entorno => EntornoArca.Simulado;

        public TimeSpan TiempoMaximo => TimeSpan.FromSeconds(30);

        public Task<ResultadoProximoNumero> ProximoNumeroAsync(
            TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(2));

        public Task<ResultadoAutorizacion> AutorizarAsync(
            SolicitudComprobante solicitud, long numero, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoAutorizacion>(new ResultadoAutorizacion.Rechazada(
                10015, $"DocNro {EscenarioFacturacion.Dni} invalido (00.718.294)."));

        public Task<ResultadoConsulta> ConsultarAsync(
            TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoConsulta>(new ResultadoConsulta.NoAutorizado());
    }

    /// <summary>Pide números y autoriza como ARCA, pero la consulta lanza una excepción imprevista con un texto marcado.</summary>
    private sealed class AutorizadorConsultaQueLanza : IAutorizadorComprobantes
    {
        public const string Texto = "Zq7-consulta-imprevista";

        public EntornoArca Entorno => EntornoArca.Simulado;

        public TimeSpan TiempoMaximo => TimeSpan.FromSeconds(30);

        public Task<ResultadoProximoNumero> ProximoNumeroAsync(
            TipoComprobante tipo, int puntoVenta, CancellationToken cancelacion) =>
            Task.FromResult<ResultadoProximoNumero>(new ResultadoProximoNumero.Numero(2));

        public Task<ResultadoAutorizacion> AutorizarAsync(
            SolicitudComprobante solicitud, long numero, CancellationToken cancelacion) =>
            throw new InvalidOperationException(Texto);

        public Task<ResultadoConsulta> ConsultarAsync(
            TipoComprobante tipo, int puntoVenta, long numero, CancellationToken cancelacion) =>
            throw new InvalidOperationException(Texto);
    }
}
