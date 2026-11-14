using System.Reflection;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Domain.Tests.Facturacion;

// Datos ficticios (D-17 de FEAT-003a): ninguno corresponde a una persona real.
public class FacturaTests
{
    private const string Dni = "23456789";
    private const string Cae = "75123456789012";
    private static readonly DateOnly Fecha = new(2026, 10, 30);
    private static readonly DateOnly VencimientoCae = new(2026, 11, 9);

    private static LineaFactura[] Lineas() =>
        [
            new LineaFactura(1, 7, "Armazón metálico", 1000m, 1, 0m, 1000m, 1000m),
            new LineaFactura(2, 9, "Cristal orgánico", 500m, 2, 0m, 500m, 1000m),
        ];

    private static Factura Construir(
        string cae = Cae, long numero = 125, IReadOnlyList<LineaFactura>? lineas = null,
        ImportesFactura? importes = null, TipoComprobante tipo = TipoComprobante.FacturaB, int puntoVenta = 3,
        long presupuestoNumero = 41, DateOnly? vencimiento = null, EntornoArca entorno = EntornoArca.Simulado,
        ConceptoFactura concepto = ConceptoFactura.Productos, DateOnly? fecha = null) =>
        new(
            presupuestoNumero, tipo, puntoVenta, numero, fecha ?? Fecha, concepto,
            ReceptorFactura.ConDni(Dni), importes ?? ImportesFactura.ParaFacturaB(2000m, 21m), cae,
            vencimiento ?? VencimientoCae, entorno, lineas ?? Lineas());

    [Fact]
    public void Factura_CaeInvalido_Rechazado()
    {
        foreach (var cae in new[] { "7512345678901", "751234567890123", "7512345678901A", "", " 7512345678901" })
        {
            var error = Assert.Throws<ArgumentException>(() => Construir(cae: cae));
            Assert.Equal("cae", error.ParamName);
            Assert.DoesNotContain("7512345678901", error.Message, StringComparison.Ordinal);
        }

        Assert.Throws<ArgumentNullException>(() => Construir(cae: null!));
    }

    [Fact]
    public void Factura_DatosFueraDeRango_Error()
    {
        Assert.Equal("numero", Assert.Throws<ArgumentException>(() => Construir(numero: 0)).ParamName);
        Assert.Equal("puntoVenta", Assert.Throws<ArgumentException>(() => Construir(puntoVenta: 0)).ParamName);
        Assert.Equal("puntoVenta", Assert.Throws<ArgumentException>(() => Construir(puntoVenta: 100_000)).ParamName);
        Assert.Equal("presupuestoNumero", Assert.Throws<ArgumentException>(() => Construir(presupuestoNumero: 0)).ParamName);
        Assert.Equal("tipo", Assert.Throws<ArgumentException>(() => Construir(tipo: (TipoComprobante)7)).ParamName);
        Assert.Equal("entorno", Assert.Throws<ArgumentException>(() => Construir(entorno: (EntornoArca)7)).ParamName);
        Assert.Equal("vencimientoCae", Assert.Throws<ArgumentException>(() => Construir(vencimiento: default(DateOnly))).ParamName);
        Assert.Equal("concepto", Assert.Throws<ArgumentException>(() => Construir(concepto: (ConceptoFactura)7)).ParamName);
        Assert.Equal("fecha", Assert.Throws<ArgumentException>(() => Construir(fecha: default(DateOnly))).ParamName);
        Assert.Equal("numero", Assert.Throws<ArgumentException>(() => Construir(numero: 100_000_000)).ParamName);

        // Las líneas suman más que el total: la suma se corta al pasarlo y se rechaza.
        Assert.Equal(
            "lineas",
            Assert.Throws<ArgumentException>(() => Construir(importes: ImportesFactura.ParaFacturaB(1500m, 21m))).ParamName);
        Assert.Equal(
            "lineas",
            Assert.Throws<ArgumentException>(() => Construir(
                importes: ImportesFactura.ParaFacturaB(1000m, 21m),
                lineas: [Lineas()[0], Lineas()[1], new LineaFactura(3, 4, "Estuche", 1m, 1, 0m, 1m, 1m)])).ParamName);

        // Sin líneas, con una null o con un orden que no es 1, 2, 3…
        Assert.Equal("lineas", Assert.Throws<ArgumentException>(() => Construir(lineas: [])).ParamName);
        Assert.Equal("lineas", Assert.Throws<ArgumentException>(() => Construir(lineas: [Lineas()[0], null!])).ParamName);
        Assert.Equal("lineas", Assert.Throws<ArgumentException>(() => Construir(lineas: [Lineas()[1], Lineas()[0]])).ParamName);

        // Las líneas suman el total facturado (FR-04).
        Assert.Equal(
            "lineas",
            Assert.Throws<ArgumentException>(() => Construir(importes: ImportesFactura.ParaFacturaB(2000.01m, 21m))).ParamName);

        // La B discrimina IVA y la C no.
        Assert.Equal(
            "importes",
            Assert.Throws<ArgumentException>(() => Construir(importes: ImportesFactura.ParaFacturaC(2000m))).ParamName);
        Assert.Equal(
            "importes",
            Assert.Throws<ArgumentException>(() => Construir(tipo: TipoComprobante.FacturaC)).ParamName);
    }

