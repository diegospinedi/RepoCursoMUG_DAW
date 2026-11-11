using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>Los seis datos del cliente tal como llegan; <see cref="PoliticaCliente"/> los valida, un error por campo.</summary>
/// <remarks>
/// Datos personales (Ley 25.326): <see cref="object.ToString"/> no muestra ningún valor (<c>DatosCliente { }</c>), así no
/// llegan a un log que reciba el record (D-17).
/// </remarks>
public sealed record DatosCliente(
    string? Apellido, string? Nombre, string? Dni, string? Domicilio, string? Email, string? Telefono)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// El pedido de alta: el cliente y las líneas en orden (D-12, D-13). Las líneas no llevan precios ni descripción: el
/// servidor los copia del catálogo y los calcula (FR-10, FR-11; R-01 de FEAT-003b).
/// </summary>
/// <remarks>Lleva el cliente: <see cref="object.ToString"/> no muestra ningún valor (D-17).</remarks>
public sealed record DatosPresupuesto(DatosCliente? Cliente, IReadOnlyList<DatosLinea?>? Lineas)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>
/// Una línea tal como llega: el código autonumérico del artículo, la Cantidad y el Porcentaje de Descuento (FR-01 a
/// FR-03). <see cref="ValidacionPresupuesto.Validar"/> los valida, un error por campo.
/// </summary>
/// <remarks>
/// <see cref="object.ToString"/> no muestra ningún valor (<c>DatosLinea { }</c>), como los demás datos del pedido: las
/// condiciones comerciales de la venta tampoco van al log (D-17).
/// </remarks>
public sealed record DatosLinea(long? CodigoArticulo, int? Cantidad, decimal? PorcentajeDescuento)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;
}

/// <summary>El cliente grabado: textos recortados, DNI sin puntos y null en los opcionales vacíos.</summary>
/// <remarks>Datos personales: <see cref="object.ToString"/> no muestra ningún valor (D-17).</remarks>
public sealed record ClienteGrabado(
    string Apellido, string Nombre, string Dni, string? Domicilio, string? Email, string? Telefono)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    internal static ClienteGrabado Desde(ClientePresupuesto cliente) => new(
        cliente.Apellido, cliente.Nombre, cliente.Dni, cliente.Domicilio, cliente.Email, cliente.Telefono);
}

/// <summary>Una línea grabada con las siete columnas de FR-13.</summary>
public sealed record LineaGrabada(
    long CodigoArticulo,
    string Descripcion,
    decimal PrecioUnitario,
    int Cantidad,
    decimal PorcentajeDescuento,
    decimal PrecioConDescuento,
    decimal PrecioFinal)
{
    internal static LineaGrabada Desde(LineaPresupuesto linea) => new(
        linea.CodigoArticulo,
        linea.Descripcion,
        linea.PrecioUnitario,
        linea.Cantidad,
        linea.PorcentajeDescuento,
        linea.PrecioConDescuento,
        linea.PrecioFinal);
}

/// <summary>
/// Un presupuesto grabado: número, fecha local sin hora, estado como texto (<see cref="EstadoBorrador"/> o
/// <see cref="EstadoFinal"/>), cliente, líneas en el orden cargado y <see cref="Total"/>.
/// </summary>
/// <remarks>
/// Lleva el cliente: <see cref="object.ToString"/> no muestra ningún valor (D-17). El estado es texto y no el enum del
/// dominio, así la Api lo devuelve sin compilar contra <c>Optica.Domain</c>.
/// </remarks>
public sealed record PresupuestoGrabado(
    long Numero, DateOnly Fecha, string Estado, ClienteGrabado Cliente, IReadOnlyList<LineaGrabada> Lineas)
{
    /// <summary>El texto de <see cref="EstadoPresupuesto.Borrador"/> (D-19).</summary>
    public const string EstadoBorrador = "borrador";

    /// <summary>El texto de <see cref="EstadoPresupuesto.Final"/> (D-19).</summary>
    public const string EstadoFinal = "final";

    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    /// <summary>
    /// La suma de los Precios Finales, con IVA incluido (FR-06). No se guarda ni se vuelve a sumar acá: es
    /// <see cref="Presupuesto.Total"/> del dominio, copiado por <see cref="Desde"/> (D-05). Va fuera del constructor
    /// posicional; la Api lo escribe como <c>total</c>, después de <c>lineas</c>. Es <c>required</c>: quien construya un
    /// presupuesto grabado fuera de <see cref="Desde"/> tiene que darlo, así no queda en 0 por olvido.
    /// </summary>
    public required decimal Total { get; init; }

    /// <summary>El texto del estado.</summary>
    /// <exception cref="InvalidOperationException">El estado no tiene texto asignado (cerrado por defecto).</exception>
    public static string TextoDelEstado(EstadoPresupuesto estado) => estado switch
    {
        EstadoPresupuesto.Borrador => EstadoBorrador,
        EstadoPresupuesto.Final => EstadoFinal,
        _ => throw new InvalidOperationException("Estado de presupuesto sin texto asignado."),
    };

    internal static PresupuestoGrabado Desde(Presupuesto presupuesto) => new(
        presupuesto.Numero,
        presupuesto.Fecha,
        TextoDelEstado(presupuesto.Estado),
        ClienteGrabado.Desde(presupuesto.Cliente),
        [.. presupuesto.Lineas.Select(LineaGrabada.Desde)])
    {
        Total = presupuesto.Total,
    };
}

