using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Facturacion;
using Optica.Domain.Presupuestos;

namespace Optica.Pruebas.Compartido;

/// <summary>
/// Una factura a sembrar con <see cref="SiembraFacturas"/>: el presupuesto de origen con su cliente y la identificación
/// del comprobante. El total es siempre <see cref="SiembraFacturas.TotalSembrado"/>.
/// </summary>
/// <remarks>Lleva datos del cliente (ficticios): <see cref="object.ToString"/> no muestra ningún valor.</remarks>
public sealed record FacturaSembrada(
    long Presupuesto,
    DateOnly Fecha,
    string Apellido,
    string Nombre,
    string Dni,
    int PuntoVenta,
    long Numero,
    bool Identificado = true,
    TipoComprobante Tipo = TipoComprobante.FacturaB)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Siembra de facturas para las pruebas de la búsqueda (Block 6 de FEAT-009, AC-17). Optica.Api.Tests y
/// Optica.Infrastructure.Tests enlazan este archivo y la usan con los servicios de su base temporal. Inserta por la
/// aplicación, como la emisión: un artículo con <see cref="IArticuloRepository"/>; cada presupuesto con
/// <see cref="IPresupuestoRepository.Agregar"/> en Borrador y después a Final con
/// <see cref="IPresupuestoRepository.CambiarEstado"/> (AGENTS.md); cada factura armada con <see cref="Factura.Emitida"/> y
/// grabada con <see cref="IFacturaRepository.Agregar"/>. No toca los triggers de cierre ni de facturas.
/// </summary>
/// <remarks>
/// Los datos son ficticios: DNI que empiezan con 00 (D-08) y un CAE inventado. Las facturas se emiten en el entorno
/// <see cref="EntornoArca.Simulado"/>. Los mensajes de error son fijos y no adjuntan la excepción del dominio.
/// </remarks>
public static class SiembraFacturas
{
    /// <summary>El volumen de AC-17: 120 facturas.</summary>
    public const int Volumen = 120;

    /// <summary>El total de cada factura sembrada: una línea de $1.000.</summary>
    public const decimal TotalSembrado = 1000m;

    /// <summary>Un CAE inventado de 14 dígitos.</summary>
    public const string Cae = "00000000000001";

    /// <summary>El punto de venta de las facturas del volumen.</summary>
    public const int PuntoVentaDelVolumen = 3;

    /// <summary>El dominio rechazó un dato; falla antes de insertar.</summary>
    public const string MensajeDatoRechazado =
        "Siembra de facturas: el dominio rechazó un presupuesto o una factura. No se insertó ninguna factura.";

    private const string CodigoProveedor = "SIEMBRA-FACTURAS";
    private const string Descripcion = "Armazón sembrado";

    /// <summary>La fecha de la factura más antigua del volumen; cada una de las siguientes es un día después.</summary>
    public static readonly DateOnly PrimeraFecha = new(2026, 1, 1);

    /// <summary>
    /// Las <paramref name="cantidad"/> facturas de AC-17, deterministas: presupuesto y número i (1..n), punto de venta 3,
    /// fecha <see cref="PrimeraFecha"/> + (i − 1) días, cliente "Ficticio, Sembrada i" con DNI "00" + i en 6 dígitos.
    /// </summary>
    public static IReadOnlyList<FacturaSembrada> DelVolumen(int cantidad = Volumen) =>
    [
        .. Enumerable.Range(1, cantidad).Select(i => new FacturaSembrada(
            i,
            PrimeraFecha.AddDays(i - 1),
            "Ficticio",
            string.Create(CultureInfo.InvariantCulture, $"Sembrada {i:D3}"),
            string.Create(CultureInfo.InvariantCulture, $"00{i:D6}"),
            PuntoVentaDelVolumen,
            i)),
    ];

