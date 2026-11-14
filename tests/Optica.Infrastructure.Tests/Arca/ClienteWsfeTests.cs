using System.Net;
using System.Xml.Linq;
using Optica.Application.Facturacion;
using Optica.Domain.Facturacion;
using Optica.Infrastructure.Arca;

namespace Optica.Infrastructure.Tests.Arca;

/// <summary>
/// Spec FEAT-007, Block 6: el SOAP de <c>FECompUltimoAutorizado</c> y <c>FECAESolicitar</c> que se envía a WSFEv1 de
/// homologación, con los códigos de D-20, importes con punto y 2 decimales (NFR-04), receptor por tope (FR-10, FR-11),
/// IVA solo en la B (FR-14, FR-16) y fechas de servicio con Productos y servicios (FR-09). Sin red.
/// </summary>
public sealed class ClienteWsfeTests : IDisposable
{
    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";

    private readonly EscenarioArca _escenario = new();

    public void Dispose() => _escenario.Dispose();

    /// <summary>AC-11 (DNI sobre el tope) y AC-14 (RI, 21 %, total 1.815: neto 1.500 e IVA 315).</summary>
    [Fact]
    public async Task Wsfe_FacturaB_XmlConIvaYDni()
    {
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaB,
            3,
            SolicitudesArca.Fecha,
            ConceptoFactura.Productos,
            ReceptorFactura.ConDni(EscenarioArca.Dni),
            ImportesFactura.ParaFacturaB(1815m, 21m));

        var resultado = await _escenario.CrearAutorizador().AutorizarAsync(solicitud, _escenario.Abierto);

        Assert.IsType<ResultadoAutorizacion.Autorizada>(resultado);

        // Primero el último número del tipo y punto de venta, autenticado con el ticket de WSAA y el CUIT.
        var ultimo = Assert.Single(_escenario.Manejador.PedidosDe("FECompUltimoAutorizado"));
        Assert.Equal("https://wswhomo.afip.gov.ar/wsfev1/service.asmx", ultimo.Url.ToString());
        Assert.Equal("http://ar.gov.afip.dif.FEV1/FECompUltimoAutorizado", ultimo.AccionSoap);
        Assert.Equal(EscenarioArca.Token, ultimo.Valor("Token"));
        Assert.Equal(EscenarioArca.Firma, ultimo.Valor("Sign"));
        Assert.Equal(EscenarioArca.Cuit, ultimo.Valor("Cuit"));
        Assert.Equal("3", ultimo.Valor("PtoVta"));
        Assert.Equal("6", ultimo.Valor("CbteTipo"));

        var pedido = Assert.Single(_escenario.Manejador.PedidosDe("FECAESolicitar"));
        Assert.Equal("https://wswhomo.afip.gov.ar/wsfev1/service.asmx", pedido.Url.ToString());
        Assert.Equal("http://ar.gov.afip.dif.FEV1/FECAESolicitar", pedido.AccionSoap);
        Assert.Equal(EscenarioArca.Token, pedido.Valor("Token"));
        Assert.Equal(EscenarioArca.Firma, pedido.Valor("Sign"));
        Assert.Equal(EscenarioArca.Cuit, pedido.Valor("Cuit"));

        var cabecera = pedido.Xml.Descendants(Fev1 + "FeCabReq").Single();
        Assert.Equal("1", cabecera.Element(Fev1 + "CantReg")!.Value);
        Assert.Equal("3", cabecera.Element(Fev1 + "PtoVta")!.Value);
        Assert.Equal("6", cabecera.Element(Fev1 + "CbteTipo")!.Value);

