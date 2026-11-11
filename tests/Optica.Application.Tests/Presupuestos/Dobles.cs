using Optica.Application.Comun;
using Optica.Application.Tests.Catalogo;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Application.Tests.Presupuestos;

/// <summary>
/// Presupuestos y catálogo en una sola unidad de trabajo en memoria, como el contexto compartido real:
/// <see cref="Agregar"/> deja el presupuesto pendiente y <see cref="ConfirmarAsync"/> lo guarda junto con lo registrado
/// en <see cref="Articulos"/>, todo o nada; un número ya guardado falla con <see cref="ConflictoAlConfirmarException"/>
/// (motivo <see cref="MotivoConflicto.Unicidad"/>), como la clave primaria.
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

    private readonly List<Presupuesto> _pendientes = [];

    public ArticulosEnMemoria Articulos { get; } = new();

    public List<Presupuesto> Guardados { get; } = [];

    public int Agregados { get; private set; }

    /// <summary>Guarda un presupuesto ya confirmado, como si existiera de antes, con datos ficticios.</summary>
    public Presupuesto Sembrar(long numero, EstadoPresupuesto estado = EstadoPresupuesto.Borrador)
    {
        var presupuesto = new Presupuesto(
            numero,
            new DateOnly(2026, 1, 5),
            estado,
            new ClientePresupuesto("Gómez", "Luis", "30111222", null, null, null),
            [new LineaPresupuesto(1, "Lente orgánico", 100m, 1, 0m, 100m, 100m)],
            version: 0);
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

    public void Agregar(Presupuesto presupuesto)
    {
        ArgumentNullException.ThrowIfNull(presupuesto);
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
                    if (_pendientes.Count > 0)
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

            Guardados.AddRange(_pendientes);
        }
        finally
        {
            _pendientes.Clear();
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
