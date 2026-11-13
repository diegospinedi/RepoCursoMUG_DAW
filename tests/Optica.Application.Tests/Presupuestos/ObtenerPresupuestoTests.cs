using Optica.Application.Presupuestos;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

public class ObtenerPresupuestoTests
{
    private readonly PresupuestosEnMemoria _unidad = new();

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task Obtener_Inexistente_Null(long numero)
    {
        _unidad.Sembrar(1);

        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(numero);

        Assert.Null(leido);
    }

    [Fact]
    public async Task Obtener_Existente_DevuelveLoGuardado()
    {
        _unidad.Sembrar(1);
        var guardado = _unidad.Sembrar(154);

        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(154);

        Assert.NotNull(leido);
        Assert.Equal(154, leido.Numero);
        Assert.Equal(guardado.Fecha, leido.Fecha);
        Assert.Equal("borrador", leido.Estado);
        Assert.Equal(new ClienteGrabado("Gómez", "Luis", "30111222", null, null, null), leido.Cliente);
        Assert.Equal([new LineaGrabada(1, "Lente orgánico", 100m, 1, 0m, 100m, 100m, 1)], leido.Lineas);
        Assert.Equal(0, leido.Version);
    }

    /// <summary>
    /// FEAT-003c (D-01, D-10): la respuesta trae la versión del dominio, para la concurrencia optimista, y el
    /// <c>orden</c> de cada línea, 1..n en el orden grabado.
    /// </summary>
    [Fact]
    public async Task Obtener_TraeVersionYOrden()
    {
        _unidad.Guardados.Add(new Presupuesto(
            3,
            new DateOnly(2026, 3, 10),
            EstadoPresupuesto.Borrador,
            new ClientePresupuesto("Ficticiez", "Prueboncia", "00718294", null, "prueba@example.com", null),
            [
                new LineaPresupuesto(2, "Lente bifocal", 1000m, 1, 0m, 1000m, 1000m),
                new LineaPresupuesto(1, "Paño", 10m, 1, 0m, 10m, 10m),
            ],
            version: 7));

        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(3);

        Assert.NotNull(leido);
        Assert.Equal(7, leido.Version);
        Assert.Equal([1, 2], leido.Lineas.Select(linea => linea.Orden));
        Assert.Equal([2L, 1L], leido.Lineas.Select(linea => linea.CodigoArticulo));
    }

    /// <summary><c>Version</c> es obligatoria al construir un <see cref="PresupuestoGrabado"/>, como <c>Total</c>.</summary>
    [Fact]
    public void PresupuestoGrabado_VersionEsObligatoria()
    {
        var version = typeof(PresupuestoGrabado).GetProperty(nameof(PresupuestoGrabado.Version));

        Assert.NotNull(version);
        Assert.True(version.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false));
    }

    /// <summary>
    /// D-05: el total no se guarda ni lo vuelve a sumar Application; <c>PresupuestoGrabado.Total</c> es el
    /// <see cref="Presupuesto.Total"/> del dominio, con su misma escala.
    /// </summary>
    [Fact]
    public async Task PresupuestoGrabado_TotalCopiadoDelDominio()
    {
        var guardado = new Presupuesto(
            9,
            new DateOnly(2026, 1, 5),
            EstadoPresupuesto.Borrador,
            new ClientePresupuesto("Ficticiez", "Prueboncia", "00718294", null, null, null),
            [
                new LineaPresupuesto(1, "Paño", 6.67m, 3, 50m, 3.34m, 10.02m),
                new LineaPresupuesto(2, "Lente bifocal", 1000m, 3, 10m, 900.00m, 2700.00m),
            ],
            version: 0);
        _unidad.Guardados.Add(guardado);

        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(9);

        Assert.NotNull(leido);
        Assert.Equal(2710.02m, guardado.Total);
        Assert.Equal(guardado.Total, leido.Total);
        Assert.Equal(
            guardado.Total.ToString(System.Globalization.CultureInfo.InvariantCulture),
            leido.Total.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(
            [new LineaGrabada(1, "Paño", 6.67m, 3, 50m, 3.34m, 10.02m, 1),
                new LineaGrabada(2, "Lente bifocal", 1000m, 3, 10m, 900m, 2700m, 2)],
            leido.Lineas);
    }

    /// <summary>
    /// <c>Total</c> es obligatorio al construir un <see cref="PresupuestoGrabado"/>: fuera de <c>Desde</c> no puede
    /// quedar en 0 por olvido.
    /// </summary>
    [Fact]
    public void PresupuestoGrabado_TotalEsObligatorio()
    {
        var total = typeof(PresupuestoGrabado).GetProperty(nameof(PresupuestoGrabado.Total));

        Assert.NotNull(total);
        Assert.True(total.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false));
    }

    /// <summary>
    /// El estado del dominio se devuelve como texto fijo en minúsculas (D-19): Borrador "borrador" y Final "final". FEAT-003a
    /// solo graba Borrador, pero un presupuesto Final guardado se lee con su texto.
    /// </summary>
    [Theory]
    [InlineData(EstadoPresupuesto.Borrador, "borrador")]
    [InlineData(EstadoPresupuesto.Final, "final")]
    public async Task Obtener_CadaEstado_SuTexto(EstadoPresupuesto estado, string texto)
    {
        _unidad.Sembrar(7, estado);

        var leido = await new ObtenerPresupuesto(_unidad).EjecutarAsync(7);

        Assert.NotNull(leido);
        Assert.Equal(texto, leido.Estado);
        Assert.Equal(texto, PresupuestoGrabado.TextoDelEstado(estado));
        Assert.Equal(PresupuestoGrabado.EstadoBorrador, PresupuestoGrabado.TextoDelEstado(EstadoPresupuesto.Borrador));
        Assert.Equal(PresupuestoGrabado.EstadoFinal, PresupuestoGrabado.TextoDelEstado(EstadoPresupuesto.Final));
    }

    /// <summary>
    /// Cerrado por defecto: un estado sin texto asignado lanza con un mensaje fijo, sin el valor. El dominio no construye
    /// un presupuesto con un estado así; la guarda fija que un estado nuevo no pase sin traducción.
    /// </summary>
    [Fact]
    public void TextoDelEstado_EstadoNoPrevisto_Lanza()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => PresupuestoGrabado.TextoDelEstado((EstadoPresupuesto)99));

        Assert.DoesNotContain("99", error.Message, StringComparison.Ordinal);
    }
}
