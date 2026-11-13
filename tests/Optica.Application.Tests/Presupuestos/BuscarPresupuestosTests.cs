using Optica.Application.Comun;
using Optica.Application.Presupuestos;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

/// <summary>
/// El caso de uso de la búsqueda de presupuestos (Block 2 de FEAT-006): valida las fechas, los largos y la página sin
/// buscar si alguno es inválido, arma el filtro del dominio y pide páginas de 50. Los datos de clientes son ficticios.
/// </summary>
public class BuscarPresupuestosTests
{
    private const string TextoFechaInvalida = "La fecha no es válida.";
    private const string TextoDesdePosterior = "La fecha Desde no puede ser posterior a Hasta.";
    private const string TextoPagina = "Pedí una página entre 1 y 1.000.000.";

    private readonly PresupuestosEnMemoria _unidad = new();

    private BuscarPresupuestos CrearCasoDeUso() => new(_unidad);

    /// <summary>AC-08: con 120 presupuestos y sin filtros, la página 1 trae del 120 al 71 y la 3, del 20 al 1.</summary>
    [Fact]
    public async Task Buscar_SinFiltros_Pagina1De50()
    {
        for (var numero = 1L; numero <= 120; numero++)
        {
            _unidad.Sembrar(numero);
        }

        var primera = await CrearCasoDeUso().EjecutarAsync(null, null, null, null, null, null);
        var tercera = await CrearCasoDeUso().EjecutarAsync(null, null, null, null, null, 3);

        Assert.True(primera.EsValido);
        Assert.Empty(primera.Errores);
        Assert.Equal(1, primera.Pagina);
        Assert.Equal(50, primera.TamanoPagina);
        Assert.Equal(50, BuscarPresupuestos.TamanoPagina);
        Assert.Equal(120, primera.Total);
        Assert.Equal(Enumerable.Range(71, 50).Reverse().Select(n => (long)n), primera.Presupuestos.Select(p => p.Numero));

        Assert.True(tercera.EsValido);
        Assert.Equal(3, tercera.Pagina);
        Assert.Equal(120, tercera.Total);
        Assert.Equal(Enumerable.Range(1, 20).Reverse().Select(n => (long)n), tercera.Presupuestos.Select(p => p.Numero));
        // Contraparte de AfirmarSinBusqueda: con parámetros válidos, el caso de uso sí consulta el repositorio.
        Assert.Contains(_unidad.Articulos.Accesos, acceso => acceso.Accion == PresupuestosEnMemoria.AccesoBuscar);
    }

    /// <summary>
    /// Textos con un sustituto UTF-16 suelto. Los sustitutos no sobreviven a la serialización del descubrimiento de
    /// xUnit (se vuelven U+FFFD), por eso <c>DisableDiscoveryEnumeration</c> (decisions-FEAT-001b).
    /// </summary>
    public static TheoryData<string, string> TextosConSustituto => new()
    {
        { "apellido", "Zq7\uD800" },
        { "nombre", "\uDC00Zq7" },
        { "dni", "30\uD800111" },
    };

    /// <summary>
    /// Apellido, Nombre o DNI con UTF-16 inválido: 400 en su campo con el texto de caracteres no válidos, antes del
    /// control de largo (aunque además sea largo), sin buscar.
    /// </summary>
    [Theory]
    [MemberData(nameof(TextosConSustituto), DisableDiscoveryEnumeration = true)]
    public async Task Buscar_TextoConUtf16Invalido_Rechazado(string campo, string texto)
    {
        var largo = texto + new string('a', 120);
        string? Valor(string nombre, string valor) => campo == nombre ? valor : null;

        var corto = await CrearCasoDeUso().EjecutarAsync(
            Valor("apellido", texto), Valor("nombre", texto), Valor("dni", texto), null, null, null);
        var conLargo = await CrearCasoDeUso().EjecutarAsync(
            Valor("apellido", largo), Valor("nombre", largo), Valor("dni", largo), null, null, null);

        var esperado = new ErrorValidacion(campo, "El texto tiene caracteres no válidos. Escribilo de nuevo.");
        Assert.Equal([esperado], corto.Errores);
        Assert.Equal([esperado], conLargo.Errores);
        AfirmarSinBusqueda();
    }

    /// <summary>Una página después de la última trae la lista vacía y el total (contrato de la Api).</summary>
    [Fact]
    public async Task Buscar_PaginaPosteriorALaUltima_VaciaConTotal()
    {
        _unidad.Sembrar(1);

        var resultado = await CrearCasoDeUso().EjecutarAsync(null, null, null, null, null, 1_000_000);

        Assert.True(resultado.EsValido);
        Assert.Empty(resultado.Presupuestos);
        Assert.Equal(1_000_000, resultado.Pagina);
        Assert.Equal(1, resultado.Total);
    }

