using Optica.Application.Marca;
using Optica.Application.Presupuestos;
using Optica.Domain.Marca;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

/// <summary>
/// El caso de uso del PDF del presupuesto (Block 5 de FEAT-006, FR-11 y FR-12): lee el presupuesto; inexistente, no
/// encontrado; en Borrador, conflicto <c>borrador</c> con su texto fijo y sin leer la marca ni llamar al generador
/// (AC-12, R-07); en Final, lee la marca vigente y devuelve los bytes del generador. Las fallas de la marca y del
/// generador se propagan. Los datos del cliente son ficticios.
/// </summary>
public class GenerarPdfPresupuestoTests
{
    private const string DetalleBorrador = "El presupuesto está en Borrador.";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private readonly PresupuestosEnMemoria _unidad = new();
    private readonly ProveedorFijo _marca = new(IdentidadVisual.Crear("#0903A0", "#FFFFFF", LogoMarca.Crear("logo.png", Png), false));
    private readonly GeneradorFalso _generador = new();

    private GenerarPdfPresupuesto CrearCasoDeUso() => new(_unidad, _marca, _generador);

    /// <summary>
    /// AC-12 (FR-12, R-07): un Borrador no genera el PDF ni siquiera pedido directo; responde conflicto
    /// <c>borrador</c> con el texto fijo, sin leer la marca y sin llamar al generador.
    /// </summary>
    [Fact]
    public async Task Pdf_PresupuestoBorrador_ConflictoSinGenerar()
    {
        _unidad.Sembrar(7, EstadoPresupuesto.Borrador);

        var resultado = await CrearCasoDeUso().EjecutarAsync(7);

        Assert.Equal(TipoPdfPresupuesto.Borrador, resultado.Tipo);
        Assert.False(resultado.EsGenerado);
        Assert.Null(resultado.Contenido);
        Assert.Equal(DetalleBorrador, resultado.Detalle);
        Assert.Equal(DetalleBorrador, GenerarPdfPresupuesto.DetalleBorrador);
        Assert.Equal(0, _generador.Llamadas);
        Assert.Equal(0, _marca.Lecturas);
    }

    /// <summary>FR-11: un Final lee la marca vigente y devuelve los bytes del generador, que recibe ese presupuesto y esa marca.</summary>
    [Fact]
    public async Task GenerarPdf_Final_DevuelveLosBytesDelGenerador()
    {
        var guardado = _unidad.Sembrar(155, EstadoPresupuesto.Final, version: 1);

        var resultado = await CrearCasoDeUso().EjecutarAsync(155);

        Assert.Equal(TipoPdfPresupuesto.Generado, resultado.Tipo);
        Assert.True(resultado.EsGenerado);
        Assert.Equal(GeneradorFalso.Bytes, resultado.Contenido);
        Assert.Null(resultado.Detalle);
        Assert.Equal(1, _generador.Llamadas);
        Assert.Same(guardado, _generador.Presupuesto);
        Assert.Same(_marca.Marca, _generador.Marca);
        Assert.Equal(1, _marca.Lecturas);
    }

    /// <summary>Un número que no existe (cualquier <c>long</c>) es no encontrado, sin leer la marca ni generar.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task GenerarPdf_Inexistente_NoEncontradoSinGenerar(long numero)
    {
        _unidad.Sembrar(1, EstadoPresupuesto.Final);

        var resultado = await CrearCasoDeUso().EjecutarAsync(numero);

        Assert.Equal(TipoPdfPresupuesto.NoEncontrado, resultado.Tipo);
        Assert.False(resultado.EsGenerado);
        Assert.Null(resultado.Contenido);
        Assert.Null(resultado.Detalle);
        Assert.Equal(0, _generador.Llamadas);
        Assert.Equal(0, _marca.Lecturas);
    }

    /// <summary>
    /// Una falla del generador (por ejemplo, un logo que no se puede dibujar) se propaga sin envolverla: la Api responde
    /// el 500 genérico. Lo mismo una falla de la marca (instalación dañada).
    /// </summary>
    [Fact]
    public async Task GenerarPdf_FallaDelGeneradorODeLaMarca_SePropaga()
    {
        _unidad.Sembrar(3, EstadoPresupuesto.Final);
        var falla = new InvalidOperationException("No se pudo generar el PDF del presupuesto.");
        _generador.Falla = falla;

        var lanzada = await Assert.ThrowsAsync<InvalidOperationException>(() => CrearCasoDeUso().EjecutarAsync(3));
        Assert.Same(falla, lanzada);

        var sinMarca = new GenerarPdfPresupuesto(_unidad, new ProveedorQueFalla(), new GeneradorFalso());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sinMarca.EjecutarAsync(3));
    }

    /// <summary>La cancelación llega al repositorio: un pedido cancelado no lee ni genera.</summary>
    [Fact]
    public async Task GenerarPdf_Cancelado_NoGenera()
    {
        _unidad.Sembrar(3, EstadoPresupuesto.Final);
        using var cancelacion = new CancellationTokenSource();
        await cancelacion.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CrearCasoDeUso().EjecutarAsync(3, cancelacion.Token));
        Assert.Equal(0, _generador.Llamadas);
    }

    /// <summary>
    /// Los resultados fijos no llevan datos: <see cref="object.ToString"/> muestra solo el nombre del tipo, y el contenido
    /// devuelto no es nulo cuando se genera. Generado rechaza un contenido nulo o vacío.
    /// </summary>
    [Fact]
    public void ResultadoPdf_Fabricas()
    {
        Assert.Equal(TipoPdfPresupuesto.NoEncontrado, ResultadoPdfPresupuesto.NoEncontrado.Tipo);
        Assert.Equal(TipoPdfPresupuesto.Borrador, ResultadoPdfPresupuesto.Borrador.Tipo);
        Assert.Equal(DetalleBorrador, ResultadoPdfPresupuesto.Borrador.Detalle);
        Assert.Throws<ArgumentNullException>(() => ResultadoPdfPresupuesto.Generado(null!));
        Assert.Throws<ArgumentException>(() => ResultadoPdfPresupuesto.Generado([]));
        var generado = ResultadoPdfPresupuesto.Generado([1, 2, 3]);
        Assert.Equal(typeof(ResultadoPdfPresupuesto).FullName, generado.ToString());
    }

    /// <summary>Devuelve siempre la misma marca y cuenta las lecturas.</summary>
    private sealed class ProveedorFijo(IdentidadVisual marca) : IProveedorMarca
    {
        public IdentidadVisual Marca => marca;

        public int Lecturas { get; private set; }

        public IdentidadVisual Obtener()
        {
            Lecturas++;
            return marca;
        }
    }

    private sealed class ProveedorQueFalla : IProveedorMarca
    {
        public IdentidadVisual Obtener() => throw new InvalidOperationException("sin marca");
    }

    /// <summary>Anota lo recibido y devuelve bytes fijos, o lanza <see cref="Falla"/> si se fijó.</summary>
    private sealed class GeneradorFalso : IGeneradorPdfPresupuesto
    {
        public static readonly byte[] Bytes = [0x25, 0x50, 0x44, 0x46, 0x2D];

        public int Llamadas { get; private set; }

        public Presupuesto? Presupuesto { get; private set; }

        public IdentidadVisual? Marca { get; private set; }

        public Exception? Falla { get; set; }

        public byte[] Generar(Presupuesto presupuesto, IdentidadVisual marca)
        {
            Llamadas++;
            Presupuesto = presupuesto;
            Marca = marca;
            if (Falla is not null)
            {
                throw Falla;
            }

            return Bytes;
        }
    }
}
