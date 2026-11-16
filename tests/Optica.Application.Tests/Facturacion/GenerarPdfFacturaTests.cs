using Optica.Application.Facturacion;
using Optica.Application.Marca;
using Optica.Application.Tests.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Marca;

namespace Optica.Application.Tests.Facturacion;

/// <summary>
/// Spec FEAT-009, Block 5 (FR-04, FR-05, AC-05, AC-06): el PDF de la factura de un presupuesto. Sin factura, no
/// encontrado; sin los datos del emisor, <see cref="TipoPdfFactura.SinDatosEmisor"/> sin generar nada; con todo, el
/// documento armado con la factura, los datos del emisor vigentes, el cliente del presupuesto y la marca, y el nombre de
/// archivo sin datos del cliente. Datos ficticios: CUIT 20-00000000-1 (D-33), razón social inventada.
/// </summary>
public sealed class GenerarPdfFacturaTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private readonly AlmacenFacturacion _almacen = new();
    private readonly DatosEmisorEnMemoria _emisor = new()
    {
        Guardados = DatosEmisorEnMemoria.Crear(
            "Zq7Óptica Inventada SA", "Calle Inventada 123", "20-00000000-1", "IB-0001", "2020-03-01"),
    };

    private readonly ProveedorFijo _marca =
        new(IdentidadVisual.Crear("#0903A0", "#FFFFFF", LogoMarca.Crear("logo.png", Png), false));

    private readonly GeneradorFalso _generador = new();

    private GenerarPdfFactura CrearCasoDeUso() => new(_almacen, _almacen, _emisor, _marca, _generador);

    /// <summary>
    /// AC-05 (FR-04, sad path): sin los datos del emisor (la razón social, entre ellos, nunca se cargó) no se genera el PDF:
    /// el resultado es <see cref="TipoPdfFactura.SinDatosEmisor"/>, sin leer la marca ni llamar al generador.
    /// </summary>
    [Fact]
    public async Task GenerarPdf_SinRazonSocial_SinDatosEmisor()
    {
        SembrarFactura(155);
        _emisor.Guardados = null;

        var resultado = await CrearCasoDeUso().EjecutarAsync(155);

        Assert.Equal(TipoPdfFactura.SinDatosEmisor, resultado.Tipo);
        Assert.False(resultado.EsGenerado);
        Assert.Null(resultado.Contenido);
        Assert.Null(resultado.NombreArchivo);
        Assert.Equal(0, _generador.Llamadas);
        Assert.Equal(0, _marca.Lecturas);
    }

    /// <summary>
    /// Sad path: un presupuesto inexistente, o uno en Final sin factura, es no encontrado, sin leer el emisor ni la marca
    /// y sin generar.
    /// </summary>
    [Theory]
    [InlineData(999)]
    [InlineData(156)]
    [InlineData(0)]
    [InlineData(long.MaxValue)]
    public async Task GenerarPdf_Inexistente_NoEncontrado(long numero)
    {
        SembrarFactura(155);
        _almacen.Presupuestos.Add(FacturasDePrueba.PresupuestoPerezAna(156, 1815m));

        var resultado = await CrearCasoDeUso().EjecutarAsync(numero);

        Assert.Equal(TipoPdfFactura.NoEncontrado, resultado.Tipo);
        Assert.False(resultado.EsGenerado);
        Assert.Null(resultado.Contenido);
        Assert.Equal(0, _emisor.Lecturas);
        Assert.Equal(0, _marca.Lecturas);
        Assert.Equal(0, _generador.Llamadas);
    }

    /// <summary>
    /// AC-06 (FR-05): con factura y datos del emisor, el generador recibe el documento de esa factura con el emisor
    /// vigente, el cliente del presupuesto y la marca; el resultado trae sus bytes y el nombre
    /// <c>factura-B-0003-00000125.pdf</c>, sin datos del cliente.
    /// </summary>
    [Fact]
    public async Task GenerarPdf_ConFactura_GeneradoConElNombreDelComprobante()
    {
        SembrarFactura(155, ReceptorFactura.ConDni("00123456"));

        var resultado = await CrearCasoDeUso().EjecutarAsync(155);

        Assert.Equal(TipoPdfFactura.Generado, resultado.Tipo);
        Assert.True(resultado.EsGenerado);
        Assert.Equal(GeneradorFalso.Bytes, resultado.Contenido);
        Assert.Equal("factura-B-0003-00000125.pdf", resultado.NombreArchivo);
        var documento = Assert.IsType<DocumentoFactura>(_generador.Documento);
        Assert.Equal("Zq7Óptica Inventada SA", documento.RazonSocial);
        Assert.Equal("20000000001", documento.CuitEmisor);
        Assert.Equal("Factura B 0003-00000125", documento.Comprobante.ToString());
        Assert.Equal("00123456", documento.DniReceptor);
        Assert.Equal("Pérez", documento.ApellidoReceptor);
        Assert.Equal("Ana", documento.NombreReceptor);
        Assert.Same(_marca.Marca, documento.Marca);
        Assert.True(documento.LlevaLeyendaSimulado);
        // El resultado no muestra datos en su ToString.
        Assert.Equal(typeof(ResultadoPdfFactura).FullName, resultado.ToString());
    }

    /// <summary>Una falla del generador se propaga sin envolverla: la Api responde el 500 genérico.</summary>
    [Fact]
    public async Task GenerarPdf_FallaDelGenerador_SePropaga()
    {
        SembrarFactura(155);
        var falla = new InvalidOperationException("No se pudo generar el PDF de la factura.");
        _generador.Falla = falla;

        var lanzada = await Assert.ThrowsAsync<InvalidOperationException>(() => CrearCasoDeUso().EjecutarAsync(155));

        Assert.Same(falla, lanzada);
    }

    private void SembrarFactura(long numero, ReceptorFactura? receptor = null)
    {
        var presupuesto = FacturasDePrueba.PresupuestoPerezAna(numero, 1500m, 315m);
        _almacen.Presupuestos.Add(presupuesto);
        _almacen.Facturas.Add(
            FacturasDePrueba.FacturaB(presupuesto, receptor ?? ReceptorFactura.SinIdentificar, EntornoArca.Simulado));
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

    /// <summary>Anota el documento recibido y devuelve bytes fijos, o lanza <see cref="Falla"/> si se fijó.</summary>
    private sealed class GeneradorFalso : IGeneradorPdfFactura
    {
        public static readonly byte[] Bytes = [0x25, 0x50, 0x44, 0x46, 0x2D];

        public int Llamadas { get; private set; }

        public DocumentoFactura? Documento { get; private set; }

        public Exception? Falla { get; set; }

        public byte[] Generar(DocumentoFactura documento)
        {
            Llamadas++;
            Documento = documento;
            if (Falla is not null)
            {
                throw Falla;
            }

            return Bytes;
        }
    }
}