    /// <summary>
    /// Graba las facturas con sus presupuestos, en tres confirmaciones: el artículo y los Borradores, el paso a Final, y
    /// las facturas. Los números de presupuesto no pueden existir en la base.
    /// </summary>
    /// <exception cref="InvalidOperationException">El dominio rechazó un dato. El mensaje es fijo.</exception>
    public static async Task SembrarAsync(IServiceProvider servicios, IReadOnlyList<FacturaSembrada> facturas)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(facturas);

        var codigo = await GrabarArticuloAsync(servicios);
        var borradores = Armar(facturas, codigo);

        using (var alcance = servicios.CreateScope())
        {
            var presupuestos = alcance.ServiceProvider.GetRequiredService<IPresupuestoRepository>();
            foreach (var borrador in borradores)
            {
                presupuestos.Agregar(borrador);
            }

            await alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
        }

        var finales = new List<Presupuesto>(borradores.Count);
        using (var alcance = servicios.CreateScope())
        {
            var presupuestos = alcance.ServiceProvider.GetRequiredService<IPresupuestoRepository>();
            foreach (var borrador in borradores)
            {
                var final = borrador.PasarAFinal();
                presupuestos.CambiarEstado(final, borrador.Version);
                finales.Add(final);
            }

            await alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
        }

        var emitidas = Emitir(facturas, finales);
        using (var alcance = servicios.CreateScope())
        {
            var repositorio = alcance.ServiceProvider.GetRequiredService<IFacturaRepository>();
            foreach (var factura in emitidas)
            {
                repositorio.Agregar(factura);
            }

            await alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
        }
    }

    private static async Task<long> GrabarArticuloAsync(IServiceProvider servicios)
    {
        var clave = NormalizacionCodigo.Clave(CodigoProveedor);
        using var alcance = servicios.CreateScope();
        var articulos = alcance.ServiceProvider.GetRequiredService<IArticuloRepository>();
        if (await articulos.ObtenerPorClaveAsync(clave) is { Codigo: { } existente })
        {
            return existente;
        }

        articulos.Agregar(Articulo.Nuevo(CodigoProveedor, Descripcion, TotalSembrado, 0m, TotalSembrado));
        await alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync();
        return (await articulos.ObtenerPorClaveAsync(clave))?.Codigo
            ?? throw new InvalidOperationException(MensajeDatoRechazado);
    }

    private static List<Presupuesto> Armar(IReadOnlyList<FacturaSembrada> facturas, long codigo)
    {
        try
        {
            return
            [
                .. facturas.Select(f => Presupuesto.Nuevo(
                    f.Presupuesto,
                    f.Fecha,
                    new ClientePresupuesto(f.Apellido, f.Nombre, f.Dni, null, null, null),
                    [new LineaPresupuesto(codigo, Descripcion, TotalSembrado, 1, 0m, TotalSembrado, TotalSembrado)])),
            ];
        }
        catch (ArgumentException)
        {
            // Sin la causa: el dato del cliente podría viajar en ella.
            throw new InvalidOperationException(MensajeDatoRechazado);
        }
    }

    private static List<Factura> Emitir(IReadOnlyList<FacturaSembrada> facturas, IReadOnlyList<Presupuesto> finales)
    {
        try
        {
            return
            [
                .. facturas.Select((f, indice) =>
                {
                    var presupuesto = finales[indice];
                    var receptor = f.Identificado
                        ? ReceptorFactura.ConDni(presupuesto.Cliente.Dni)
                        : ReceptorFactura.SinIdentificar;
                    var importes = f.Tipo == TipoComprobante.FacturaB
                        ? ImportesFactura.ParaFacturaB(presupuesto.Total, 21m)
                        : ImportesFactura.ParaFacturaC(presupuesto.Total);
                    var solicitud = new SolicitudComprobante(
                        f.Tipo, f.PuntoVenta, f.Fecha, ConceptoFactura.Productos, receptor, importes);
                    return Factura.Emitida(presupuesto, solicitud, f.Numero, Cae, f.Fecha.AddDays(10), EntornoArca.Simulado);
                }),
            ];
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(MensajeDatoRechazado);
        }
    }
}
