using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Application.Catalogo;

/// <summary>
/// Cómo terminó la carga de una planilla de costos (FR-01 a FR-15 de FEAT-011): <see cref="Aplicada"/>, con el
/// resultado, o uno de los rechazos que no tocan la base: <see cref="NoEsXlsx"/>, <see cref="FormatoInesperado"/> o
/// <see cref="ExcedeLimites"/>. Los rechazos no llevan datos del archivo (R-05).
/// </summary>
public abstract record ResultadoPlanillaCostos
{
    private ResultadoPlanillaCostos()
    {
    }

    /// <summary>
    /// La planilla se procesó y lo aplicable quedó confirmado en una sola transacción (FR-12, D-15).
    /// </summary>
    /// <remarks>
    /// Datos sensibles: cada <see cref="ResultadoFilaPlanilla"/> de <see cref="NoAplicadas"/> y <see cref="ParaRevisar"/>
    /// expone el código tal como vino en la planilla, sin acotar (<see cref="ResultadoFilaPlanilla.Codigo"/>), y en las
    /// aplicadas el <see cref="Articulo"/> completo: costo, margen, precio de venta, estado y versión. Son datos
    /// comerciales de la empresa y de sus proveedores. No se serializan tal cual ni van al log (R-05): el controlador los
    /// pasa a su propio DTO con la fila, el código acotado (D-10), la razón, las indicaciones y el precio de costo. La
    /// prueba de ese contrato es del Block 4.
    /// </remarks>
    /// <param name="CantidadFilasLeidas">Las filas de datos de la planilla, sin la de encabezados.</param>
    /// <param name="CantidadAplicadas">
    /// Las filas aplicadas, también las que no cambiaban nada porque el costo y la venta ya eran esos (D-13).
    /// </param>
    /// <param name="NoAplicadas">Las filas no aplicadas con su razón, en el orden de la hoja (FR-13).</param>
    /// <param name="ParaRevisar">
    /// Las filas aplicadas con costo cero o negativo (FR-14) o sobre un artículo desactivado (FR-15), en el orden de la
    /// hoja; cada una dice cuál con <see cref="ResultadoFilaPlanilla.Indicaciones"/>.
    /// </param>
    public sealed record Aplicada(
        int CantidadFilasLeidas,
        int CantidadAplicadas,
        IReadOnlyList<ResultadoFilaPlanilla> NoAplicadas,
        IReadOnlyList<ResultadoFilaPlanilla> ParaRevisar) : ResultadoPlanillaCostos
    {
        /// <summary>Las filas no aplicadas; nunca null.</summary>
        public IReadOnlyList<ResultadoFilaPlanilla> NoAplicadas { get; } =
            NoAplicadas ?? throw new ArgumentNullException(nameof(NoAplicadas));

        /// <summary>Las filas aplicadas para revisar; nunca null.</summary>
        public IReadOnlyList<ResultadoFilaPlanilla> ParaRevisar { get; } =
            ParaRevisar ?? throw new ArgumentNullException(nameof(ParaRevisar));
    }

    /// <summary>El archivo no es un .xlsx legible (FR-02, R-09): no se abrió la transacción.</summary>
    public sealed record NoEsXlsx : ResultadoPlanillaCostos
    {
        /// <summary>La única instancia: el rechazo no lleva datos.</summary>
        public static NoEsXlsx Instancia { get; } = new();
    }

    /// <summary>
    /// La fila 1 no tiene los encabezados del formato o hay columnas de más (FR-03, FR-04): no se evaluó ninguna fila ni
    /// se abrió la transacción.
    /// </summary>
    public sealed record FormatoInesperado : ResultadoPlanillaCostos
    {
        /// <summary>La única instancia: el rechazo no lleva datos.</summary>
        public static FormatoInesperado Instancia { get; } = new();
    }

    /// <summary>La planilla pasa un límite de D-14 (R-02): no se abrió la transacción.</summary>
    public sealed record ExcedeLimites : ResultadoPlanillaCostos
    {
        /// <summary>La única instancia: el rechazo no lleva datos.</summary>
        public static ExcedeLimites Instancia { get; } = new();
    }
}

