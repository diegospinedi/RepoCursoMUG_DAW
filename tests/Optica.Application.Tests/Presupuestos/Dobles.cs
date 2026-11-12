using Optica.Application.Comun;
using Optica.Application.Tests.Catalogo;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

/// <summary>
/// Presupuestos y catálogo en una sola unidad de trabajo en memoria, como el contexto compartido real:
/// <see cref="Agregar"/> deja el presupuesto pendiente y <see cref="ConfirmarAsync"/> lo guarda junto con lo registrado
/// en <see cref="Articulos"/>, todo o nada; un número ya guardado falla con <see cref="ConflictoAlConfirmarException"/>
/// (motivo <see cref="MotivoConflicto.Unicidad"/>), como la clave primaria. <see cref="Actualizar"/> y
/// <see cref="CambiarEstado"/> simulan el token de versión (<see cref="MotivoConflicto.Concurrencia"/>) y los triggers de
/// cierre (<see cref="MotivoConflicto.RegistroCerrado"/>), y al confirmar dejan la versión leída + 1 (FEAT-003c).
/// </summary>
/// <remarks>
/// <see cref="EjecutarEnTransaccionAsync"/> usa la operación de <see cref="Articulos"/> (copia al entrar, rechazo del
/// anidamiento y de lo registrado sin confirmar, falla simulada del commit externo con
/// <see cref="ArticulosEnMemoria.ErrorAlConfirmarOperacion"/>) y además restaura los presupuestos si algo lanza. Cada
/// lectura y registro de presupuestos se anota en <see cref="ArticulosEnMemoria.Accesos"/>, junto a las lecturas de
/// artículos, para fijar el límite de la operación (ADR-013, D-07).
/// </remarks>
internal sealed class PresupuestosEnMemoria : IPresupuestoRepository, IUnidadDeTrabajo
{
    public const string AccesoUltimoNumero = nameof(ObtenerUltimoNumeroAsync);
    public const string AccesoAgregar = "AgregarPresupuesto";
    public const string AccesoObtener = "ObtenerPresupuesto";
    public const string AccesoActualizar = "ActualizarPresupuesto";
    public const string AccesoCambiarEstado = "CambiarEstadoPresupuesto";

    private readonly List<Presupuesto> _pendientes = [];

    /// <summary>Modificaciones y pasos a Final registrados, con la versión leída, en el orden en que llegaron.</summary>
    private readonly List<(Presupuesto Presupuesto, long VersionOriginal, bool EsActualizacion)> _cambios = [];

    public ArticulosEnMemoria Articulos { get; } = new();

    public List<Presupuesto> Guardados { get; } = [];

    public int Agregados { get; private set; }

    /// <summary>Guarda un presupuesto ya confirmado, como si existiera de antes, con datos ficticios.</summary>
    public Presupuesto Sembrar(long numero, EstadoPresupuesto estado = EstadoPresupuesto.Borrador, long version = 0)
    {
        var presupuesto = new Presupuesto(
            numero,
            new DateOnly(2026, 1, 5),
            estado,
            new ClientePresupuesto("Gómez", "Luis", "30111222", null, null, null),
            [new LineaPresupuesto(1, "Lente orgánico", 100m, 1, 0m, 100m, 100m)],
            version);
        Guardados.Add(presupuesto);
        return presupuesto;
    }

    /// <summary>Guarda un artículo en el catálogo con ese código y ese precio de venta.</summary>
    public Articulo SembrarArticulo(long codigo, string descripcion, decimal precioVenta, bool activo = true)
    {
        var articulo = new Articulo(
            codigo, $"PRV-{codigo}", descripcion, 1000m, 85m, precioVenta, activo, version: 0);
        Articulos.Guardados.Add(articulo);
        return articulo;
    }

    public Task<long?> ObtenerUltimoNumeroAsync(CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        Articulos.Anotar(AccesoUltimoNumero);
        return Task.FromResult(Guardados.Count == 0 ? (long?)null : Guardados.Max(presupuesto => presupuesto.Numero));
    }

    /// <exception cref="ArgumentException">El presupuesto no está en Borrador, como en el repositorio real.</exception>
    public void Agregar(Presupuesto presupuesto)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        if (presupuesto.Estado != EstadoPresupuesto.Borrador)
        {
            throw new ArgumentException("Un presupuesto nuevo se graba en Borrador.", nameof(presupuesto));
        }

