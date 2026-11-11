using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Presupuestos;

/// <summary>Cómo terminó una modificación o un paso a Final de un presupuesto (FEAT-003c).</summary>
public enum TipoEdicionPresupuesto
{
    Grabado,
    Invalido,
    NoEncontrado,
    Final,
    Cambiado,
}

/// <summary>
/// El resultado de <see cref="ModificarPresupuesto"/> y de <see cref="PasarPresupuestoAFinal"/>: grabado, con el
/// presupuesto releído; inválido, con un error por campo; no encontrado; en Final (FR-05) o cambiado por otra sesión
/// (D-01), con el texto fijo de <see cref="Detalle"/>, que no repite datos recibidos.
/// </summary>
/// <remarks>
/// Lleva el presupuesto con los datos del cliente: <see cref="object.ToString"/> no se sobrescribe y muestra solo el
/// nombre del tipo, y <see cref="PresupuestoGrabado"/> no muestra valores (D-17 de FEAT-003a).
/// </remarks>
public sealed class ResultadoEdicionPresupuesto
{
    private ResultadoEdicionPresupuesto(
        TipoEdicionPresupuesto tipo, PresupuestoGrabado? presupuesto, IReadOnlyList<ErrorValidacion> errores,
        string? detalle)
    {
        Tipo = tipo;
        Presupuesto = presupuesto;
        Errores = errores;
        Detalle = detalle;
    }

    /// <summary>El presupuesto no existe.</summary>
    public static ResultadoEdicionPresupuesto NoEncontrado { get; } =
        new(TipoEdicionPresupuesto.NoEncontrado, null, [], null);

    /// <summary>El presupuesto está en Final y no se modificó (FR-05, FR-06, D-14).</summary>
    public static ResultadoEdicionPresupuesto Final { get; } =
        new(TipoEdicionPresupuesto.Final, null, [], ValidacionPresupuesto.DetalleFinal);

    /// <summary>Otra sesión grabó el presupuesto entre la lectura de la pantalla y la grabación (D-01).</summary>
    public static ResultadoEdicionPresupuesto Cambiado { get; } =
        new(TipoEdicionPresupuesto.Cambiado, null, [], ValidacionPresupuesto.DetalleCambiado);

    public TipoEdicionPresupuesto Tipo { get; }

    /// <summary>El presupuesto releído; null salvo cuando <see cref="Tipo"/> es <see cref="TipoEdicionPresupuesto.Grabado"/>.</summary>
    public PresupuestoGrabado? Presupuesto { get; }

    /// <summary>Errores de validación; vacío salvo cuando <see cref="Tipo"/> es <see cref="TipoEdicionPresupuesto.Invalido"/>.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    /// <summary>El texto fijo de un rechazo por Final o por cambio; null en los demás resultados.</summary>
    public string? Detalle { get; }

    [MemberNotNullWhen(true, nameof(Presupuesto))]
    public bool EsGrabado => Tipo == TipoEdicionPresupuesto.Grabado && Presupuesto is not null;

    public static ResultadoEdicionPresupuesto Grabado(PresupuestoGrabado presupuesto)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        return new ResultadoEdicionPresupuesto(TipoEdicionPresupuesto.Grabado, presupuesto, [], null);
    }

    public static ResultadoEdicionPresupuesto Invalido(IReadOnlyList<ErrorValidacion> errores)
    {
        if (errores is null || errores.Count == 0)
        {
            throw new ArgumentException("Una edición inválida necesita al menos un error.", nameof(errores));
        }

        return new ResultadoEdicionPresupuesto(TipoEdicionPresupuesto.Invalido, null, [.. errores], null);
    }
}