/// <summary>
/// Actualiza los precios de costo del catálogo con una planilla .xlsx del proveedor y recalcula los precios de venta
/// (FR-01 a FR-15 de FEAT-011).
/// </summary>
/// <remarks>
/// <para>
/// Lee la planilla con <see cref="ILectorPlanillaCostos"/> fuera de la transacción: "no es .xlsx" y "excede límites"
/// vuelven como rechazo sin tocar la base, y una fila 1 que no cumple <see cref="PlanillaCostos.TieneFormatoEsperado"/>,
/// como <see cref="ResultadoPlanillaCostos.FormatoInesperado"/> sin evaluar filas (FR-04).
/// </para>
/// <para>
/// Si no, dentro de <see cref="IUnidadDeTrabajo.EjecutarEnTransaccionAsync"/> (ADR-013): lee la configuración vigente y
/// el catálogo entero una sola vez, arma un diccionario por <see cref="Articulo.CodigoProveedorClave"/> con comparación
/// ordinal (la clave ya viene normalizada, ADR-009), evalúa las filas con <see cref="PlanillaCostos.Evaluar"/>, registra
/// con <see cref="IArticuloRepository.RegistrarCostoActualizado"/> solo las aplicadas que cambian (D-13) y confirma una
/// sola vez: todo o nada (D-15, R-06). Un conflicto de concurrencia (<see cref="ConflictoAlConfirmarException"/>) o
/// cualquier otra falla revierte la transacción, se propaga y no se registra la carga.
/// </para>
/// <para>
/// Después del commit registra en el log, con la categoría <see cref="CategoriaRegistro"/>, solo cantidades y la fecha y
/// hora UTC (D-16, R-07): nunca códigos, precios, textos de celdas ni el nombre del archivo, que son datos comerciales de
/// la empresa y de sus proveedores (R-05).
/// </para>
/// <para>
/// Las cargas se serializan con el <see cref="CandadoCargaPlanilla"/> único (singleton en la Api) alrededor de la lectura
/// y la transacción (D-23, D-25).
/// </para>
/// </remarks>
public sealed partial class ActualizarCostosPorPlanilla(
    ILectorPlanillaCostos lector,
    IConfiguracionNegocioRepository configuracion,
    IArticuloRepository articulos,
    IUnidadDeTrabajo unidadDeTrabajo,
    TimeProvider reloj,
    ILoggerFactory fabricaRegistros,
    CandadoCargaPlanilla candado)
{
    /// <summary>
    /// La categoría del registro de cada carga confirmada (D-16). <c>appsettings.json</c> la envía también al Registro
    /// de eventos de Windows con esta clave exacta (R-07).
    /// </summary>
    public const string CategoriaRegistro = "Optica.Application.Catalogo.Planilla";

    private const string MensajeClaveRepetida = "El catálogo tiene dos artículos con la misma clave.";

    private const string MensajeLecturaDesconocida = "El lector de la planilla devolvió una lectura desconocida.";

    private readonly CandadoCargaPlanilla _candado = candado ?? throw new ArgumentNullException(nameof(candado));

    private readonly ILogger _registro =
        (fabricaRegistros ?? throw new ArgumentNullException(nameof(fabricaRegistros))).CreateLogger(CategoriaRegistro);

    /// <summary>
    /// Carga la planilla: el controlador ya exigió bytes no vacíos y acotó el tamaño. Si otra carga está en curso,
    /// espera su turno en fila, sin mensaje nuevo (D-23).
    /// </summary>
    /// <exception cref="ConflictoAlConfirmarException">
    /// Un artículo cambió de versión o de estado entre la lectura y la confirmación: no se aplicó nada (D-15).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Se canceló mientras esperaba su turno (no leyó ni tocó la base) o durante la transacción (se revirtió).
    /// </exception>
    public async Task<ResultadoPlanillaCostos> EjecutarAsync(
        ReadOnlyMemory<byte> archivo, CancellationToken cancelacion = default)
    {
        await _candado.TomarAsync(cancelacion);
        try
        {
            return await LeerYAplicarAsync(archivo, cancelacion);
        }
        finally
        {
            _candado.Liberar();
        }
    }

    private async Task<ResultadoPlanillaCostos> LeerYAplicarAsync(
        ReadOnlyMemory<byte> archivo, CancellationToken cancelacion)
    {
        // Fuera de la transacción: leer el archivo no toma el bloqueo de escritura (R-04).
        ContenidoPlanilla contenido;
        switch (lector.Leer(archivo))
        {
            case LecturaPlanilla.Leida leida:
                contenido = leida.Contenido;
                break;
            case LecturaPlanilla.ExcedeLimites:
                return ResultadoPlanillaCostos.ExcedeLimites.Instancia;
            case LecturaPlanilla.NoEsXlsx:
                return ResultadoPlanillaCostos.NoEsXlsx.Instancia;
            default:
                // El lector no cumplió su contrato (null): error de programación. Mensaje fijo, sin datos del archivo.
                throw new UnreachableException(MensajeLecturaDesconocida);
        }

        if (!PlanillaCostos.TieneFormatoEsperado(contenido))
        {
            return ResultadoPlanillaCostos.FormatoInesperado.Instancia;
        }

        var filas = await unidadDeTrabajo.EjecutarEnTransaccionAsync(
            operacion => AplicarEnTransaccionAsync(contenido.Filas, operacion), cancelacion);

        // Después del commit de la operación: se registra solo lo confirmado (R-07).
        var aplicadas = filas.Where(fila => fila.EsAplicada).ToList();
        var noAplicadas = filas.Where(fila => !fila.EsAplicada).ToList();
        var paraRevisar = aplicadas.Where(fila => fila.PrecioNoPositivo || fila.Inactivo).ToList();
        RegistrarCarga(
            _registro,
            filas.Count,
            aplicadas.Count,
            noAplicadas.Count(fila => fila.Razon == RazonNoAplicada.CodigoInexistente),
            noAplicadas.Count(fila => fila.Razon == RazonNoAplicada.PrecioInvalido),
            noAplicadas.Count(fila => fila.Razon == RazonNoAplicada.CodigoRepetido),
            aplicadas.Count(fila => fila.PrecioNoPositivo),
            aplicadas.Count(fila => fila.Inactivo),
            reloj.GetUtcNow().UtcDateTime);

        return new ResultadoPlanillaCostos.Aplicada(filas.Count, aplicadas.Count, noAplicadas, paraRevisar);
    }

    // Todo lo que lee y graba, dentro de la transacción de escritura (ADR-013): una lectura del catálogo, un diccionario
    // en memoria y una sola confirmación (NFR-01, R-04, D-15).
    private async Task<IReadOnlyList<ResultadoFilaPlanilla>> AplicarEnTransaccionAsync(
        IReadOnlyList<FilaPlanilla> filas, CancellationToken cancelacion)
    {
        var vigente = await configuracion.ObtenerAsync(cancelacion);
        var catalogo = await articulos.ListarParaRecalculoAsync(cancelacion);
        var porClave = new Dictionary<string, Articulo>(catalogo.Count, StringComparer.Ordinal);
        foreach (var articulo in catalogo)
        {
            // La base la hace única (ADR-009). Sin Add: su mensaje llevaría la clave, un dato del proveedor (R-05).
            if (!porClave.TryAdd(articulo.CodigoProveedorClave, articulo))
            {
                throw new InvalidOperationException(MensajeClaveRepetida);
            }
        }

        var resultados = PlanillaCostos.Evaluar(filas, porClave, vigente);
        foreach (var resultado in resultados)
        {
            // D-13: una fila sin cambios cuenta como aplicada y no se escribe.
            if (resultado.EsAplicada && resultado.Cambia)
            {
                articulos.RegistrarCostoActualizado(resultado.Articulo);
            }
        }

        // D-15: todo en una sola confirmación.
        await unidadDeTrabajo.ConfirmarAsync(cancelacion);
        return resultados;
    }

    // Solo cantidades y la fecha: nunca códigos, precios, textos de celdas ni el nombre del archivo (D-16, R-05).
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Planilla de costos cargada. Filas leídas: {FilasLeidas}. Aplicadas: {Aplicadas}. No aplicadas: " +
            "{CodigoInexistente} por código inexistente, {PrecioInvalido} por precio inválido y {CodigoRepetido} por " +
            "código repetido en la planilla. Aplicadas con precio negativo o cero: {PrecioNoPositivo}. Artículos " +
            "inactivos actualizados: {Inactivos}. Fecha y hora UTC: {FechaHoraUtc:O}.")]
    private static partial void RegistrarCarga(
        ILogger registro,
        int filasLeidas,
        int aplicadas,
        int codigoInexistente,
        int precioInvalido,
        int codigoRepetido,
        int precioNoPositivo,
        int inactivos,
        DateTime fechaHoraUtc);
}