    /// <summary>
    /// AC-09: cada resultado trae número, fecha, Apellido, Nombre, DNI, estado (en minúsculas) y total (la suma de los
    /// Precio Final de sus líneas, 10,02 + 2.700,00).
    /// </summary>
    [Fact]
    public async Task Buscar_ResultadoTraeLosSieteDatos()
    {
        _unidad.Guardados.Add(new Presupuesto(
            155,
            new DateOnly(2026, 3, 10),
            EstadoPresupuesto.Final,
            new ClientePresupuesto("González", "María José", "23456789", null, null, "00 0000-0000"),
            [
                new LineaPresupuesto(1, "Lente orgánico", 6.67m, 3, 50m, 3.34m, 10.02m),
                new LineaPresupuesto(2, "Armazón", 2700m, 1, 0m, 2700m, 2700m),
            ],
            version: 1));
        _unidad.Sembrar(7);

        var resultado = await CrearCasoDeUso().EjecutarAsync(null, null, null, null, null, null);

        Assert.True(resultado.EsValido);
        Assert.Equal(
            [
                new PresupuestoListado(155, new DateOnly(2026, 3, 10), "González", "María José", "23456789", "final", 2710.02m),
                new PresupuestoListado(7, new DateOnly(2026, 1, 5), "Gómez", "Luis", "30111222", "borrador", 100m),
            ],
            resultado.Presupuestos);
    }