        Agregados++;
        Articulos.Anotar(AccesoAgregar);
        _pendientes.Add(presupuesto);
    }

    public Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default)
    {
        cancelacion.ThrowIfCancellationRequested();
        Articulos.Anotar(AccesoObtener);
        return Task.FromResult(Guardados.SingleOrDefault(presupuesto => presupuesto.Numero == numero));
    }

    /// <summary>
    /// Registra la modificación; al confirmar, como la base: si el guardado está en Final falla con
    /// <see cref="MotivoConflicto.RegistroCerrado"/> aunque la versión también haya cambiado, y si no, con una versión
    /// guardada distinta de <paramref name="versionOriginal"/>, con <see cref="MotivoConflicto.Concurrencia"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Los dos presupuestos tienen distinto número, o la versión es negativa o la máxima, como en el repositorio real.
    /// </exception>
    public void Actualizar(Presupuesto original, Presupuesto modificado, long versionOriginal)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(modificado);
        if (modificado.Numero != original.Numero)
        {
            throw new ArgumentException("El presupuesto modificado no es el mismo que el original.", nameof(modificado));
        }

        ValidarVersionOriginal(versionOriginal);
        Articulos.Anotar(AccesoActualizar);
        _cambios.Add((modificado, versionOriginal, EsActualizacion: true));
    }

    /// <summary>
    /// Registra el paso a Final; al confirmar, una versión distinta falla con <see cref="MotivoConflicto.Concurrencia"/>
    /// antes de mirar el estado, como el <c>UPDATE ... WHERE Version</c> que no encuentra la fila.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El presupuesto no está en Final, o la versión es negativa o la máxima, como en el repositorio real.
    /// </exception>
    public void CambiarEstado(Presupuesto presupuesto, long versionOriginal)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
        if (presupuesto.Estado != EstadoPresupuesto.Final)
        {
            throw new ArgumentException(
                "Solo se registra el paso a Final de un presupuesto en Final.", nameof(presupuesto));
        }

        ValidarVersionOriginal(versionOriginal);
        Articulos.Anotar(AccesoCambiarEstado);
        _cambios.Add((presupuesto, versionOriginal, EsActualizacion: false));
    }

    private static void ValidarVersionOriginal(long versionOriginal)
    {
        if (versionOriginal is < 0 or >= Presupuesto.VersionMaxima)
        {
            throw new ArgumentException("La versión leída del presupuesto no es válida.", nameof(versionOriginal));
        }
    }

    public async Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> operacion, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        var guardadosAlEntrar = new List<Presupuesto>(Guardados);
        try
        {
            return await Articulos.EjecutarEnTransaccionAsync(
                async token =>
                {
                    var resultado = await operacion(token);
                    if (_pendientes.Count > 0 || _cambios.Count > 0)
                    {
                        throw new InvalidOperationException("La operación terminó con cambios sin confirmar.");
                    }

                    return resultado;
                },
                cancelacion);
        }
        catch
        {
            Guardados.Clear();
            Guardados.AddRange(guardadosAlEntrar);
            _pendientes.Clear();
            _cambios.Clear();
            throw;
        }
    }

    public async Task ConfirmarAsync(CancellationToken cancelacion = default)
    {
        try
        {
            await Articulos.ConfirmarAsync(cancelacion);
            if (_pendientes.Any(pendiente => Guardados.Any(guardado => guardado.Numero == pendiente.Numero))
                || _pendientes.DistinctBy(pendiente => pendiente.Numero).Count() != _pendientes.Count)
            {
                throw new ConflictoAlConfirmarException("Conflicto al confirmar.", MotivoConflicto.Unicidad);
            }

            // El orden de los motivos replica lo que devuelve la base real:
            // - Actualizar sobre un Final da RegistroCerrado aunque la versión sea vieja: sus comandos sobre las líneas
            //   (que se filtran solo por clave) llegan a las filas del Final y el trigger de cierre aborta antes de que
            //   el UPDATE del encabezado informe 0 filas (prueba Actualizar_OtraSesionPasoAFinal_MotivoRegistroCerrado).
            // - CambiarEstado no tiene comandos sobre las líneas: con la versión vieja, el UPDATE ... WHERE Version no
            //   encuentra la fila, el trigger no dispara y el motivo es Concurrencia.
            var reemplazos = new List<(int Indice, Presupuesto Nuevo)>();
            foreach (var (presupuesto, versionOriginal, esActualizacion) in _cambios)
            {
                var indice = Guardados.FindIndex(guardado => guardado.Numero == presupuesto.Numero);
                if (esActualizacion && indice >= 0 && Guardados[indice].Estado == EstadoPresupuesto.Final)
                {
                    throw new ConflictoAlConfirmarException("Conflicto al confirmar.", MotivoConflicto.RegistroCerrado);
                }

                if (indice < 0 || Guardados[indice].Version != versionOriginal)
                {
                    throw new ConflictoAlConfirmarException("Conflicto al confirmar.", MotivoConflicto.Concurrencia);
                }

                if (Guardados[indice].Estado == EstadoPresupuesto.Final)
                {
                    throw new ConflictoAlConfirmarException("Conflicto al confirmar.", MotivoConflicto.RegistroCerrado);
                }

                reemplazos.Add((indice, new Presupuesto(
                    presupuesto.Numero, Guardados[indice].Fecha, presupuesto.Estado, presupuesto.Cliente,
                    presupuesto.Lineas, versionOriginal + 1)));
            }

            Guardados.AddRange(_pendientes);
            foreach (var (indice, nuevo) in reemplazos)
            {
                Guardados[indice] = nuevo;
            }
        }
        finally
        {
            _pendientes.Clear();
            _cambios.Clear();
        }
    }
}

/// <summary>
/// Reloj fijo con su propia zona horaria local: <see cref="TimeProvider.GetLocalNow"/> convierte el instante UTC a esa
/// zona. <see cref="AlLeer"/> se ejecuta en cada lectura (por ejemplo, para anotarla en los accesos).
/// </summary>
internal sealed class RelojLocal(DateTimeOffset utc, TimeZoneInfo zona) : TimeProvider
{
    /// <summary>La hora de Argentina: UTC−3 sin horario de verano, sin depender de la base de zonas del sistema.</summary>
    public static readonly TimeZoneInfo Argentina =
        TimeZoneInfo.CreateCustomTimeZone("Prueba/Argentina", TimeSpan.FromHours(-3), "Argentina", "Argentina");

    public const string AccesoReloj = "Reloj";

    public RelojLocal(DateTimeOffset utc)
        : this(utc, Argentina)
    {
    }

    public Action? AlLeer { get; set; }

    public override TimeZoneInfo LocalTimeZone => zona;

    public override DateTimeOffset GetUtcNow()
    {
        AlLeer?.Invoke();
        return utc;
    }
}
