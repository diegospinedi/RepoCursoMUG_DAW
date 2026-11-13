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
        var final = new Presupuesto(155, Fecha, EstadoPresupuesto.Final, cliente, lineas, version: 0);
        Assert.Equal(EstadoPresupuesto.Final, final.Estado);
        var error = Assert.Throws<ArgumentException>(
            () => new Presupuesto(155, Fecha, (EstadoPresupuesto)99, cliente, lineas, version: 0));
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
        Assert.Equal(2710.02m, new Presupuesto(1, Fecha, EstadoPresupuesto.Final, Cliente(), lineas, version: 0).Total);
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
            () => new Presupuesto(1, Fecha, EstadoPresupuesto.Final, Cliente(), excedidas, version: 0));
        Assert.Equal("lineas", errorReconstruido.ParamName);
    }

    // Datos claramente ficticios para las pruebas de FEAT-003c: DNI 00.718.294, dominio example.com y teléfono 0000.
    private static readonly string[] DatosDelCliente =
    [
        "Ficticiez", "Prueboncia", "00718294", "00.718.294", "Calle Ficticia", "prueboncia@example.com", "0000-0000",
    ];

    private static ClientePresupuesto ClienteFicticio() =>
        new("Ficticiez", "Prueboncia", "00.718.294", "Calle Ficticia 100", "prueboncia@example.com", "00 0000-0000");

    private static ClientePresupuesto ClienteMudado() =>
        new("Ficticiez", "Prueboncia", "00.718.294", "Calle Ficticia 200", "prueboncia@example.com", "00 0000-0000");

    private static LineaPresupuesto[] OtrasLineas() =>
    [
        new LineaPresupuesto(8, "Lente orgánico", 1000m, 3, 10m, 900m, 2700m),
        new LineaPresupuesto(9, "Paño", 6.67m, 3, 50m, 3.34m, 10.02m),
    ];

    /// <summary>
    /// AC-01: la modificación del 12/03/2026 conserva la fecha 10/03/2026. AC-02: el Domicilio nuevo queda grabado.
    /// La versión no la toca el dominio: la incrementa quien persiste.
    /// </summary>
    [Fact]
    public void Modificar_Borrador_ConservaNumeroFechaYVersion()
    {
        var original = new Presupuesto(
            155, Fecha, EstadoPresupuesto.Borrador, ClienteFicticio(), UnaLinea(), version: 4);
        var cliente = ClienteMudado();
        var lineas = OtrasLineas();

        var modificado = original.Modificar(cliente, lineas);

        Assert.Equal(155, modificado.Numero);
        Assert.Equal(new DateOnly(2026, 3, 10), modificado.Fecha);
        Assert.Equal(4, modificado.Version);
        Assert.Equal(EstadoPresupuesto.Borrador, modificado.Estado);
        Assert.Same(cliente, modificado.Cliente);
        Assert.Equal("Calle Ficticia 200", modificado.Cliente.Domicilio);
        Assert.Equal(lineas, modificado.Lineas);
        Assert.Equal(2710.02m, modificado.Total);

        // El original no cambia.
        Assert.Equal("Calle Ficticia 100", original.Cliente.Domicilio);
        Assert.Single(original.Lineas);
        Assert.Equal(1850m, original.Total);

        // Valida como el constructor: al menos una línea, total dentro del límite y cliente presente.
        Assert.Equal("lineas", Assert.Throws<ArgumentException>(() => original.Modificar(cliente, [])).ParamName);
        Assert.Equal("cliente", Assert.Throws<ArgumentNullException>(() => original.Modificar(null!, lineas)).ParamName);
        LineaPresupuesto[] excedidas =
        [
            new(7, "Armazón metálico", ReglasImporte.ImporteMaximo, 1, 0m, ReglasImporte.ImporteMaximo,
                ReglasImporte.ImporteMaximo),
            new(8, "Lente orgánico", 0.01m, 1, 0m, 0.01m, 0.01m),
        ];
        Assert.Equal("lineas", Assert.Throws<ArgumentException>(() => original.Modificar(cliente, excedidas)).ParamName);
    }

    /// <summary>AC-07: un presupuesto en Final no se modifica; el rechazo no repite datos del cliente.</summary>
    [Fact]
    public void Modificar_Final_Rechazado()
    {
        var final = new Presupuesto(155, Fecha, EstadoPresupuesto.Final, ClienteFicticio(), UnaLinea(), version: 4);

        var error = Assert.Throws<InvalidOperationException>(() => final.Modificar(ClienteMudado(), OtrasLineas()));

        foreach (var dato in DatosDelCliente)
        {
            Assert.DoesNotContain(dato, error.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("155", error.Message, StringComparison.Ordinal);
        Assert.Equal(EstadoPresupuesto.Final, final.Estado);
        Assert.Equal("Calle Ficticia 100", final.Cliente.Domicilio);
        Assert.Single(final.Lineas);
        Assert.Equal(4, final.Version);
    }

    /// <summary>AC-04: el paso a Final solo cambia el estado; número, fecha, versión, cliente y líneas se conservan.</summary>
    [Fact]
    public void PasarAFinal_Borrador_QuedaEnFinal()
    {
        var cliente = ClienteFicticio();
        var lineas = UnaLinea();
        var borrador = new Presupuesto(155, Fecha, EstadoPresupuesto.Borrador, cliente, lineas, version: 4);

        var final = borrador.PasarAFinal();

        Assert.Equal(EstadoPresupuesto.Final, final.Estado);
        Assert.Equal(155, final.Numero);
        Assert.Equal(Fecha, final.Fecha);
        Assert.Equal(4, final.Version);
        Assert.Same(cliente, final.Cliente);
        Assert.Equal(lineas, final.Lineas);
        Assert.Equal(1850m, final.Total);

        // El original no cambia.
        Assert.Equal(EstadoPresupuesto.Borrador, borrador.Estado);
    }

    /// <summary>AC-08: un Final no vuelve a pasar a Final; el rechazo no repite datos del cliente.</summary>
    [Fact]
    public void PasarAFinal_Final_Rechazado()
    {
        var final = new Presupuesto(155, Fecha, EstadoPresupuesto.Final, ClienteFicticio(), UnaLinea(), version: 4);

        var error = Assert.Throws<InvalidOperationException>(final.PasarAFinal);

        foreach (var dato in DatosDelCliente)
        {
            Assert.DoesNotContain(dato, error.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("155", error.Message, StringComparison.Ordinal);
        Assert.Equal(EstadoPresupuesto.Final, final.Estado);
    }

    /// <summary>
    /// FR-06, AC-08: no existe una operación que devuelva un Final a Borrador. Agregar una operación pública que
    /// devuelva <see cref="Presupuesto"/> obliga a revisar esta prueba: la lista de abajo es la lista completa.
    /// </summary>
    [Fact]
    public void OperacionesPublicas_NoIncluyenVolverABorrador()
    {
        var operaciones = typeof(Presupuesto)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(metodo => metodo.ReturnType == typeof(Presupuesto))
            .Select(metodo => metodo.Name)
            .Order(StringComparer.Ordinal);
        Assert.Equal(["Modificar", "PasarAFinal"], operaciones);

        // El estado tampoco se puede cambiar desde afuera.
        var estado = typeof(Presupuesto).GetProperty(nameof(Presupuesto.Estado))!;
        Assert.Null(estado.GetSetMethod(nonPublic: false));
    }

    [Fact]
    public void Constructor_VersionInvalida_Rechazada()
    {
        const long versionMaxima = 9_007_199_254_740_991;

        foreach (var version in new[] { -1L, versionMaxima + 1 })
        {
            var error = Assert.Throws<ArgumentException>(
                () => new Presupuesto(1, Fecha, EstadoPresupuesto.Borrador, Cliente(), UnaLinea(), version));
            Assert.Equal("version", error.ParamName);
            Assert.DoesNotContain(version.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message,
                StringComparison.Ordinal);
        }

        // Los extremos del rango se aceptan, y el alta empieza en 0.
        Assert.Equal(0, new Presupuesto(1, Fecha, EstadoPresupuesto.Borrador, Cliente(), UnaLinea(), 0).Version);
        Assert.Equal(
            versionMaxima,
            new Presupuesto(1, Fecha, EstadoPresupuesto.Borrador, Cliente(), UnaLinea(), versionMaxima).Version);
        Assert.Equal(0, Presupuesto.Nuevo(1, Fecha, Cliente(), UnaLinea()).Version);
        Assert.Equal(versionMaxima, Presupuesto.VersionMaxima);
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