    /// <summary>
    /// AC-01 en el caso de uso: el Apellido llega al filtro del dominio plegado ("ONZALEZ" encuentra "González" y no
    /// "Gómez"), con las fechas leídas y la página de 50.
    /// </summary>
    [Fact]
    public async Task Buscar_ApellidoYFechas_ArmaElFiltro()
    {
        _unidad.Guardados.Add(new Presupuesto(
            2,
            new DateOnly(2026, 3, 20),
            EstadoPresupuesto.Borrador,
            new ClientePresupuesto("González", "Ana", "23456789", null, null, null),
            [new LineaPresupuesto(1, "Lente orgánico", 100m, 1, 0m, 100m, 100m)],
            version: 0));
        _unidad.Guardados.Add(new Presupuesto(
            3,
            new DateOnly(2026, 3, 20),
            EstadoPresupuesto.Borrador,
            new ClientePresupuesto("Gómez", "Ana", "23456788", null, null, null),
            [new LineaPresupuesto(1, "Lente orgánico", 100m, 1, 0m, 100m, 100m)],
            version: 0));
        _unidad.Sembrar(1);

        var resultado = await CrearCasoDeUso().EjecutarAsync(
            "  ONZALEZ ", null, "23.456", "2026-03-10", "2026-03-20", null);

        Assert.True(resultado.EsValido);
        Assert.Equal(2, Assert.Single(resultado.Presupuestos).Numero);
        Assert.Equal(1, resultado.Total);
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-13-01")]
    [InlineData("10/03/2026")]
    [InlineData("2026-3-10")]
    [InlineData("2026-03-10T00:00")]
    [InlineData(" 2026-03-10")]
    [InlineData("")]
    [InlineData("Zq7-marcado")]
    public async Task Buscar_FechaInvalida_Rechazada(string fecha)
    {
        _unidad.Sembrar(1);

        var enDesde = await CrearCasoDeUso().EjecutarAsync(null, null, null, fecha, "2026-03-10", null);
        var enHasta = await CrearCasoDeUso().EjecutarAsync(null, null, null, "2026-03-10", fecha, null);

        Assert.False(enDesde.EsValido);
        Assert.Equal([new ErrorValidacion("desde", TextoFechaInvalida)], enDesde.Errores);
        Assert.Empty(enDesde.Presupuestos);
        Assert.Equal(0, enDesde.Pagina);
        Assert.Equal(0, enDesde.Total);
        Assert.Equal([new ErrorValidacion("hasta", TextoFechaInvalida)], enHasta.Errores);
        AfirmarSinBusqueda();
    }

    /// <summary>AC-07: Desde 20/03/2026 y Hasta 10/03/2026 no busca y da el error junto a Desde.</summary>
    [Fact]
    public async Task Buscar_DesdePosteriorAHasta_Rechazado()
    {
        _unidad.Sembrar(1);

        var resultado = await CrearCasoDeUso().EjecutarAsync(null, null, null, "2026-03-20", "2026-03-10", null);

        Assert.False(resultado.EsValido);
        Assert.Equal([new ErrorValidacion("desde", TextoDesdePosterior)], resultado.Errores);
        AfirmarSinBusqueda();
    }

    [Fact]
    public async Task Buscar_DesdeIgualAHasta_Busca()
    {
        _unidad.Sembrar(1);

        var resultado = await CrearCasoDeUso().EjecutarAsync(null, null, null, "2026-01-05", "2026-01-05", null);

        Assert.True(resultado.EsValido);
        Assert.Equal(1, Assert.Single(resultado.Presupuestos).Numero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Buscar_PaginaInvalida_Rechazada(int pagina)
    {
        _unidad.Sembrar(1);

        var resultado = await CrearCasoDeUso().EjecutarAsync(null, null, null, null, null, pagina);

        Assert.False(resultado.EsValido);
        // El texto es fijo para cualquier valor: no repite el recibido.
        Assert.Equal([new ErrorValidacion("pagina", TextoPagina)], resultado.Errores);
        AfirmarSinBusqueda();
    }

    [Theory]
    [InlineData(1_000_001)]
    [InlineData(int.MaxValue)]
    public async Task Buscar_PaginaFueraDeTope_Rechazada(int pagina)
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(null, null, null, null, null, pagina);

        Assert.False(resultado.EsValido);
        Assert.Equal([new ErrorValidacion("pagina", TextoPagina)], resultado.Errores);
        AfirmarSinBusqueda();
    }

    /// <summary>D-07: Apellido y Nombre de más de 100 caracteres después de recortar, 400 en su campo; 100, se busca.</summary>
    [Fact]
    public async Task Buscar_ApellidoDemasiadoLargo_Rechazado()
    {
        var largo = await CrearCasoDeUso().EjecutarAsync(new string('a', 101), null, null, null, null, null);
        var nombreLargo = await CrearCasoDeUso().EjecutarAsync(null, " " + new string('b', 101) + " ", null, null, null, null);

        Assert.Equal([new ErrorValidacion("apellido", "Usá hasta 100 caracteres.")], largo.Errores);
        Assert.Equal([new ErrorValidacion("nombre", "Usá hasta 100 caracteres.")], nombreLargo.Errores);
        AfirmarSinBusqueda();

        var justo = await CrearCasoDeUso().EjecutarAsync(
            "  " + new string('a', 100) + " \t", " " + new string('b', 100), null, null, null, null);
        Assert.True(justo.EsValido);
    }

    /// <summary>D-07: un DNI de más de 20 caracteres después de recortar, 400 en <c>dni</c>; 20, se busca.</summary>
    [Fact]
    public async Task Buscar_DniDemasiadoLargo_Rechazado()
    {
        var largo = await CrearCasoDeUso().EjecutarAsync(null, null, new string('1', 21), null, null, null);

        Assert.Equal([new ErrorValidacion("dni", "Usá hasta 20 caracteres.")], largo.Errores);
        AfirmarSinBusqueda();

        var justo = await CrearCasoDeUso().EjecutarAsync(null, null, "  " + new string('1', 20) + " ", null, null, null);
        Assert.True(justo.EsValido);
    }

    /// <summary>Todos los errores juntos, uno por campo, en el orden apellido, nombre, DNI, desde, hasta, página.</summary>
    [Fact]
    public async Task Buscar_VariosErrores_TodosPorCampo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(
            new string('a', 101), new string('b', 101), new string('1', 21), "2026-02-30", "x", 0);

        Assert.Equal(
            [
                new ErrorValidacion("apellido", "Usá hasta 100 caracteres."),
                new ErrorValidacion("nombre", "Usá hasta 100 caracteres."),
                new ErrorValidacion("dni", "Usá hasta 20 caracteres."),
                new ErrorValidacion("desde", TextoFechaInvalida),
                new ErrorValidacion("hasta", TextoFechaInvalida),
                new ErrorValidacion("pagina", TextoPagina),
            ],
            resultado.Errores);
        AfirmarSinBusqueda();
    }

    /// <summary>
    /// Alerta de datos (Ley 25.326): los records de la búsqueda no muestran Apellido, Nombre, DNI ni importes en
    /// <see cref="object.ToString"/>. Datos ficticios.
    /// </summary>
    [Fact]
    public async Task ResultadoBusqueda_ToString_SinValores()
    {
        _unidad.Guardados.Add(new Presupuesto(
            987654321,
            new DateOnly(2026, 3, 10),
            EstadoPresupuesto.Borrador,
            new ClientePresupuesto("Zq7Apellido", "Zq7Nombre", "30111222", null, null, null),
            [new LineaPresupuesto(1, "Lente orgánico", 7531m, 1, 0m, 7531m, 7531m)],
            version: 0));

        var valido = await CrearCasoDeUso().EjecutarAsync("zq7", null, null, null, null, null);
        var invalido = await CrearCasoDeUso().EjecutarAsync(new string('z', 101), null, null, null, null, null);
        var textos = new[]
        {
            Assert.Single(valido.Presupuestos).ToString(),
            new PresupuestoListado(
                987654321, new DateOnly(2026, 3, 10), "Zq7Apellido", "Zq7Nombre", "30111222", "borrador", 7531m)
                .ToString(),
            valido.ToString(),
            invalido.ToString(),
        };

        foreach (var texto in textos)
        {
            Assert.DoesNotContain("Zq7", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("30111222", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("987654321", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("7531", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("=", texto, StringComparison.Ordinal);
        }
    }

    private void AfirmarSinBusqueda() =>
        Assert.DoesNotContain(_unidad.Articulos.Accesos, acceso => acceso.Accion == PresupuestosEnMemoria.AccesoBuscar);
}
