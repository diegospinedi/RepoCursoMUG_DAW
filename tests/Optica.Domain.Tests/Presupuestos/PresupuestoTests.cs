using Optica.Domain.Comun;
using Optica.Domain.Presupuestos;

namespace Optica.Domain.Tests.Presupuestos;

// Datos ficticios (D-17): ninguno corresponde a una persona real.
public class PresupuestoTests
{
    private static readonly DateOnly Fecha = new(2026, 3, 10);

    private static ClientePresupuesto Cliente() =>
        new("Zabaleta", "Ernestina", "23.456.789", "Calle Falsa 123", "ernestina@mail.com", "11 5555-0000");

    private static LineaPresupuesto[] UnaLinea() =>
        [new LineaPresupuesto(7, "Armazón metálico", 1850m, 1, 0m, 1850m, 1850m)];

    [Fact]
    public void Nuevo_SinLineas_Rechazado()
    {
        var error = Assert.Throws<ArgumentException>(() => Presupuesto.Nuevo(1, Fecha, Cliente(), []));
        Assert.Equal("lineas", error.ParamName);

        var errorNulo = Assert.Throws<ArgumentException>(() => Presupuesto.Nuevo(1, Fecha, Cliente(), [null!]));
        Assert.Equal("lineas", errorNulo.ParamName);

        var sinLista = Assert.Throws<ArgumentNullException>(() => Presupuesto.Nuevo(1, Fecha, Cliente(), null!));
        Assert.Equal("lineas", sinLista.ParamName);
        var sinCliente = Assert.Throws<ArgumentNullException>(() => Presupuesto.Nuevo(1, Fecha, null!, UnaLinea()));
        Assert.Equal("cliente", sinCliente.ParamName);

        foreach (var numero in new long[] { 0, -54321 })
        {
            var errorNumero = Assert.Throws<ArgumentException>(() => Presupuesto.Nuevo(numero, Fecha, Cliente(), UnaLinea()));
            Assert.Equal("numero", errorNumero.ParamName);
            Assert.DoesNotContain("54321", errorNumero.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Siguiente_UltimoMaximo_Error()
    {
        Assert.Throws<InvalidOperationException>(() => NumeracionPresupuesto.Siguiente(long.MaxValue));
        Assert.Equal(long.MaxValue, NumeracionPresupuesto.Siguiente(long.MaxValue - 1));

        // Ningún presupuesto tiene un número menor que 1: un último así es un dato roto.
        foreach (var ultimo in new long[] { 0, -1 })
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => NumeracionPresupuesto.Siguiente(ultimo));
            Assert.Equal("ultimo", error.ParamName);
        }
    }

    [Fact]
    public void Siguiente_Ultimo154_155()
    {
        Assert.Equal(155, NumeracionPresupuesto.Siguiente(154));
    }

    [Fact]
    public void Siguiente_SinPresupuestos_1()
    {
        Assert.Equal(1, NumeracionPresupuesto.Siguiente(null));
    }

    [Fact]
    public void Nuevo_QuedaEnBorrador()
    {
        var cliente = Cliente();
        var lineas = UnaLinea();

        var presupuesto = Presupuesto.Nuevo(155, Fecha, cliente, lineas);

        Assert.Equal(EstadoPresupuesto.Borrador, presupuesto.Estado);
        Assert.Equal(155, presupuesto.Numero);
        Assert.Same(cliente, presupuesto.Cliente);
        Assert.Equal(lineas, presupuesto.Lineas);

        // La reconstrucción conserva el estado guardado.
        var final = new Presupuesto(155, Fecha, EstadoPresupuesto.Final, cliente, lineas);
        Assert.Equal(EstadoPresupuesto.Final, final.Estado);
        var error = Assert.Throws<ArgumentException>(
            () => new Presupuesto(155, Fecha, (EstadoPresupuesto)99, cliente, lineas));
        Assert.Equal("estado", error.ParamName);
    }

    [Fact]
    public void Nuevo_Fecha10_03_2026_LaConserva()
    {
        var presupuesto = Presupuesto.Nuevo(1, Fecha, Cliente(), UnaLinea());

        Assert.Equal(new DateOnly(2026, 3, 10), presupuesto.Fecha);

        // Una fecha sin asignar (01/01/0001) no es la del día en que se grabó (FR-15).
        var error = Assert.Throws<ArgumentException>(() => Presupuesto.Nuevo(1, default, Cliente(), UnaLinea()));
        Assert.Equal("fecha", error.ParamName);
        Assert.DoesNotContain("0001", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Total_SumaPreciosFinales()
    {
        // AC-07: 10,02 (AC-06) + 2.700,00 (AC-01) = 2.710,02, sumando los Precios Finales ya redondeados.
        LineaPresupuesto[] lineas =
        [
            new(7, "Armazón metálico", 6.67m, 3, 50m, 3.34m, 10.02m),
            new(8, "Lente orgánico", 1000m, 3, 10m, 900m, 2700m),
        ];

        Assert.Equal(2710.02m, Presupuesto.Nuevo(1, Fecha, Cliente(), lineas).Total);
        Assert.Equal(1850m, Presupuesto.Nuevo(1, Fecha, Cliente(), UnaLinea()).Total);
        Assert.Equal(2710.02m, new Presupuesto(1, Fecha, EstadoPresupuesto.Final, Cliente(), lineas).Total);
    }

    [Fact]
    public void Constructor_TotalExcedido_Rechazado()
    {
        const decimal maximo = ReglasImporte.ImporteMaximo;

        // Justo en el límite del tipo: se acepta.
        LineaPresupuesto[] enElLimite =
        [
            new(7, "Armazón metálico", maximo - 0.01m, 1, 0m, maximo - 0.01m, maximo - 0.01m),
            new(8, "Lente orgánico", 0.01m, 1, 0m, 0.01m, 0.01m),
        ];
        Assert.Equal(maximo, Presupuesto.Nuevo(1, Fecha, Cliente(), enElLimite).Total);

        LineaPresupuesto[] excedidas =
        [
            new(7, "Armazón metálico", maximo, 1, 0m, maximo, maximo),
            new(8, "Lente orgánico", 0.01m, 1, 0m, 0.01m, 0.01m),
        ];

        var error = Assert.Throws<ArgumentException>(() => Presupuesto.Nuevo(1, Fecha, Cliente(), excedidas));
        Assert.Equal("lineas", error.ParamName);
        Assert.DoesNotContain("92233720368547758", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("92.233.720", error.Message, StringComparison.Ordinal);

        var errorReconstruido = Assert.Throws<ArgumentException>(
            () => new Presupuesto(1, Fecha, EstadoPresupuesto.Final, Cliente(), excedidas));
        Assert.Equal("lineas", errorReconstruido.ParamName);
    }

    [Fact]
    public void ToString_ClienteYPresupuesto_SinDatosPersonales()
    {
        var cliente = Cliente();
        var presupuesto = Presupuesto.Nuevo(1, Fecha, cliente, UnaLinea());
        string[] datosPersonales =
            ["Zabaleta", "Ernestina", "23456789", "23.456.789", "Calle Falsa", "ernestina@mail.com", "5555-0000"];

        foreach (var texto in new[] { cliente.ToString(), presupuesto.ToString() })
        {
            foreach (var dato in datosPersonales)
            {
                Assert.DoesNotContain(dato, texto, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