/// <summary>Cómo terminó un alta de presupuesto: grabado, con el presupuesto, o inválido, con un error por campo.</summary>
public sealed class ResultadoAltaPresupuesto
{
    private ResultadoAltaPresupuesto(PresupuestoGrabado? presupuesto, IReadOnlyList<ErrorValidacion> errores)
    {
        Presupuesto = presupuesto;
        Errores = errores;
    }

    /// <summary>El presupuesto grabado; null si el alta fue inválida.</summary>
    public PresupuestoGrabado? Presupuesto { get; }

    /// <summary>Errores de validación; vacío si el presupuesto se grabó.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    [MemberNotNullWhen(true, nameof(Presupuesto))]
    public bool EsGrabado => Presupuesto is not null;

    public static ResultadoAltaPresupuesto Grabado(PresupuestoGrabado presupuesto)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        return new ResultadoAltaPresupuesto(presupuesto, []);
    }

    public static ResultadoAltaPresupuesto Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Un alta inválida necesita al menos un error.", nameof(errores));
        }

        return new ResultadoAltaPresupuesto(null, [.. errores]);
    }
}

/// <summary>
/// Da de alta un presupuesto en Borrador con su cliente y sus líneas del catálogo (FR-01 a FR-15). Si el cliente o la
/// forma de las líneas incumplen <see cref="ValidacionPresupuesto.Validar"/>, devuelve
/// <see cref="ResultadoAltaPresupuesto.Invalido"/> con todos los errores sin leer la base. Si no, dentro de una operación
/// en transacción (ADR-013): lee cada artículo, arma la línea con la Cantidad y el Descuento pedidos
/// (<see cref="LineaPresupuesto.DesdeArticulo"/>) y rechaza junto a la línea uno inexistente, inactivo o con precio de
/// venta negativo (D-14); si todos sirven, controla con <see cref="ValidacionPresupuesto.ControlarTopes"/> el Precio
/// Final de cada línea y el total (D-04). Cualquier rechazo termina sin registrar nada; si no hay ninguno, lee el
/// último número, toma el siguiente (D-07), toma la fecha local del día con <see cref="TimeProvider.GetLocalNow"/>
/// (D-06), registra el presupuesto y confirma. La relectura del presupuesto grabado va después.
/// </summary>
/// <remarks>
/// Un <see cref="ConflictoAlConfirmarException"/> (número repetido) y cualquier otra falla al confirmar no se atienden:
/// la operación revierte y la excepción se propaga tal cual, sin envolverla (D-07, D-30). No recibe un registrador: no
/// registra en el log los datos del cliente (D-17).
/// </remarks>
public sealed class AltaPresupuesto(
    IPresupuestoRepository presupuestos,
    IArticuloRepository articulos,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj)
{
    public async Task<ResultadoAltaPresupuesto> EjecutarAsync(
        DatosPresupuesto datos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var errores = ValidacionPresupuesto.Validar(datos);
        if (errores.Count > 0)
        {
            return ResultadoAltaPresupuesto.Invalido(errores);
        }

        // La validación ya aceptó el cliente: existe, con apellido, nombre y DNI.
        var datosCliente = datos.Cliente!;
        var cliente = new ClientePresupuesto(
            datosCliente.Apellido!, datosCliente.Nombre!, datosCliente.Dni!, datosCliente.Domicilio,
            datosCliente.Email, datosCliente.Telefono);
        // La validación ya aceptó cada línea: existe, con código, cantidad y descuento válidos.
        var lineas = datos.Lineas!
            .Select(linea => new LineaPedida(linea!.CodigoArticulo!.Value, linea.Cantidad!.Value,
                linea.PorcentajeDescuento!.Value))
            .ToList();

        // ADR-013 y D-07: los artículos, el último número y la fecha se leen en la misma transacción de escritura que
        // graba, así ninguna otra grabación confirma entre la lectura del número y la del presupuesto.
        var desenlace = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => GrabarEnTransaccionAsync(cliente, lineas, operacion), cancelacion);
        if (desenlace.Rechazo is { } rechazo)
        {
            return rechazo;
        }

        // Después de la operación. Sin cancelación a propósito, como el alta de artículos: el presupuesto ya quedó
        // confirmado, y cortar esta lectura haría que la operadora lo grabara de nuevo con otro número.
        var grabado = await presupuestos.ObtenerAsync(desenlace.Numero, CancellationToken.None)
            ?? throw new InvalidOperationException("El presupuesto confirmado no se encontró por su número: estado corrupto.");
        return ResultadoAltaPresupuesto.Grabado(PresupuestoGrabado.Desde(grabado));
    }

    private async Task<Desenlace> GrabarEnTransaccionAsync(
        ClientePresupuesto cliente, IReadOnlyList<LineaPedida> pedidas, CancellationToken operacion)
    {
        var resultados = new List<ResultadoLinea>(pedidas.Count);
        var errores = new List<ErrorValidacion>();
        for (var indice = 0; indice < pedidas.Count; indice++)
        {
            var pedida = pedidas[indice];
            var articulo = await articulos.ObtenerAsync(pedida.CodigoArticulo, operacion);
            if (articulo is null)
            {
                errores.Add(ValidacionPresupuesto.ArticuloNoDisponible(indice));
                continue;
            }

            var resultado = LineaPresupuesto.DesdeArticulo(articulo, pedida.Cantidad, pedida.PorcentajeDescuento);
            if (resultado.EsValida || resultado.EsImporteExcedido)
            {
                // Los topes de importe se controlan juntos, después (D-11).
                resultados.Add(resultado);
            }
            else if (resultado.EsPrecioNegativo)
            {
                errores.Add(ValidacionPresupuesto.PrecioNegativo(indice));
            }
            else if (resultado.EsInactivo)
            {
                errores.Add(ValidacionPresupuesto.ArticuloNoDisponible(indice));
            }
            else
            {
                throw new UnreachableException("Rechazo de línea sin error asignado.");
            }
        }

        // Los rechazos del catálogo no dependen de la cantidad ni del descuento: se informan primero. Si no hay, los
        // topes del contrato (D-04, D-17), antes de construir el presupuesto: su constructor lanza con un total fuera
        // del tipo, y así un importe excedido termina en un error por campo y nunca en una excepción.
        if (errores.Count == 0)
        {
            errores.AddRange(ValidacionPresupuesto.ControlarTopes(resultados));
        }

        if (errores.Count > 0)
        {
            // D-14: un rechazo no graba nada; la operación termina sin cambios registrados.
            return new Desenlace(0, ResultadoAltaPresupuesto.Invalido(errores));
        }

        var numero = NumeracionPresupuesto.Siguiente(await presupuestos.ObtenerUltimoNumeroAsync(operacion));
        // D-06: la fecha calendario de la PC, no la UTC.
        var fecha = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
        presupuestos.Agregar(Presupuesto.Nuevo(numero, fecha, cliente, LineasValidas(resultados)));

        // D-07 y D-30: un conflicto o cualquier otra falla al confirmar se propaga sin atenderla ni envolverla.
        await unidadDeTrabajo.ConfirmarAsync(operacion);
        return new Desenlace(numero, null);
    }

    /// <summary>Las líneas de los resultados, en orden. Sin errores, ControlarTopes ya aceptó todas como válidas.</summary>
    /// <exception cref="UnreachableException">Algún resultado no tiene línea: el control previo no se hizo.</exception>
    private static List<LineaPresupuesto> LineasValidas(IReadOnlyList<ResultadoLinea> resultados)
    {
        var lineas = new List<LineaPresupuesto>(resultados.Count);
        foreach (var resultado in resultados)
        {
            if (!resultado.EsValida)
            {
                throw new UnreachableException("Línea rechazada después de controlar los topes.");
            }

            lineas.Add(resultado.Linea);
        }

        return lineas;
    }

    /// <summary>El número grabado, o el rechazo de alguna línea (sin grabar nada).</summary>
    private sealed record Desenlace(long Numero, ResultadoAltaPresupuesto? Rechazo);

    /// <summary>Una línea ya validada por <see cref="ValidacionPresupuesto.Validar"/>, sin el artículo leído.</summary>
    /// <remarks>Sin valores en <see cref="object.ToString"/>, como <see cref="DatosLinea"/> (D-17).</remarks>
    private sealed record LineaPedida(long CodigoArticulo, int Cantidad, decimal PorcentajeDescuento)
    {
        private bool PrintMembers(System.Text.StringBuilder constructor) => false;
    }
}
