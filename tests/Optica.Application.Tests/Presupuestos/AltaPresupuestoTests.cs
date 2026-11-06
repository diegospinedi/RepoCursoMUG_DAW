using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Application.Presupuestos;
using Optica.Application.Tests.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

/// <summary>Datos ficticios: ningún valor pertenece a una persona real.</summary>
public class AltaPresupuestoTests
{
    private const string Apellido = "Pérez";
    private const string Nombre = "Ana";
    private const string Dni = "23456789";
    private const string Domicilio = "Av. Siempreviva 742";
    private const string Email = "ana@mail.com";
    private const string Telefono = "11 5555-0000";

    private const string TextoArticuloNoDisponible = "Ese artículo ya no está disponible. Quitá la línea y buscalo de nuevo";
    private const string TextoPrecioNegativo = "El precio unitario no puede ser negativo";
    private const string TextoSinLineas = "Agregá al menos una línea al presupuesto";

    /// <summary>10/03/2026 12:00 en Argentina.</summary>
    private static readonly DateTimeOffset MedioDia10DeMarzo = new(2026, 3, 10, 15, 0, 0, TimeSpan.Zero);

    private readonly PresupuestosEnMemoria _unidad = new();
    private readonly RelojLocal _reloj = new(MedioDia10DeMarzo);

    public AltaPresupuestoTests()
    {
        _unidad.SembrarArticulo(15, "Armazón de acetato", 1850m);
    }

    private AltaPresupuesto CrearCasoDeUso() => new(_unidad, _unidad.Articulos, _unidad, _reloj);

    private static DatosCliente ClienteCompleto() => new(Apellido, Nombre, Dni, Domicilio, Email, Telefono);

    private static DatosPresupuesto Datos(DatosCliente? cliente = null, params long?[] lineas) =>
        new(cliente ?? ClienteCompleto(), lineas.Length == 0 ? [15] : lineas);

    private void AssertSinLeerLaBase()
    {
        Assert.Empty(_unidad.Articulos.Accesos);
        Assert.Equal(0, _unidad.Articulos.Operaciones);
        Assert.Equal(0, _unidad.Agregados);
        Assert.Empty(_unidad.Guardados);
    }