/// <summary>
/// Modifica el cliente y las líneas de un presupuesto en Borrador (FR-01, FR-02, FR-07 de FEAT-003c). Antes de leer la
/// base valida la versión, el cliente, la cantidad y el descuento con las reglas del alta, y la forma del <c>orden</c>
/// (<see cref="ValidacionPresupuesto.ValidarModificacion"/>): con algún error, inválido sin leer nada. Si no, dentro de
/// una operación en transacción (ADR-013): lee el presupuesto (inexistente, no encontrado; en Final, final; con otra
/// versión, cambiado) y recién entonces valida que cada <c>orden</c> exista y tenga el mismo código
/// (<see cref="ValidacionPresupuesto.LineaGrabadaDe"/>, D-10). Una línea con <c>orden</c> conserva su copia del catálogo
/// y recalcula con la Cantidad y el Descuento nuevos (<see cref="LineaPresupuesto.ConCantidadYDescuento"/>, D-02); una
/// sin <c>orden</c> se arma con el artículo activo del catálogo, como en el alta. Con todas válidas controla los topes
/// (<see cref="ValidacionPresupuesto.ControlarTopes"/>), aplica <see cref="Presupuesto.Modificar"/> (las líneas quedan
/// 1..n en el orden recibido; la fecha y el número no cambian, D-06), registra la modificación con la versión enviada y
/// confirma. Grabado devuelve el presupuesto releído después de la operación.
/// </summary>
/// <remarks>
/// Un <see cref="ConflictoAlConfirmarException"/> se filtra por motivo: <see cref="MotivoConflicto.Concurrencia"/> relee
/// y decide entre no encontrado, final y cambiado; <see cref="MotivoConflicto.RegistroCerrado"/> (otra sesión lo pasó a
/// Final, D-14) es final; cualquier otro motivo, y cualquier otra falla al confirmar (por ejemplo, la
/// <c>DbUpdateException</c> del trigger de la fecha y el número, cuyas <c>Entries</c> llevan datos del cliente), se
/// propaga sin atenderla ni envolverla. No recibe un registrador: no registra datos del cliente (D-17 de FEAT-003a).
/// </remarks>
public sealed class ModificarPresupuesto(
    IPresupuestoRepository presupuestos,
    IArticuloRepository articulos,
    IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<ResultadoEdicionPresupuesto> EjecutarAsync(
        long numero, DatosPresupuesto datos, long? version, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var errores = ValidacionPresupuesto.ValidarModificacion(datos, version);
        if (errores.Count > 0)
        {
            return ResultadoEdicionPresupuesto.Invalido(errores);
        }

        var versionEnviada = version!.Value;
        // La validación ya aceptó el cliente: existe, con apellido, nombre y DNI.
        var cliente = ArmadoLineas.Cliente(datos.Cliente!);
        // La validación ya aceptó cada línea: existe, con código, cantidad y descuento válidos.
        var pedidas = ArmadoLineas.Pedidas(datos.Lineas!);

        // ADR-013: la lectura del presupuesto y de los artículos, la grabación y la atención de un conflicto, en una sola
        // transacción de escritura. Null: la modificación se confirmó.
        var rechazo = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => GrabarEnTransaccionAsync(numero, versionEnviada, cliente, pedidas, operacion), cancelacion);
        if (rechazo is not null)
        {
            return rechazo;
        }

        // Después de la operación. Sin cancelación a propósito, como el alta: la modificación ya quedó confirmada.
        var grabado = await presupuestos.ObtenerAsync(numero, CancellationToken.None)
            ?? throw new InvalidOperationException("El presupuesto confirmado no se encontró por su número: estado corrupto.");
        return ResultadoEdicionPresupuesto.Grabado(PresupuestoGrabado.Desde(grabado));
    }

    private async Task<ResultadoEdicionPresupuesto?> GrabarEnTransaccionAsync(
        long numero, long versionEnviada, ClientePresupuesto cliente, IReadOnlyList<LineaPedida> pedidas,
        CancellationToken operacion)
    {
        var leido = await presupuestos.ObtenerAsync(numero, operacion);
        if (leido is null)
        {
            return ResultadoEdicionPresupuesto.NoEncontrado;
        }

        if (leido.Estado == EstadoPresupuesto.Final)
        {
            // FR-05: aunque la versión también haya cambiado, la respuesta es final (D-14).
            return ResultadoEdicionPresupuesto.Final;
        }

        if (leido.Version != versionEnviada)
        {
            // D-10: la versión se compara antes que el orden, que se refiere a las líneas que leyó la pantalla.
            return ResultadoEdicionPresupuesto.Cambiado;
        }

        var resultados = new List<ResultadoLinea>(pedidas.Count);
        var errores = new List<ErrorValidacion>();
        for (var indice = 0; indice < pedidas.Count; indice++)
        {
            var pedida = pedidas[indice];
            if (pedida.Orden is { } orden)
            {
                // D-02: la línea grabada conserva Código, Descripción y Precio Unitario, sin consultar el catálogo.
                if (ValidacionPresupuesto.LineaGrabadaDe(orden, pedida.CodigoArticulo, leido.Lineas) is { } grabada)
                {
                    resultados.Add(grabada.ConCantidadYDescuento(pedida.Cantidad, pedida.PorcentajeDescuento));
                }
                else
                {
                    errores.Add(ValidacionPresupuesto.LineaAjena(indice));
                }

                continue;
            }

            // Como en el alta: el artículo activo del catálogo, con los mismos rechazos.
            var nueva = await ArmadoLineas.LineaNuevaAsync(articulos, pedida, indice, operacion);
            if (nueva.Error is { } error)
            {
                errores.Add(error);
            }
            else
            {
                resultados.Add(nueva.Resultado ?? throw new UnreachableException("Línea nueva sin resultado ni error."));
            }
        }

        // Como en el alta: los rechazos de las líneas primero; si no hay, los topes del contrato (D-04 de FEAT-003b).
        if (errores.Count == 0)
        {
            errores.AddRange(ValidacionPresupuesto.ControlarTopes(resultados));
        }

        if (errores.Count > 0)
        {
            // Un rechazo no graba nada: la operación termina sin cambios registrados.
            return ResultadoEdicionPresupuesto.Invalido(errores);
        }

        var modificado = leido.Modificar(cliente, ArmadoLineas.LineasValidas(resultados));
        presupuestos.Actualizar(leido, modificado, versionEnviada);

        try
        {
            await unidadDeTrabajo.ConfirmarAsync(operacion);
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.Concurrencia)
        {
            // D-01: no se guardó nada; la relectura dice por qué. Dentro de la transacción la rama queda como defensa
            // ante un escritor externo.
            var releido = await presupuestos.ObtenerAsync(numero, operacion);
            return releido switch
            {
                null => ResultadoEdicionPresupuesto.NoEncontrado,
                { Estado: EstadoPresupuesto.Final } => ResultadoEdicionPresupuesto.Final,
                _ => ResultadoEdicionPresupuesto.Cambiado,
            };
        }
        catch (ConflictoAlConfirmarException conflicto) when (conflicto.Motivo == MotivoConflicto.RegistroCerrado)
        {
            // D-14: otra sesión lo pasó a Final; el trigger de cierre rechazó las líneas antes que la versión.
            return ResultadoEdicionPresupuesto.Final;
        }

        return null;
    }
}