    [Fact]
    public void Factura_Construida_ExponeComprobanteYDatos()
    {
        var factura = Construir();

        Assert.Equal("Factura B 0003-00000125", factura.Comprobante.ToString());
        Assert.Equal(TipoComprobante.FacturaB, factura.Tipo);
        Assert.Equal(3, factura.PuntoVenta);
        Assert.Equal(125, factura.Numero);
        Assert.Equal(41, factura.PresupuestoNumero);
        Assert.Equal(Cae, factura.Cae);
        Assert.Equal(VencimientoCae, factura.VencimientoCae);
        Assert.Equal(EntornoArca.Simulado, factura.Entorno);
        Assert.Equal(Fecha, factura.Fecha);
        Assert.Equal(ConceptoFactura.Productos, factura.Concepto);
        Assert.Equal(Dni, factura.Receptor.Dni);
        Assert.Equal(2, factura.Lineas.Count);
    }

    [Fact]
    public void Factura_Emitida_CopiaElPresupuestoYLaSolicitud()
    {
        var cliente = new ClientePresupuesto("Zabaleta", "Ernestina", Dni, null, null, null);
        var presupuesto = Presupuesto.Nuevo(
                41, new DateOnly(2026, 10, 1), cliente,
                [new LineaPresupuesto(7, "Armazón metálico", 1000m, 1, 10m, 900m, 900m),
                 new LineaPresupuesto(9, "Cristal orgánico", 500m, 2, 0m, 500m, 1000m)])
            .PasarAFinal();
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaC, 3, Fecha, ConceptoFactura.ProductosYServicios, ReceptorFactura.SinIdentificar,
            ImportesFactura.ParaFacturaC(1900m));

        var factura = Factura.Emitida(presupuesto, solicitud, 8, Cae, VencimientoCae, EntornoArca.Homologacion);

        Assert.Equal("Factura C 0003-00000008", factura.Comprobante.ToString());
        Assert.Equal(41, factura.PresupuestoNumero);
        Assert.Equal(ConceptoFactura.ProductosYServicios, factura.Concepto);
        Assert.False(factura.Receptor.EstaIdentificado);
        Assert.Equal(EntornoArca.Homologacion, factura.Entorno);
        var primera = factura.Lineas[0];
        Assert.Equal((1, 7L, "Armazón metálico", 1000m, 1, 10m, 900m, 900m),
            (primera.Orden, primera.CodigoArticulo, primera.Descripcion, primera.PrecioUnitario, primera.Cantidad,
                primera.PorcentajeDescuento, primera.PrecioConDescuento, primera.PrecioFinal));
        Assert.Equal(2, factura.Lineas[1].Orden);