        // El detalle, en el orden del WSDL de WSFEv1.
        var detalle = pedido.Xml.Descendants(Fev1 + "FECAEDetRequest").Single();
        Assert.Equal(
            [
                "Concepto", "DocTipo", "DocNro", "CbteDesde", "CbteHasta", "CbteFch", "ImpTotal", "ImpTotConc", "ImpNeto",
                "ImpOpEx", "ImpTrib", "ImpIVA", "MonId", "MonCotiz", "CondicionIVAReceptorId", "Iva",
            ],
            detalle.Elements().Select(elemento => elemento.Name.LocalName).ToArray());
        Assert.All(detalle.Elements(), elemento => Assert.Equal(Fev1, elemento.Name.Namespace));
        Assert.Equal("1", Valor(detalle, "Concepto"));
        Assert.Equal("96", Valor(detalle, "DocTipo"));
        Assert.Equal("718294", Valor(detalle, "DocNro"));
        Assert.Equal("125", Valor(detalle, "CbteDesde"));
        Assert.Equal("125", Valor(detalle, "CbteHasta"));
        Assert.Equal("20261030", Valor(detalle, "CbteFch"));
        Assert.Equal("1815.00", Valor(detalle, "ImpTotal"));
        Assert.Equal("0.00", Valor(detalle, "ImpTotConc"));
        Assert.Equal("1500.00", Valor(detalle, "ImpNeto"));
        Assert.Equal("0.00", Valor(detalle, "ImpOpEx"));
        Assert.Equal("0.00", Valor(detalle, "ImpTrib"));
        Assert.Equal("315.00", Valor(detalle, "ImpIVA"));
        Assert.Equal("PES", Valor(detalle, "MonId"));
        Assert.Equal("1", Valor(detalle, "MonCotiz"));
        Assert.Equal("5", Valor(detalle, "CondicionIVAReceptorId"));

