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
/// El pedido de alta: el cliente y, por cada línea en orden, el código autonumérico del artículo (D-12, D-13). La línea no
/// lleva precio ni descripción: el servidor los copia del catálogo (FR-10, FR-11).
/// </summary>
/// <remarks>Lleva el cliente: <see cref="object.ToString"/> no muestra ningún valor (D-17).</remarks>
public sealed record DatosPresupuesto(DatosCliente? Cliente, IReadOnlyList<long?>? Lineas)
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

/// <summary>Un presupuesto grabado: número, fecha local sin hora, estado, cliente y líneas en el orden cargado.</summary>
/// <remarks>Lleva el cliente: <see cref="object.ToString"/> no muestra ningún valor (D-17).</remarks>
public sealed record PresupuestoGrabado(
    long Numero, DateOnly Fecha, EstadoPresupuesto Estado, ClienteGrabado Cliente, IReadOnlyList<LineaGrabada> Lineas)
{
    private bool PrintMembers(System.Text.StringBuilder constructor) => false;

    internal static PresupuestoGrabado Desde(Presupuesto presupuesto) => new(
        presupuesto.Numero,
        presupuesto.Fecha,
        presupuesto.Estado,
        ClienteGrabado.Desde(presupuesto.Cliente),
        [.. presupuesto.Lineas.Select(LineaGrabada.Desde)]);
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
/// en transacción (ADR-013): lee cada artículo y rechaza junto a la línea uno inexistente, inactivo o con precio de venta
/// negativo (D-14), sin registrar nada; si todos sirven, lee el último número, toma el siguiente (D-07), toma la fecha
/// local del día con <see cref="TimeProvider.GetLocalNow"/> (D-06), registra el presupuesto y confirma. La relectura del
/// presupuesto grabado va después.
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

        // La validación ya aceptó el cliente y cada código: existen y son positivos.
        var datosCliente = datos.Cliente!;
        var cliente = new ClientePresupuesto(
            datosCliente.Apellido!, datosCliente.Nombre!, datosCliente.Dni!, datosCliente.Domicilio,
            datosCliente.Email, datosCliente.Telefono);
        var codigos = datos.Lineas!.Select(codigo => codigo!.Value).ToList();

        // ADR-013 y D-07: los artículos, el último número y la fecha se leen en la misma transacción de escritura que
        // graba, así ninguna otra grabación confirma entre la lectura del número y la del presupuesto.
        var desenlace = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => GrabarEnTransaccionAsync(cliente, codigos, operacion), cancelacion);
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
        ClientePresupuesto cliente, IReadOnlyList<long> codigos, CancellationToken operacion)
    {
        var lineas = new List<LineaPresupuesto>(codigos.Count);
        var errores = new List<ErrorValidacion>();
        for (var indice = 0; indice < codigos.Count; indice++)
        {
            var articulo = await articulos.ObtenerAsync(codigos[indice], operacion);
            if (articulo is null)
            {
                errores.Add(ValidacionPresupuesto.ArticuloNoDisponible(indice));
                continue;
            }

            var resultado = LineaPresupuesto.DesdeArticulo(articulo);
            if (resultado.EsValida)
            {
                lineas.Add(resultado.Linea);
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

        if (errores.Count > 0)
        {
            // D-14: un rechazo no graba nada; la operación termina sin cambios registrados.
            return new Desenlace(0, ResultadoAltaPresupuesto.Invalido(errores));
        }

        var numero = NumeracionPresupuesto.Siguiente(await presupuestos.ObtenerUltimoNumeroAsync(operacion));
        // D-06: la fecha calendario de la PC, no la UTC.
        var fecha = DateOnly.FromDateTime(reloj.GetLocalNow().DateTime);
        presupuestos.Agregar(Presupuesto.Nuevo(numero, fecha, cliente, lineas));

        // D-07 y D-30: un conflicto o cualquier otra falla al confirmar se propaga sin atenderla ni envolverla.
        await unidadDeTrabajo.ConfirmarAsync(operacion);
        return new Desenlace(numero, null);
    }

    /// <summary>El número grabado, o el rechazo de alguna línea (sin grabar nada).</summary>
    private sealed record Desenlace(long Numero, ResultadoAltaPresupuesto? Rechazo);
}
