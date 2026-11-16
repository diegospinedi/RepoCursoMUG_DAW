using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Optica.Application.Marca;
using Optica.Domain.Configuracion;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Facturacion;

/// <summary>Cómo terminó el pedido del PDF de una factura (FEAT-009).</summary>
public enum TipoPdfFactura
{
    Generado,
    NoEncontrado,
    SinDatosEmisor,
}

/// <summary>
/// El resultado de <see cref="GenerarPdfFactura"/>: generado, con los bytes del PDF y el nombre del archivo
/// (<c>factura-B-0003-00000125.pdf</c>, sin datos del cliente); no encontrado (sin presupuesto o sin factura); o sin los
/// datos del emisor (FR-04).
/// </summary>
/// <remarks>
/// El PDF lleva datos del receptor: <see cref="object.ToString"/> no se sobrescribe y muestra solo el nombre del tipo,
/// como en <c>ResultadoPdfPresupuesto</c>.
/// </remarks>
public sealed class ResultadoPdfFactura
{
    private ResultadoPdfFactura(TipoPdfFactura tipo, byte[]? contenido, string? nombreArchivo)
    {
        Tipo = tipo;
        Contenido = contenido;
        NombreArchivo = nombreArchivo;
    }

    /// <summary>El presupuesto no existe o no tiene factura.</summary>
    public static ResultadoPdfFactura NoEncontrado { get; } = new(TipoPdfFactura.NoEncontrado, null, null);

    /// <summary>Faltan los datos del emisor: no se genera el PDF (FR-04, AC-05).</summary>
    public static ResultadoPdfFactura SinDatosEmisor { get; } = new(TipoPdfFactura.SinDatosEmisor, null, null);

    public TipoPdfFactura Tipo { get; }

    /// <summary>Los bytes del PDF; null salvo cuando <see cref="Tipo"/> es <see cref="TipoPdfFactura.Generado"/>.</summary>
    public byte[]? Contenido { get; }

    /// <summary>"factura-B-0003-00000125.pdf"; null salvo cuando se generó.</summary>
    public string? NombreArchivo { get; }

    [MemberNotNullWhen(true, nameof(Contenido), nameof(NombreArchivo))]
    public bool EsGenerado => Tipo == TipoPdfFactura.Generado && Contenido is not null && NombreArchivo is not null;

    /// <exception cref="ArgumentNullException">El contenido o el comprobante son null.</exception>
    /// <exception cref="ArgumentException">El contenido está vacío.</exception>
    public static ResultadoPdfFactura Generado(byte[] contenido, NumeroComprobante comprobante)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        ArgumentNullException.ThrowIfNull(comprobante);
        if (contenido.Length == 0)
        {
            throw new ArgumentException("El PDF generado está vacío.", nameof(contenido));
        }

        return new ResultadoPdfFactura(TipoPdfFactura.Generado, contenido, NombreArchivoDe(comprobante));
    }

    /// <summary>
    /// "factura-B-0003-00000125.pdf": el tipo, el punto de venta con al menos 4 dígitos y el número con 8, como el
    /// comprobante visible (D-19 de FEAT-007), sin datos del cliente (threat model, Information Disclosure).
    /// </summary>
    private static string NombreArchivoDe(NumeroComprobante comprobante)
    {
        var letra = comprobante.Tipo == TipoComprobante.FacturaB ? "B" : "C";
        return string.Create(
            CultureInfo.InvariantCulture, $"factura-{letra}-{comprobante.PuntoVenta:D4}-{comprobante.Numero:D8}.pdf");
    }
}

/// <summary>
/// El PDF de la factura emitida desde el presupuesto <c>numero</c> (FR-04 y FR-05 de FEAT-009): lee el presupuesto y su
/// factura (cualquiera de los dos falta, no encontrado); lee los datos del emisor vigentes (sin ellos,
/// <see cref="ResultadoPdfFactura.SinDatosEmisor"/> sin leer la marca ni llamar al generador); arma el
/// <see cref="DocumentoFactura"/> con la factura, el emisor, el Apellido y el Nombre del cliente del presupuesto (solo
/// se dibujan si la factura se emitió con DNI, FR-08) y la marca de <see cref="IProveedorMarca"/>, y devuelve los bytes
/// de <see cref="IGeneradorPdfFactura"/>.
/// </summary>
/// <remarks>
/// Los datos del emisor son los vigentes al generar, no los del día de la emisión (Out of Scope del PRD, ADR-028). Es
/// una lectura: no abre una transacción de escritura. Las fallas de los repositorios, de la marca, del documento y del
/// generador se propagan sin envolverlas; la Api responde el 500 genérico. No recibe un registrador: no registra los
/// datos del receptor ni la descarga (R-02), como <c>GenerarPdfPresupuesto</c>.
/// </remarks>
public sealed class GenerarPdfFactura(
    IPresupuestoRepository presupuestos, IFacturaRepository facturas, IDatosEmisorRepository emisor,
    IProveedorMarca proveedorMarca, IGeneradorPdfFactura generador)
{
    public async Task<ResultadoPdfFactura> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        if (await presupuestos.ObtenerAsync(numero, cancelacion) is not { } presupuesto
            || await facturas.ObtenerPorPresupuestoAsync(numero, cancelacion) is not { } factura)
        {
            return ResultadoPdfFactura.NoEncontrado;
        }

        // FR-04: sin los cinco datos del emisor no hay PDF. La fila existe solo con los cinco (DatosEmisor valida al
        // grabar y al leer), así que "falta alguno" es "no hay fila".
        if (await emisor.ObtenerAsync(cancelacion) is not { } datosEmisor)
        {
            return ResultadoPdfFactura.SinDatosEmisor;
        }

        var marca = proveedorMarca.Obtener();
        var cliente = presupuesto.Cliente;
        var documento = DocumentoFactura.Desde(factura, datosEmisor, cliente.Apellido, cliente.Nombre, marca);
        return ResultadoPdfFactura.Generado(generador.Generar(documento), factura.Comprobante);
    }
}