    [Fact]
    public async Task Alta_SinApellidoSinNombreSinDni_TodosLosErroresSinLeer()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            Datos(new DatosCliente(null, "   ", "", Domicilio, Email, Telefono)));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                new ErrorValidacion("apellido", "Ingresá el apellido del cliente"),
                new ErrorValidacion("nombre", "Ingresá el nombre del cliente"),
                new ErrorValidacion("dni", "Ingresá el DNI del cliente"),
            ],
            resultado.Errores);
        AssertSinLeerLaBase();

        // Sin el cliente: los tres obligatorios faltan, igual que si llegaran vacíos.
        var sinCliente = await CrearCasoDeUso().EjecutarAsync(new DatosPresupuesto(null, [15]));
        Assert.Equal(["apellido", "nombre", "dni"], sinCliente.Errores.Select(error => error.Campo));
        AssertSinLeerLaBase();
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("123456789")]
    [InlineData("23A56789")]
    public async Task Alta_Dni123456_ErrorEnDni(string dni)
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            Datos(new DatosCliente(Apellido, Nombre, dni, null, null, null)));

        Assert.Equal([new ErrorValidacion("dni", "El DNI debe tener 7 u 8 dígitos")], resultado.Errores);
        Assert.DoesNotContain(resultado.Errores, error => error.Accion.Contains(dni, StringComparison.Ordinal));
        AssertSinLeerLaBase();
    }

    [Theory]
    [InlineData("ana@")]
    [InlineData("ana@mail")]
    [InlineData("ana@mail.c")]
    public async Task Alta_EmailAnaArroba_ErrorEnEmail(string email)
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            Datos(new DatosCliente(Apellido, Nombre, Dni, null, email, null)));

        Assert.Equal(
            [new ErrorValidacion("email", "El email debe tener el formato usuario@dominio.extensión")],
            resultado.Errores);
        AssertSinLeerLaBase();
    }

    /// <summary>Largos de D-08 y UTF-16 inválido (D-28): textos propios que repiten los del catálogo.</summary>
    [Fact]
    public async Task Alta_LargosYCaracteresInvalidos_TextosDelCatalogo()
    {
        const string Invalido = "Pérez\U0000D800";
        var largos = new DatosCliente(
            new string('a', 101), new string('b', 101), Dni, new string('c', 201),
            new string('d', 250) + "@m.ar", new string('9', 31));
        var invalidos = new DatosCliente(Invalido, Invalido, Dni, Invalido, "ana\U0000D800@mail.com", Invalido);

        var conLargos = await CrearCasoDeUso().EjecutarAsync(Datos(largos));
        var conInvalidos = await CrearCasoDeUso().EjecutarAsync(Datos(invalidos));

        Assert.Equal(
            [
                new ErrorValidacion("apellido", "Usá hasta 100 caracteres."),
                new ErrorValidacion("nombre", "Usá hasta 100 caracteres."),
                new ErrorValidacion("domicilio", "Usá hasta 200 caracteres."),
                new ErrorValidacion("email", "Usá hasta 254 caracteres."),
                new ErrorValidacion("telefono", "Usá hasta 30 caracteres."),
            ],
            conLargos.Errores);
        const string Caracteres = "El texto tiene caracteres no válidos. Escribilo de nuevo.";
        Assert.Equal(
            [
                new ErrorValidacion("apellido", Caracteres),
                new ErrorValidacion("nombre", Caracteres),
                new ErrorValidacion("domicilio", Caracteres),
                new ErrorValidacion("email", Caracteres),
                new ErrorValidacion("telefono", Caracteres),
            ],
            conInvalidos.Errores);
        AssertSinLeerLaBase();
    }

    [Fact]
    public async Task Alta_SinLineas_ErrorEnLineas()
    {
        var vacia = await CrearCasoDeUso().EjecutarAsync(new DatosPresupuesto(ClienteCompleto(), []));
        var ausente = await CrearCasoDeUso().EjecutarAsync(new DatosPresupuesto(ClienteCompleto(), null));

        Assert.Equal([new ErrorValidacion("lineas", TextoSinLineas)], vacia.Errores);
        Assert.Equal([new ErrorValidacion("lineas", TextoSinLineas)], ausente.Errores);
        AssertSinLeerLaBase();
    }

    public static TheoryData<long?[], ErrorValidacion[]> LineasInvalidas => new()
    {
        { Enumerable.Repeat<long?>(15, 101).ToArray(), [new ErrorValidacion("lineas", "Usá hasta 100 líneas.")] },
        { [null], [new ErrorValidacion("lineas[0]", TextoArticuloNoDisponible)] },
        {
            [15, 0, -1],
            [
                new ErrorValidacion("lineas[1]", TextoArticuloNoDisponible),
                new ErrorValidacion("lineas[2]", TextoArticuloNoDisponible),
            ]
        },
        { [9_007_199_254_740_992], [new ErrorValidacion("lineas[0]", TextoArticuloNoDisponible)] },
    };

    [Theory]
    [MemberData(nameof(LineasInvalidas))]
    public async Task Alta_101LineasOCodigoFaltante_Rechazado(long?[] lineas, ErrorValidacion[] esperados)
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(new DatosPresupuesto(ClienteCompleto(), lineas));

        Assert.Equal(esperados, resultado.Errores);
        AssertSinLeerLaBase();
    }

    [Fact]
    public async Task Alta_100Lineas_Grabado()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            new DatosPresupuesto(ClienteCompleto(), Enumerable.Repeat<long?>(15, 100).ToArray()));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(100, resultado.Presupuesto.Lineas.Count);
    }

    [Fact]
    public async Task Alta_ArticuloInactivoOInexistente_ErrorEnLaLinea()
    {
        _unidad.SembrarArticulo(16, "Lente orgánico", 900m, activo: false);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Datos(null, 15, 16, 99));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                new ErrorValidacion("lineas[1]", TextoArticuloNoDisponible),
                new ErrorValidacion("lineas[2]", TextoArticuloNoDisponible),
            ],
            resultado.Errores);
        Assert.Equal(0, _unidad.Agregados);
        Assert.Empty(_unidad.Guardados);
        Assert.DoesNotContain(_unidad.Articulos.Accesos, acceso => acceso.Accion == PresupuestosEnMemoria.AccesoUltimoNumero);
    }

    [Fact]
    public async Task Alta_PrecioMenos150_ErrorEnLaLinea()
    {
        _unidad.SembrarArticulo(16, "Estuche", -150m);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Datos(null, 16));

        Assert.Equal([new ErrorValidacion("lineas[0]", TextoPrecioNegativo)], resultado.Errores);
        Assert.Equal(0, _unidad.Agregados);
        Assert.Empty(_unidad.Guardados);
    }

    public static TheoryData<string> FallasAlConfirmar => new() { "falla", "conflicto", "commit" };

    [Theory]
    [MemberData(nameof(FallasAlConfirmar))]
    public async Task Alta_FallaAlConfirmar_SePropagaSinGrabar(string falla)
    {
        Exception esperada = falla switch
        {
            "conflicto" => new ConflictoAlConfirmarException("Conflicto al confirmar.", MotivoConflicto.Unicidad),
            _ => new InvalidOperationException("Falla simulada."),
        };
        if (falla == "commit")
        {
            _unidad.Articulos.ErrorAlConfirmarOperacion = esperada;
        }
        else
        {
            _unidad.Articulos.ErrorAlConfirmar = esperada;
        }

        var error = await Assert.ThrowsAnyAsync<Exception>(() => CrearCasoDeUso().EjecutarAsync(Datos()));

        Assert.Same(esperada, error);
        Assert.Equal(1, _unidad.Agregados);
        Assert.Empty(_unidad.Guardados);
        Assert.Equal(0, _unidad.Articulos.OperacionesConfirmadas);
        Assert.DoesNotContain(_unidad.Articulos.Accesos, acceso => acceso.Accion == PresupuestosEnMemoria.AccesoObtener);
    }

    [Fact]
    public async Task Alta_NumeroRepetidoAlConfirmar_ConflictoSePropaga()
    {
        // Otra grabación confirma el número 1 entre la lectura y la confirmación: la clave primaria lo rechaza (D-07).
        _unidad.Articulos.AntesDeConfirmar = () => _unidad.Sembrar(1);

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => CrearCasoDeUso().EjecutarAsync(Datos()));

        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.Empty(_unidad.Guardados);
    }

    [Fact]
    public async Task Alta_SeisDatosYUnaLinea_Grabado()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Datos(new DatosCliente(
            "  " + Apellido + " ", Nombre, Dni, Domicilio, " " + Email, Telefono)));

        Assert.True(resultado.EsGrabado);
        Assert.Empty(resultado.Errores);
        var grabado = resultado.Presupuesto;
        Assert.Equal(1, grabado.Numero);
        Assert.Equal(new ClienteGrabado(Apellido, Nombre, Dni, Domicilio, Email, Telefono), grabado.Cliente);
        Assert.Equal([new LineaGrabada(15, "Armazón de acetato", 1850m, 1, 0m, 1850m, 1850m)], grabado.Lineas);

        var guardado = Assert.Single(_unidad.Guardados);
        Assert.Equal(1, guardado.Numero);
        Assert.Equal(Apellido, guardado.Cliente.Apellido);
        Assert.Equal(Telefono, guardado.Cliente.Telefono);
    }

    [Fact]
    public async Task Alta_SinOpcionales_Grabado()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            Datos(new DatosCliente(Apellido, Nombre, Dni, null, "   ", "")));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(new ClienteGrabado(Apellido, Nombre, Dni, null, null, null), resultado.Presupuesto.Cliente);
        var guardado = Assert.Single(_unidad.Guardados);
        Assert.Null(guardado.Cliente.Domicilio);
        Assert.Null(guardado.Cliente.Email);
        Assert.Null(guardado.Cliente.Telefono);
    }

    [Fact]
    public async Task Alta_DniConPuntos_Graba23456789()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            Datos(new DatosCliente(Apellido, Nombre, "23.456.789", null, null, null)));

        Assert.True(resultado.EsGrabado);
        Assert.Equal("23456789", resultado.Presupuesto.Cliente.Dni);
        Assert.Equal("23456789", Assert.Single(_unidad.Guardados).Cliente.Dni);
    }

    [Theory]
    [InlineData("ana@mail.com")]
    [InlineData("ana@mail.com.ar")]
    public async Task Alta_EmailAnaMailCom_Grabado(string email)
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            Datos(new DatosCliente(Apellido, Nombre, Dni, null, email, null)));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(email, resultado.Presupuesto.Cliente.Email);
    }

    [Fact]
    public async Task Alta_Ultimo154_Numero155()
    {
        _unidad.Sembrar(154);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Datos());

        Assert.True(resultado.EsGrabado);
        Assert.Equal(155, resultado.Presupuesto.Numero);
        Assert.Contains(_unidad.Guardados, presupuesto => presupuesto.Numero == 155);
    }

    [Fact]
    public async Task Alta_SinPresupuestos_Numero1()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Datos());

        Assert.True(resultado.EsGrabado);
        Assert.Equal(1, resultado.Presupuesto.Numero);
    }

    [Fact]
    public async Task Alta_QuedaEnBorrador()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Datos());

        Assert.True(resultado.EsGrabado);
        Assert.Equal(EstadoPresupuesto.Borrador, resultado.Presupuesto.Estado);
        Assert.Equal(EstadoPresupuesto.Borrador, Assert.Single(_unidad.Guardados).Estado);
    }

    [Fact]
    public async Task Alta_Articulo1850_LineaConPrecioDelCatalogo()
    {
        // El precio guardado del catálogo se copia tal cual, sin recalcularlo con el costo y el margen (FR-10).
        _unidad.SembrarArticulo(16, "Lente orgánico", 1234.56m);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Datos(null, 15, 16, 15));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(
            [
                new LineaGrabada(15, "Armazón de acetato", 1850m, 1, 0m, 1850m, 1850m),
                new LineaGrabada(16, "Lente orgánico", 1234.56m, 1, 0m, 1234.56m, 1234.56m),
                new LineaGrabada(15, "Armazón de acetato", 1850m, 1, 0m, 1850m, 1850m),
            ],
            resultado.Presupuesto.Lineas);
        var linea = Assert.Single(_unidad.Guardados).Lineas[0];
        Assert.Equal((15L, 1850m, 1, 0m, 1850m, 1850m),
            (linea.CodigoArticulo, linea.PrecioUnitario, linea.Cantidad, linea.PorcentajeDescuento,
                linea.PrecioConDescuento, linea.PrecioFinal));
    }

    public static TheoryData<DateTimeOffset> RelojesDel10DeMarzo => new()
    {
        MedioDia10DeMarzo,
        // 10/03/2026 23:30 en Argentina: en UTC ya es el 11/03 (D-06).
        new DateTimeOffset(2026, 3, 11, 2, 30, 0, TimeSpan.Zero),
    };

    [Theory]
    [MemberData(nameof(RelojesDel10DeMarzo))]
    public async Task Alta_RelojLocal10_03_2026_GuardaEsaFecha(DateTimeOffset utc)
    {
        var resultado = await new AltaPresupuesto(_unidad, _unidad.Articulos, _unidad, new RelojLocal(utc))
            .EjecutarAsync(Datos());

        Assert.True(resultado.EsGrabado);
        Assert.Equal(new DateOnly(2026, 3, 10), resultado.Presupuesto.Fecha);
        Assert.Equal(new DateOnly(2026, 3, 10), Assert.Single(_unidad.Guardados).Fecha);
    }

    [Fact]
    public async Task Alta_LeeArticulosNumeroYFechaDentroDeLaOperacion()
    {
        _unidad.Sembrar(7);
        _reloj.AlLeer = () => _unidad.Articulos.Anotar(RelojLocal.AccesoReloj);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Datos(null, 15, 15));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(1, _unidad.Articulos.Operaciones);
        Assert.Equal(1, _unidad.Articulos.OperacionesConfirmadas);
        Assert.Equal(
            [
                (nameof(ArticulosEnMemoria.ObtenerAsync), true),
                (nameof(ArticulosEnMemoria.ObtenerAsync), true),
                (PresupuestosEnMemoria.AccesoUltimoNumero, true),
                (RelojLocal.AccesoReloj, true),
                (PresupuestosEnMemoria.AccesoAgregar, true),
                (nameof(ArticulosEnMemoria.ConfirmarAsync), true),
                // La relectura del presupuesto grabado, después de la operación.
                (PresupuestosEnMemoria.AccesoObtener, false),
            ],
            _unidad.Articulos.Accesos);
    }

    /// <summary>
    /// D-17: el alta no recibe un registrador, así que no registra nada; y si quien la llama registra los datos o el
    /// resultado, el registrador no recibe ninguno de los seis datos del cliente (los records no imprimen sus miembros).
    /// </summary>
    [Fact]
    public async Task Alta_NoRegistraDatosDelCliente()
    {
        var parametros = typeof(AltaPresupuesto).GetConstructors()
            .Concat(typeof(ObtenerPresupuesto).GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parametro => parametro.ParameterType);
        Assert.DoesNotContain(parametros, tipo => typeof(ILogger).IsAssignableFrom(tipo)
            || typeof(ILoggerFactory).IsAssignableFrom(tipo));

        var datos = Datos(new DatosCliente(Apellido, Nombre, "23.456.789", Domicilio, Email, Telefono));
        var grabado = await CrearCasoDeUso().EjecutarAsync(datos);
        var invalido = await CrearCasoDeUso().EjecutarAsync(
            Datos(new DatosCliente(Apellido, Nombre, "123456", Domicilio, "ana@", Telefono)));
        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(1);
        var cliente = new ClienteGrabado(Apellido, Nombre, Dni, Domicilio, Email, Telefono);

        using var fabrica = new FabricaRegistrosFalsa();
        var registro = fabrica.CreateLogger("Prueba");
        object?[] registrados =
        [
            datos, datos.Cliente, cliente, new PresupuestoGrabado(1, new DateOnly(2026, 3, 10),
                EstadoPresupuesto.Borrador, cliente, []),
            grabado, grabado.Presupuesto, grabado.Presupuesto?.Cliente, invalido, leido, leido?.Cliente,
        ];
        foreach (var registrado in registrados)
        {
            registro.LogInformation("Presupuesto {Valor}", registrado);
        }

        var textos = fabrica.Entradas
            .SelectMany(entrada => entrada.Valores.Values.Select(valor => valor?.ToString() ?? string.Empty)
                .Append(entrada.Mensaje))
            .ToList();
        Assert.Equal(registrados.Length, fabrica.Entradas.Count);
        foreach (var dato in new[] { Apellido, Nombre, Dni, "23.456.789", "123456", Domicilio, Email, "ana@", Telefono })
        {
            Assert.DoesNotContain(textos, texto => texto.Contains(dato, StringComparison.Ordinal));
        }

        // Lo registrado incluye lo que devolvió el alta y la consulta, no solo lo armado en la prueba.
        Assert.True(grabado.EsGrabado);
        Assert.NotNull(leido);
    }
}