        // Un Borrador no se factura, y la solicitud tiene que ser del total del presupuesto.
        var borrador = Presupuesto.Nuevo(41, new DateOnly(2026, 10, 1), cliente, presupuesto.Lineas);
        Assert.Equal(
            "presupuesto",
            Assert.Throws<ArgumentException>(() =>
                Factura.Emitida(borrador, solicitud, 8, Cae, VencimientoCae, EntornoArca.Simulado)).ParamName);
        var otroTotal = new SolicitudComprobante(
            TipoComprobante.FacturaC, 3, Fecha, ConceptoFactura.Productos, ReceptorFactura.SinIdentificar,
            ImportesFactura.ParaFacturaC(1901m));
        Assert.Equal(
            "solicitud",
            Assert.Throws<ArgumentException>(() =>
                Factura.Emitida(presupuesto, otroTotal, 8, Cae, VencimientoCae, EntornoArca.Simulado)).ParamName);
    }

    [Fact]
    public void Factura_Emitida_ReceptorDeOtroCliente_Rechazado()
    {
        var presupuesto = PresupuestoFinal1900();
        var conDniDelCliente = SolicitudC1900(ReceptorFactura.ConDni(Dni));
        Assert.Equal(Dni, Factura.Emitida(presupuesto, conDniDelCliente, 8, Cae, VencimientoCae, EntornoArca.Simulado).Receptor.Dni);

        var conOtroDni = SolicitudC1900(ReceptorFactura.ConDni("12345678"));
        var error = Assert.Throws<ArgumentException>(() =>
            Factura.Emitida(presupuesto, conOtroDni, 8, Cae, VencimientoCae, EntornoArca.Simulado));

        Assert.Equal("solicitud", error.ParamName);
        Assert.DoesNotContain("12345678", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Dni, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Factura_Emitida_VencimientoCaeAnteriorALaFecha_Rechazado()
    {
        var presupuesto = PresupuestoFinal1900();
        var solicitud = SolicitudC1900(ReceptorFactura.SinIdentificar);

        var error = Assert.Throws<ArgumentException>(() =>
            Factura.Emitida(presupuesto, solicitud, 8, Cae, Fecha.AddDays(-1), EntornoArca.Simulado));
        Assert.Equal("vencimientoCae", error.ParamName);
        Assert.DoesNotContain("2026", error.Message, StringComparison.Ordinal);

        // Vence el mismo día del comprobante: se admite.
        Assert.Equal(Fecha, Factura.Emitida(presupuesto, solicitud, 8, Cae, Fecha, EntornoArca.Simulado).VencimientoCae);
    }

    [Fact]
    public void LineaFactura_OrdenMenorA1_Rechazado()
    {
        foreach (var orden in new[] { 0, -3 })
        {
            var error = Assert.Throws<ArgumentException>(() =>
                new LineaFactura(orden, 7, "Armazón metálico", 1000m, 1, 0m, 1000m, 1000m));
            Assert.Equal("orden", error.ParamName);
        }
    }

    public static TheoryData<long, string, decimal, int, decimal, decimal, decimal> LineasInvalidas => new()
    {
        { 0, "Armazón", 1m, 1, 0m, 1m, 1m },
        { 7, "", 1m, 1, 0m, 1m, 1m },
        { 7, " Armazón", 1m, 1, 0m, 1m, 1m },
        { 7, new string('a', 201), 1m, 1, 0m, 1m, 1m },
        { 7, "Armazón", -1m, 1, 0m, 1m, 1m },
        { 7, "Armazón", 1.001m, 1, 0m, 1m, 1m },
        { 7, "Armazón", 1m, 0, 0m, 1m, 1m },
        { 7, "Armazón", 1m, 10_000, 0m, 1m, 1m },
        { 7, "Armazón", 1m, 1, 100.01m, 1m, 1m },
        { 7, "Armazón", 1m, 1, 0.001m, 1m, 1m },
        { 7, "Armazón", 1m, 1, 0m, -1m, 1m },
        { 7, "Armazón", 1m, 1, 0m, 1m, -1m },
    };

    /// <summary>D-34: la línea de la factura rechaza lo mismo que la del presupuesto, con el mismo campo y mensaje.</summary>
    [Theory]
    [MemberData(nameof(LineasInvalidas))]
    public void LineaFactura_MismasReglasQueLineaPresupuesto(
        long codigo, string descripcion, decimal precioUnitario, int cantidad, decimal descuento,
        decimal precioConDescuento, decimal precioFinal)
    {
        var dePresupuesto = Assert.Throws<ArgumentException>(() => new LineaPresupuesto(
            codigo, descripcion, precioUnitario, cantidad, descuento, precioConDescuento, precioFinal));
        var deFactura = Assert.Throws<ArgumentException>(() => new LineaFactura(
            1, codigo, descripcion, precioUnitario, cantidad, descuento, precioConDescuento, precioFinal));

        Assert.Equal(dePresupuesto.ParamName, deFactura.ParamName);
        Assert.Equal(dePresupuesto.Message, deFactura.Message);
    }

    [Fact]
    public void LineaFactura_NoConstruyeUnaLineaDePresupuesto()
    {
        // WARN-1 de la revisión: las reglas se comparten, no se valida armando una LineaPresupuesto descartable.
        var constructor = Assert.Single(typeof(LineaFactura).GetConstructors());
        var llamadas = Ensamblado.ConstructoresInvocados(constructor);

        Assert.DoesNotContain(typeof(LineaPresupuesto), llamadas);
    }

    private static Presupuesto PresupuestoFinal1900()
    {
        var cliente = new ClientePresupuesto("Zabaleta", "Ernestina", Dni, null, null, null);
        return Presupuesto.Nuevo(
                41, new DateOnly(2026, 10, 1), cliente,
                [new LineaPresupuesto(7, "Armazón metálico", 1000m, 1, 10m, 900m, 900m),
                 new LineaPresupuesto(9, "Cristal orgánico", 500m, 2, 0m, 500m, 1000m)])
            .PasarAFinal();
    }

    private static SolicitudComprobante SolicitudC1900(ReceptorFactura receptor) =>
        new(TipoComprobante.FacturaC, 3, Fecha, ConceptoFactura.Productos, receptor, ImportesFactura.ParaFacturaC(1900m));

    /// <summary>Lee el IL de un método y devuelve los tipos cuyos constructores invoca con <c>newobj</c>.</summary>
    private static class Ensamblado
    {
        private const byte Newobj = 0x73;

        public static IReadOnlyList<Type> ConstructoresInvocados(MethodBase metodo)
        {
            var il = metodo.GetMethodBody()!.GetILAsByteArray()!;
            var tipos = new List<Type>();

            // Recorrido simple: cada 0x73 seguido de un token de método resoluble se toma como newobj. Un falso
            // positivo solo podría agregar tipos, nunca ocultar el de LineaPresupuesto.
            for (var i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != Newobj)
                {
                    continue;
                }

                var token = BitConverter.ToInt32(il, i + 1);
                try
                {
                    if (metodo.Module.ResolveMethod(token) is ConstructorInfo invocado && invocado.DeclaringType is { } tipo)
                    {
                        tipos.Add(tipo);
                    }
                }
                catch (ArgumentException)
                {
                    // No era un token de método.
                }
            }

            return tipos;
        }
    }

    [Fact]
    public void Factura_NoExponeNadaQueLaCambie()
    {
        // FR-25: ninguna propiedad pública con set ni método público de instancia que no sea de object.
        foreach (var tipo in new[] { typeof(Factura), typeof(LineaFactura), typeof(ImportesFactura), typeof(ReceptorFactura) })
        {
            Assert.True(tipo.IsSealed, tipo.Name);
            Assert.All(
                tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                propiedad => Assert.Null(propiedad.SetMethod));
            Assert.All(
                tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(metodo => !metodo.IsSpecialName),
                metodo => Assert.Fail($"{tipo.Name}.{metodo.Name} es un método público de instancia."));
        }

        // Las líneas no se pueden cambiar a través de la lista expuesta.
        var lineas = Construir().Lineas;
        Assert.False(lineas is LineaFactura[]);
        Assert.True(lineas is not IList<LineaFactura> lista || lista.IsReadOnly);
    }

    [Fact]
    public void Receptor_ToString_SinDni()
    {
        var receptor = ReceptorFactura.ConDni(Dni);

        Assert.DoesNotContain(Dni, receptor.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Dni, Construir().ToString(), StringComparison.Ordinal);
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaB, 3, Fecha, ConceptoFactura.Productos, receptor, ImportesFactura.ParaFacturaB(2000m, 21m));
        Assert.DoesNotContain(Dni, solicitud.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Dni, new EmisionPendiente(41, DateTimeOffset.UnixEpoch, solicitud, EntornoArca.Simulado).ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Receptor_DniInvalido_ErrorSinElValor()
    {
        foreach (var dni in new[] { "123456", "123456789", "23.456.789", "2345678A", "" })
        {
            var error = Assert.Throws<ArgumentException>(() => ReceptorFactura.ConDni(dni));
            Assert.Equal("dni", error.ParamName);
            Assert.DoesNotContain("23", error.Message, StringComparison.Ordinal);
        }

        Assert.Throws<ArgumentNullException>(() => ReceptorFactura.ConDni(null!));
        Assert.Equal("1234567", ReceptorFactura.ConDni("1234567").Dni);
        Assert.False(ReceptorFactura.SinIdentificar.EstaIdentificado);
    }

    [Fact]
    public void Solicitud_DatosInvalidos_Error()
    {
        var importesB = ImportesFactura.ParaFacturaB(1815m, 21m);

        Assert.Equal("puntoVenta", Assert.Throws<ArgumentException>(() => new SolicitudComprobante(
            TipoComprobante.FacturaB, 0, Fecha, ConceptoFactura.Productos, ReceptorFactura.SinIdentificar, importesB)).ParamName);
        Assert.Equal("fecha", Assert.Throws<ArgumentException>(() => new SolicitudComprobante(
            TipoComprobante.FacturaB, 3, default, ConceptoFactura.Productos, ReceptorFactura.SinIdentificar, importesB)).ParamName);
        Assert.Equal("concepto", Assert.Throws<ArgumentException>(() => new SolicitudComprobante(
            TipoComprobante.FacturaB, 3, Fecha, (ConceptoFactura)5, ReceptorFactura.SinIdentificar, importesB)).ParamName);
        Assert.Equal("importes", Assert.Throws<ArgumentException>(() => new SolicitudComprobante(
            TipoComprobante.FacturaC, 3, Fecha, ConceptoFactura.Productos, ReceptorFactura.SinIdentificar, importesB)).ParamName);
        Assert.Throws<ArgumentNullException>(() => new SolicitudComprobante(
            TipoComprobante.FacturaB, 3, Fecha, ConceptoFactura.Productos, null!, importesB));
    }

    [Fact]
    public void EmisionPendiente_GuardaPresupuestoMomentoUtcYSolicitud()
    {
        var solicitud = new SolicitudComprobante(
            TipoComprobante.FacturaC, 3, Fecha, ConceptoFactura.Productos, ReceptorFactura.SinIdentificar,
            ImportesFactura.ParaFacturaC(1815m));
        var momento = new DateTimeOffset(2026, 10, 30, 11, 15, 0, TimeSpan.FromHours(-3));

        var pendiente = new EmisionPendiente(41, momento, solicitud, EntornoArca.Homologacion);

        Assert.Equal(41, pendiente.PresupuestoNumero);
        Assert.Equal(TimeSpan.Zero, pendiente.Momento.Offset);
        Assert.Equal(momento, pendiente.Momento);
        Assert.Same(solicitud, pendiente.Solicitud);
        Assert.Equal(EntornoArca.Homologacion, pendiente.Entorno);

        Assert.Equal("presupuestoNumero", Assert.Throws<ArgumentException>(() =>
            new EmisionPendiente(0, momento, solicitud, EntornoArca.Simulado)).ParamName);
        Assert.Equal("momento", Assert.Throws<ArgumentException>(() =>
            new EmisionPendiente(41, default, solicitud, EntornoArca.Simulado)).ParamName);
        Assert.Equal("entorno", Assert.Throws<ArgumentException>(() =>
            new EmisionPendiente(41, momento, solicitud, (EntornoArca)9)).ParamName);
        Assert.Throws<ArgumentNullException>(() => new EmisionPendiente(41, momento, null!, EntornoArca.Simulado));
    }
}