        var alicuota = Assert.Single(detalle.Element(Fev1 + "Iva")!.Elements(Fev1 + "AlicIva"));
        Assert.Equal("5", Valor(alicuota, "Id"));
        Assert.Equal("1500.00", Valor(alicuota, "BaseImp"));
        Assert.Equal("315.00", Valor(alicuota, "Importe"));
    }

    /// <summary>FR-10: sin superar el tope, la B va a "Consumidor Final" sin identificar; AC-15: 2 decimales.</summary>
    [Fact]
    public async Task Wsfe_FacturaB_SinIdentificar_DocTipo99YDocNro0()
    {
        await _escenario.CrearAutorizador().AutorizarAsync(
            new SolicitudComprobante(
                TipoComprobante.FacturaB,
                3,
                SolicitudesArca.Fecha,
                ConceptoFactura.Productos,
                ReceptorFactura.SinIdentificar,
                ImportesFactura.ParaFacturaB(1000m, 21m)),
            _escenario.Abierto);

        var detalle = Assert.Single(_escenario.Manejador.PedidosDe("FECAESolicitar")).Xml
            .Descendants(Fev1 + "FECAEDetRequest").Single();
        Assert.Equal("99", Valor(detalle, "DocTipo"));
        Assert.Equal("0", Valor(detalle, "DocNro"));
        Assert.Equal("1000.00", Valor(detalle, "ImpTotal"));
        Assert.Equal("826.45", Valor(detalle, "ImpNeto"));
        Assert.Equal("173.55", Valor(detalle, "ImpIVA"));
    }

    /// <summary>AC-17: Monotributo, total 1.815, Factura C sin desglose de IVA.</summary>
    [Fact]
    public async Task Wsfe_FacturaC_XmlSinIva()
    {
        _escenario.Manejador.ResponderTexto(
            "FECompUltimoAutorizado",
            EscenarioArca.Respuesta("wsfe-ultimo-ok").Replace("<CbteTipo>6</CbteTipo>", "<CbteTipo>11</CbteTipo>"));
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaC,
            3,
            SolicitudesArca.Fecha,
            ConceptoFactura.Productos,
            ReceptorFactura.SinIdentificar,
            ImportesFactura.ParaFacturaC(1815m));

        await _escenario.CrearAutorizador().AutorizarAsync(solicitud, _escenario.Abierto);

        Assert.Equal("11", Assert.Single(_escenario.Manejador.PedidosDe("FECompUltimoAutorizado")).Valor("CbteTipo"));
        var pedido = Assert.Single(_escenario.Manejador.PedidosDe("FECAESolicitar"));
        Assert.Equal("11", pedido.Xml.Descendants(Fev1 + "FeCabReq").Single().Element(Fev1 + "CbteTipo")!.Value);
        var detalle = pedido.Xml.Descendants(Fev1 + "FECAEDetRequest").Single();
        Assert.Equal("1815.00", Valor(detalle, "ImpTotal"));
        Assert.Equal("1815.00", Valor(detalle, "ImpNeto"));
        Assert.Equal("0.00", Valor(detalle, "ImpIVA"));
        Assert.Equal("99", Valor(detalle, "DocTipo"));
        Assert.Equal("0", Valor(detalle, "DocNro"));
        Assert.Equal("5", Valor(detalle, "CondicionIVAReceptorId"));
        Assert.Null(detalle.Element(Fev1 + "Iva"));
        Assert.Empty(pedido.Xml.Descendants(Fev1 + "AlicIva"));
    }

    /// <summary>AC-07: Productos y servicios lleva las tres fechas iguales a la del comprobante; AC-08: Productos no.</summary>
    [Fact]
    public async Task Wsfe_ProductosYServicios_XmlConFechas()
    {
        var conServicios = new SolicitudComprobante(
            TipoComprobante.FacturaB,
            3,
            SolicitudesArca.Fecha,
            ConceptoFactura.ProductosYServicios,
            ReceptorFactura.SinIdentificar,
            ImportesFactura.ParaFacturaB(1210m, 21m));
        var autorizador = _escenario.CrearAutorizador();

        await autorizador.AutorizarAsync(conServicios, _escenario.Abierto);
        await autorizador.AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        var pedidos = _escenario.Manejador.PedidosDe("FECAESolicitar");
        Assert.Equal(2, pedidos.Count);
        var servicios = pedidos[0].Xml.Descendants(Fev1 + "FECAEDetRequest").Single();
        Assert.Equal("3", Valor(servicios, "Concepto"));
        Assert.Equal("20261030", Valor(servicios, "FchServDesde"));
        Assert.Equal("20261030", Valor(servicios, "FchServHasta"));
        Assert.Equal("20261030", Valor(servicios, "FchVtoPago"));
        var nombres = servicios.Elements().Select(elemento => elemento.Name.LocalName).ToList();
        Assert.True(nombres.IndexOf("ImpIVA") < nombres.IndexOf("FchServDesde")
            && nombres.IndexOf("FchVtoPago") < nombres.IndexOf("MonId"));

        var productos = pedidos[1].Xml.Descendants(Fev1 + "FECAEDetRequest").Single();
        Assert.Equal("1", Valor(productos, "Concepto"));
        Assert.Null(productos.Element(Fev1 + "FchServDesde"));
        Assert.Null(productos.Element(Fev1 + "FchServHasta"));
        Assert.Null(productos.Element(Fev1 + "FchVtoPago"));
    }

    public static TheoryData<string> FallasUltimoAutorizado() => new() { "errors", "http500", "ilegible", "sin-numero" };

    [Theory]
    [MemberData(nameof(FallasUltimoAutorizado))]
    public async Task Wsfe_UltimoAutorizado_Error_NoEnviada(string caso)
    {
        switch (caso)
        {
            case "errors":
                _escenario.Manejador.Responder("FECompUltimoAutorizado", "wsfe-ultimo-error");
                break;
            case "http500":
                _escenario.Manejador.ResponderTexto(
                    "FECompUltimoAutorizado", "error interno", HttpStatusCode.InternalServerError);
                break;
            case "ilegible":
                _escenario.Manejador.ResponderTexto("FECompUltimoAutorizado", "<soap:Envelope sin cerrar");
                break;
            default:
                _escenario.Manejador.ResponderTexto(
                    "FECompUltimoAutorizado",
                    EscenarioArca.Respuesta("wsfe-ultimo-ok").Replace("<CbteNro>124</CbteNro>", "<CbteNro>x</CbteNro>"));
                break;
        }

        var resultado = await _escenario.CrearAutorizador()
            .AutorizarAsync(SolicitudesArca.FacturaB(), _escenario.Abierto);

        // Sin el último número no se envía el pedido de autorización (D-16): no enviada, sin pendiente.
        Assert.Equal(
            AutorizadorWsfe.MotivoUltimoNumero, Assert.IsType<ResultadoAutorizacion.NoEnviada>(resultado).Motivo);
        Assert.Empty(_escenario.Manejador.PedidosDe("FECAESolicitar"));
        Assert.All(_escenario.Registro.Entradas, entrada => Assert.Null(entrada.Excepcion));
    }

    private static string? Valor(XElement padre, string nombre) => padre.Element(Fev1 + nombre)?.Value;
}
