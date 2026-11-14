using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Facturacion;

/// <summary>
/// Emite la factura a consumidor final de un presupuesto en Final (FR-01 a FR-04, FR-07, FR-12, FR-13, FR-15, FR-17 a
/// FR-25, FR-27, FR-29 de FEAT-007) con el número registrado antes del envío (FR-01 de FEAT-008, ADR-025):
/// <list type="number">
/// <item>Valida el concepto antes de leer la base: faltante o distinto de <see cref="ConceptoProductos"/> y
/// <see cref="ConceptoProductosYServicios"/>, inválido en <see cref="CampoConcepto"/>.</item>
/// <item>Transacción 1, de solo lectura (con el token del pedido): lee el presupuesto (no encontrado), rechaza un
/// Borrador, uno con factura o con emisión pendiente; lee la configuración vigente y arma la solicitud con la fecha local
/// del día (<see cref="ReglasEmision.Armar"/>: sin tope, sin punto de venta, alícuota no admitida). No graba nada.</item>
/// <item>Un único plazo, <see cref="IAutorizadorComprobantes.TiempoMaximo"/>, medido con el <see cref="TimeProvider"/>
/// desde que se pide el candado de emisión (D-32 de FEAT-007): cubre la espera del candado, el próximo número y la
/// llamada a ARCA. Sin el candado en el plazo, "no enviada" sin llamar a ARCA. El plazo no se enlaza con el token del
/// pedido: si el navegador corta, la emisión sigue.</item>
/// <item>Con el turno tomado, <see cref="ProcesoEmision"/> pide el próximo número, graba la pendiente con él repitiendo
/// las comprobaciones (D-08), controla el margen, pide el CAE y graba el resultado (D-09). El turno se libera cuando el
/// proceso termina, con la factura o la baja de la pendiente ya grabadas.</item>
/// </list>
/// Ninguna rama reintenta por su cuenta.
/// </summary>
/// <remarks>
/// El log (R-07) nunca lleva el DNI, la solicitud, los datos del cliente ni la descripción de un rechazo de ARCA, ni
/// adjunta excepciones (su texto o sus <c>Entries</c> pueden llevar el DNI).
/// </remarks>
public sealed class EmitirFactura(
    IPresupuestoRepository presupuestos,
    IConfiguracionNegocioRepository configuracion,
    IFacturaRepository facturas,
    IEmisionPendienteRepository pendientes,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAutorizadorComprobantes autorizador,
    ICandadoEmision candado,
    ProcesoEmision proceso,
    TimeProvider reloj,
    ILogger<EmitirFactura> registro)
{
    /// <summary>El campo del concepto en los errores (FR-08).</summary>
    public const string CampoConcepto = "concepto";

    /// <summary>El texto del concepto Productos.</summary>
    public const string ConceptoProductos = "productos";

    /// <summary>El texto del concepto Productos y servicios.</summary>
    public const string ConceptoProductosYServicios = "productos_y_servicios";

    /// <summary>La acción de un concepto faltante o desconocido; no repite lo recibido.</summary>
    public const string AccionConceptoInvalido = "Elegí el concepto de la factura: Productos o Productos y servicios.";

    private const string MotivoOtraEmision =
        "Otra emisión ocupó el plazo de espera: la solicitud no se envió a ARCA.";

    public async Task<ResultadoEmision> EjecutarAsync(long numero, string? concepto, CancellationToken cancelacion = default)
    {
        if (LeerConcepto(concepto) is not { } conceptoFactura)
        {
            return ResultadoEmision.Invalido([new ErrorValidacion(CampoConcepto, AccionConceptoInvalido)]);
        }

        var preparada = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => PrepararAsync(numero, conceptoFactura, operacion), cancelacion);
        if (preparada.Rechazo is { } rechazo)
        {
            return rechazo;
        }

        using var plazo = PlazoEmision.Iniciar(autorizador.TiempoMaximo, reloj);
        var turno = await candado.TomarAsync(plazo.Token);
        if (turno is null)
        {
            // El motivo es un texto fijo, sin datos del cliente.
            registro.LogWarning(
                "La factura del presupuesto {Presupuesto} no se envió a ARCA: {Motivo}", numero, MotivoOtraEmision);
            return ResultadoEmision.Conflicto(MotivoConflictoEmision.NoEnviada);
        }

        try
        {
            return await proceso.EjecutarAsync(preparada.Presupuesto!, preparada.Solicitud!, turno, plazo);
        }
        finally
        {
            Liberar(turno);
        }
    }

    private static ConceptoFactura? LeerConcepto(string? concepto) => concepto switch
    {
        ConceptoProductos => ConceptoFactura.Productos,
        ConceptoProductosYServicios => ConceptoFactura.ProductosYServicios,
        _ => null,
    };

    /// <summary>Transacción 1: valida contra lo guardado y arma la solicitud; no graba nada.</summary>
    private async Task<Preparacion> PrepararAsync(long numero, ConceptoFactura concepto, CancellationToken operacion)
    {
        var presupuesto = await presupuestos.ObtenerAsync(numero, operacion);
        if (presupuesto is null)
        {
            return Preparacion.Rechazada(ResultadoEmision.NoEncontrado);
        }

        if (presupuesto.Estado != EstadoPresupuesto.Final)
        {
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(MotivoConflictoEmision.Borrador));
        }

        if (await facturas.ExisteParaPresupuestoAsync(numero, operacion))
        {
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(MotivoConflictoEmision.Facturado));
        }

        if (await pendientes.ExisteAsync(numero, operacion))
        {
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(MotivoConflictoEmision.Pendiente));
        }

        var vigente = await configuracion.ObtenerAsync(operacion);
        var fecha = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
        var armado = ReglasEmision.Armar(presupuesto, vigente, concepto, fecha);
        if (!armado.EsArmada)
        {
            return Preparacion.Rechazada(ResultadoEmision.Conflicto(MotivoDe(armado.Motivo.Value)));
        }

        return new Preparacion(presupuesto, armado.Solicitud, null);
    }

    private static MotivoConflictoEmision MotivoDe(MotivoEmisionRechazada motivo) => motivo switch
    {
        MotivoEmisionRechazada.Borrador => MotivoConflictoEmision.Borrador,
        MotivoEmisionRechazada.SinTope => MotivoConflictoEmision.SinTope,
        MotivoEmisionRechazada.SinPuntoVenta => MotivoConflictoEmision.SinPuntoVenta,
        MotivoEmisionRechazada.AlicuotaNoAdmitida => MotivoConflictoEmision.AlicuotaNoAdmitida,
        _ => throw new InvalidOperationException("Motivo de rechazo de la emisión no previsto."),
    };

    /// <summary>
    /// Libera el turno. Si la Api se está apagando y el candado ya se descartó, lo tolera: una factura que ARCA autorizó
    /// no se pierde por no poder liberar un candado que ya no existe (WARN-1 de la auditoría de FEAT-007).
    /// </summary>
    private static void Liberar(IDisposable turno)
    {
        try
        {
            turno.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Intencional: el candado ya no existe; no hay turno que devolver.
        }
    }

    /// <summary>Lo que deja la transacción 1: el presupuesto y la solicitud, o el rechazo.</summary>
    private sealed record Preparacion(
        Presupuesto? Presupuesto, SolicitudComprobante? Solicitud, ResultadoEmision? Rechazo)
    {
        public static Preparacion Rechazada(ResultadoEmision rechazo) => new(null, null, rechazo);

        // Lleva el DNI del receptor dentro de la solicitud: sin valores en ToString (Ley 25.326).
        private bool PrintMembers(System.Text.StringBuilder constructor) => false;
    }
}
