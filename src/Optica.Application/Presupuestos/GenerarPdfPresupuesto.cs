using System.Diagnostics.CodeAnalysis;
using Optica.Application.Marca;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>Cómo terminó el pedido del PDF de un presupuesto (FEAT-006).</summary>
public enum TipoPdfPresupuesto
{
    Generado,
    NoEncontrado,
    Borrador,
}

/// <summary>
/// El resultado de <see cref="GenerarPdfPresupuesto"/>: generado, con los bytes del PDF; no encontrado; o en Borrador
/// (FR-12), con el texto fijo de <see cref="Detalle"/>, que no repite datos del presupuesto.
/// </summary>
/// <remarks>
/// El PDF lleva los datos del cliente: <see cref="object.ToString"/> no se sobrescribe y muestra solo el nombre del
/// tipo, como en <see cref="ResultadoEdicionPresupuesto"/>.
/// </remarks>
public sealed class ResultadoPdfPresupuesto
{
    private ResultadoPdfPresupuesto(TipoPdfPresupuesto tipo, byte[]? contenido, string? detalle)
    {
        Tipo = tipo;
        Contenido = contenido;
        Detalle = detalle;
    }

    /// <summary>El presupuesto no existe.</summary>
    public static ResultadoPdfPresupuesto NoEncontrado { get; } = new(TipoPdfPresupuesto.NoEncontrado, null, null);

    /// <summary>El presupuesto está en Borrador: no se genera su PDF (FR-12, AC-12, D-11, D-16).</summary>
    public static ResultadoPdfPresupuesto Borrador { get; } =
        new(TipoPdfPresupuesto.Borrador, null, GenerarPdfPresupuesto.DetalleBorrador);

    public TipoPdfPresupuesto Tipo { get; }

    /// <summary>Los bytes del PDF; null salvo cuando <see cref="Tipo"/> es <see cref="TipoPdfPresupuesto.Generado"/>.</summary>
    public byte[]? Contenido { get; }

    /// <summary>El texto fijo del rechazo por Borrador; null en los demás resultados.</summary>
    public string? Detalle { get; }

    [MemberNotNullWhen(true, nameof(Contenido))]
    public bool EsGenerado => Tipo == TipoPdfPresupuesto.Generado && Contenido is not null;

    /// <exception cref="ArgumentNullException">El contenido es null.</exception>
    /// <exception cref="ArgumentException">El contenido está vacío.</exception>
    public static ResultadoPdfPresupuesto Generado(byte[] contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        if (contenido.Length == 0)
        {
            throw new ArgumentException("El PDF generado está vacío.", nameof(contenido));
        }

        return new ResultadoPdfPresupuesto(TipoPdfPresupuesto.Generado, contenido, null);
    }
}

/// <summary>
/// El PDF de un presupuesto en Final (FR-11 y FR-12 de FEAT-006): lee el presupuesto por número (inexistente, no
/// encontrado); en Borrador, devuelve <see cref="ResultadoPdfPresupuesto.Borrador"/> sin leer la marca ni llamar al
/// generador (AC-12, R-07), también ante un pedido directo a la Api; en Final, lee la marca vigente con
/// <see cref="IProveedorMarca"/> y devuelve los bytes de <see cref="IGeneradorPdfPresupuesto"/>.
/// </summary>
/// <remarks>
/// Es una lectura: no abre una transacción de escritura. Las fallas del repositorio, de la marca y del generador se
/// propagan sin atenderlas ni envolverlas; la Api responde el 500 genérico. El generador relanza toda falla de la
/// biblioteca de PDF con un texto fijo y sin la causa (R-10, D-15). No recibe un registrador: no registra los datos del
/// cliente ni la descarga en el log (R-02).
/// </remarks>
public sealed class GenerarPdfPresupuesto(
    IPresupuestoRepository presupuestos, IProveedorMarca proveedorMarca, IGeneradorPdfPresupuesto generador)
{
    /// <summary>El <c>detail</c> del 409 <c>borrador</c>: el PDF solo se genera de un Final (D-16).</summary>
    public const string DetalleBorrador = "El presupuesto está en Borrador.";

    public async Task<ResultadoPdfPresupuesto> EjecutarAsync(long numero, CancellationToken cancelacion = default)
    {
        var presupuesto = await presupuestos.ObtenerAsync(numero, cancelacion);
        if (presupuesto is null)
        {
            return ResultadoPdfPresupuesto.NoEncontrado;
        }

        if (presupuesto.Estado != EstadoPresupuesto.Final)
        {
            // FR-12: un Borrador no se genera, ni siquiera pedido directo a la Api; el generador no se llama.
            return ResultadoPdfPresupuesto.Borrador;
        }

        var marca = proveedorMarca.Obtener();
        return ResultadoPdfPresupuesto.Generado(generador.Generar(presupuesto, marca));
    }
}
